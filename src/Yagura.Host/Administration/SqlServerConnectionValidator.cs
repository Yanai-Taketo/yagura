using Microsoft.Data.SqlClient;
using Yagura.Abstractions.Administration;
using Yagura.Storage.SqlServer;

namespace Yagura.Host.Administration;

/// <summary>
/// <see cref="ISqlServerConnectionValidator"/> の実体（M8-4）。
/// </summary>
/// <remarks>
/// 接続を開いて <c>SELECT 1</c> を実行し、続けて<b>自由文検索が要求する照合順序の実在</b>を
/// 確認する。失敗は <see cref="SqlConnectionFailureClassifier"/> で原因を分類して返す
/// （database.md §6.1 の原因別の次の一手）。
/// 接続試行の打ち切りは接続文字列の <c>Connect Timeout</c>（既定 15 秒）に従う——
/// 応答しないサーバで無限待ちにはならない。
/// <para>
/// <b>照合順序を「切替の前に」見る理由</b>（Issue #515）: 照合順序の検査は本来
/// スキーマ初期化のトランザクション内（<c>SqlServerLogStore.Schema</c>）にあり、その初期化は
/// 起動経路の外で行われる（ADR-0023 決定 1）。したがってここで見ないと、利用者は
/// 「接続検証に成功 → 切替を実行 → <b>サービスを再起動（＝受信が止まる）</b> → そこで初めて
/// 保存先が使えないと判明」という順序を踏む。**検証を通したのに、受信を止めてから失敗する**
/// のは最も避けたい形であり、検出点を準備フェーズへ前倒しする。
/// </para>
/// <para>
/// 権限（テーブル作成可否）の事前検証は引き続き対象外とする——database.md §6.1 の準備フェーズの
/// 完全形は後続の課題であり、本変更は「受信を止めてから失敗する」経路だけを潰す最小の追加に
/// 留める。
/// </para>
/// </remarks>
public sealed class SqlServerConnectionValidator : ISqlServerConnectionValidator
{
    /// <inheritdoc/>
    public async Task<SqlServerConnectionValidationResult> ValidateAsync(
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            _ = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

            // 自由文検索の照合順序がこのインスタンスに実在するか（Issue #515）。
            // 値は provider 側の定義を唯一の正とする（二重定義にしない）。
            await using var collationCommand = connection.CreateCommand();
            collationCommand.CommandText =
                "SELECT 1 FROM sys.fn_helpcollations() WHERE name = @collation";
            collationCommand.Parameters.Add(new SqlParameter("@collation", SqlServerLogStore.SearchCollation));
            var collationExists = await collationCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

            if (collationExists is null or DBNull)
            {
                // 分類は Unclassified にする——接続・認証・到達性のいずれでもなく、
                // 既存の分類のどれに寄せても「次の一手」が誤って案内されるため。
                return new SqlServerConnectionValidationResult(
                    false,
                    $"接続はできましたが、この SQL Server には自由文検索に必要な照合順序 " +
                    $"{SqlServerLogStore.SearchCollation} がありません。この保存先へ切り替えると、" +
                    "サービスの再起動後にログを保存できません。別のインスタンスを指定するか、" +
                    "SQL Server 側でこの照合順序が利用できるか確認してください。",
                    PromotionConnectionFailureKind.Unclassified);
            }

            return new SqlServerConnectionValidationResult(true, "接続に成功しました。");
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or ArgumentException or FormatException)
        {
            // SqlException.Message はサーバ名・DB 名を含み得るが、いずれも管理者自身が入力した
            // 値であり秘密情報（パスワード）は SqlClient がメッセージに載せない。原因の要約と
            // して利用者向けにそのまま返す（監査記録には載せない——記録するのは成否と分類のみ。
            // PromotionWizardService 参照）。
            return new SqlServerConnectionValidationResult(
                false,
                $"接続できませんでした: {ex.Message}",
                SqlConnectionFailureClassifier.Classify(ex));
        }
    }
}
