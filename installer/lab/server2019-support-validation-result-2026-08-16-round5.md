# Yagura lab 第 5 回検証 報告（PR #519 の追加検証）

## 0. 実施日・実施者・総括

- **実施日**: 2026-08-16
- **実施者**: Claude Code（主: `WIN-DKF5O8U26MJ` / 従: `WIN-IVQ782H2OA4`）

| 項目 | 対象 Issue | 判定 |
|---|---|---|
| A ビルドと開始前確認 | — | **完了** |
| B 秘密鍵 ACL の誤警告 | [#511](https://github.com/Yanai-Taketo/Yagura/issues/511) | **合格**（逆側の回帰確認も実施） |
| C-3-a 再起動待ちカード | [#512](https://github.com/Yanai-Taketo/Yagura/issues/512) | **不合格（未修正）**——対照実験で確定 |
| C-3-b 昇格直後の retention | [#510](https://github.com/Yanai-Taketo/Yagura/issues/510) | **合格** |
| D 拒否ダイアログの目視 | [#496](https://github.com/Yanai-Taketo/Yagura/issues/496) 申し送り | **未実施**（対話デスクトップを用意できず。項目 8 参照） |

**新規の発見が 2 件**あります（項目 7）。うち 1 件は #510 と同型の「昇格直後の初回起動でスキーマ未作成のまま照会する」経路が**まだ 2 か所残っている**というものです。

---

## 1. 重要な前提の相違 —— main ではなく PR #519 ブランチで検証した

依頼書 §2-1 は「**main の最新**を取得」と指示している。しかし実施時点で:

```
origin/main                                    = 220f23a
main に ShouldWarnGrantFailed                  → 無し
main に本依頼書 (…visual-request.md)           → 無し
→ PR #519 は未マージ
```

依頼書 §0 の注記は「本書が main で読めるなら修正も入っている」という前提を置いているが、
**本書自体が PR #519 に含まれるため、未マージの現時点では main から読めない**。
依頼書どおり main からビルドすると、§2-2 の分岐により**項目 B が「未マージのため実施不可」**となり、
本回の主目的が失われる。

ご依頼が「**PR #519 の**追加検証」であることから、**PR ブランチからビルドして事前検証**した。

- **ビルド元 SHA**: `b5a4d84132ad2963c396311324b66c2e13ffdf83`
  （`docs(lab): 第 5 回検証の依頼書をリポジトリへ置く (#510 #511 #512)`）
- 収録アセンブリの `ProductVersion` は `0.9.3+b5a4d84…` で、ビルド元を追跡できる

> **依頼書への提案**: §0 の「本書が読めれば修正も入っている」という判定法は、
> 本書と修正が同じ PR にある限り**マージ後にしか成立しない**。
> 「main か、対象 PR のブランチか」を明示する欄を設けるほうが安全。

## 2. 項目 A: ビルドと開始前確認

### 開始前確認（依頼書 §2-2）

`CertificatePrivateKeyAccessGranter.cs` に **`ShouldWarnGrantFailed` あり**（PR ブランチ）。

```
270: public bool ShouldRecordGrantAudit => Succeeded && !_wasAlreadyGranted;
279: public bool ShouldWarnGrantFailed  => !Succeeded;
```

呼び出し側も 3 か所すべてが独立した 2 つの `if` に分離済み（第 4 回に報告した
`if/else` の流用が原因だったので、修正の形として正しい）:

```
Program.cs 1411/1422, 1464/1475, 1518/1529
```

### ビルドと導入

`Directory.Build.props` の `<YaguraVersion>` を **0.9.3** に書き換え（`-p:` は不使用）。
`installer/bin`・`obj`・`publish`・`src/**/bin,obj` を削除してからビルド。

| | 値 |
|---|---|
| MSI ProductVersion | 0.9.3 |
| 収録 `Yagura.Host.exe` FileVersion | **0.9.3.0** |
| MSI SHA256 | `2590DE7082FA63AE96DCF6719ABB43D663EF9F3F0E499BB632DA8268741758DD` |

> **手順どおりに進まなかった点**: 依頼書 §2-4 は「既存の 0.9.2 へアップグレードで導入」と
> 書いているが、**第 4 回の A-3 でアンインストール済み**のため 0.9.2 は入っていなかった。
> 先に 0.9.2 を新規導入してからアップグレードした。

アップグレード結果: `0.9.2.0` → **`0.9.3.0`**、サービス Running、ARP は 0.9.3 単一。

## 3. 項目 B: 秘密鍵 ACL の誤警告（#511）—— 合格

前提の確認: TLS 受信 (6514)・管理リモート HTTPS (8516)・閲覧 HTTPS (8514) の 3 つとも有効で、
秘密鍵に `NT SERVICE\Yagura  Read, Synchronize  Allow` が**既に付いている**状態
（＝第 4 回に誤警告が出ていた条件）。

### 主判定: 誤警告が消えたこと

サービス再起動後のイベントログ:

```
private-key-grant-failed: 0 件
```

第 4 回は毎起動 3 件（ingestion-tls / admin-https / viewer-https、いずれも `理由: (null)`）出ていた。
**合格。** 起動時に残る警告は `[firewall-rule-mismatch]`（Issue #518）のみ。

### 逆側の回帰確認（依頼書 §3「併せて 1 点」）—— 実施した

証明書の秘密鍵から `NT SERVICE\Yagura` の ACE を外して再起動:

| 確認点 | 結果 |
|---|---|
| 警告が出ること | **出た（3 件）** |
| 理由が `(null)` でなく実際の理由 | **実理由が入った**（下記） |
| 案内の `icacls` がそのまま通るか | **この経路では icacls が案内されない**（後述） |

実際の理由:

> （理由: 秘密鍵を開けなかったため、権限付与先の鍵ファイルを特定できませんでした: キー セットがありません。 現在の実行アカウントに秘密鍵への権限がない場合に起きます（付与しようとしている権限を付与処理自体が必要とするため、自動では解決できません）。証明書スナップイン（certlm.msc）の「秘密キーの管理」から手動で権限を付与してください（configuration.md §6 CF-D2）。）

**警告が「本物の失敗」を潰していないことの裏づけ**として、権限が無い間は 3 つとも
listen はするが TLS ハンドシェイクが全滅することも確認した:

```
port 6514 -> FAILED: Authentication failed because the remote party has closed the transport stream.
port 8516 -> FAILED: 同上
port 8514 -> FAILED: 同上
```

ACE を戻して再起動すると、警告 0 件・3 ポートとも `Tls12` で OK に復帰した。

#### 副次的な指摘: 最も起きやすい失敗経路に `icacls` の案内が出ない

`icacls` を含む案内は `UnauthorizedAccessException / IOException` の分岐（`…Granter.cs:150-160`）にしかない。
一方、**サービスアカウントに権限が無いという典型状況**では、その手前の
`ResolveCngKeyFilePath` が `CryptographicException`（キー セットがありません）で落ち、
`certlm.msc` だけを案内する分岐（同 76-85）に入る。

つまり #503 で追加した「そのまま貼れる `icacls`」は、**一番出会いやすい場面では出ない**。
この分岐でも鍵ファイルパスは推定できる（`RSACng.Key.UniqueName` が取れない状況のため厳密には難しいが、
証明書の拇印から案内文を作ることは可能）ので、検討の価値があると思われる。

なお第 4 回で確認したとおり、**現行の引用符の形自体は PowerShell で正しく通る**
（今回も ACE 復旧に `icacls "<key>" /grant "NT SERVICE\Yagura:R"` を使い exit 0）。

## 4. 項目 C: 昇格の 1 サイクル

### C-0 / C-1 / C-2（準備）

- バックアップ: `C:\build\backup-programdata-yagura-20260816-174516`（`C:\ProgramData\Yagura` 全体）
- 既存 DB（`Yagura` / `YaguraLab2`）は**残置**
- SQLite へ戻して起動 → UDP 514 へ **25 件**投入 → 保存済みログ件数 3,405 → **3,430**、最終受信も更新（取り込み確認）
- 保持期間を **1 日**に設定（`Retention.Days = 1`）

### C-3 昇格の実行

接続先 `localhost\SQLEXPRESS` / DB `YaguraLab3` / Windows 統合認証 / サーバ証明書を信頼する。

> **手順どおりに進まなかった点（依頼書の想定漏れ）**: 依頼書 §4 C-3 は「新しい DB 名（例: `YaguraLab3`）」と
> 指示しているが、**製品は DB を自動作成しない**。最初の接続検証は次のように正しく失敗した:
>
> > 指定したデータベースを開けませんでした。データベースが未作成の場合は、下の SQL で作成できます。
> > 接続できませんでした: このログインで要求されたデータベース "YaguraLab3" を開けません。ログインに失敗しました。 ユーザー 'NT SERVICE\Yagura' はログインできませんでした。
>
> 画面が提示する SQL（`CREATE DATABASE` → `CREATE USER` → `db_owner` 追加）をそのまま実行したところ、
> 再検証で「SQL Server への接続を確認しました。」となった。**この画面の作り自体は良い**——
> 何をすればよいかが完結しており、SQL をコピーできる。依頼書側に「DB は事前に作る」を足すべき。

また「切り替えを実行する」を押せるようになるまでに、**旧ログの扱い（退避／削除）の選択**と、
退避を選んだ場合は**退避先フォルダの入力 + 確定**が必要だった（依頼書には記載がない）。
`C:\build\yagura-sqlite-evacuated` を指定して「退避」で確定し、確認ダイアログ「切り替える」で実行した。

切替直後の状態（**サービスは未再起動**、プロセス開始 17:46:54 / 切替 17:51:05）:

```
yagura.json: Storage.Provider = sqlserver / ConnectionString = 設定済み(dpapi)
```

### C-3-a 観測点その 1（#512）—— **不合格。カードに載らない**

切替直後・再起動前に管理ホーム `/admin` を開いた結果:

```
本文・HTML とも「再起動待ち」の文字列が 1 件も無い（カードそのものが出ない）
```

**#512 の修正コード自体は入っている**（`PromotionWizardScreen.razor:444-459` が
保存成立時に `ReloadService.ReloadAsync` を呼ぶ）。それでもカードに載らない。

#### 原因切り分けのための対照実験（依頼範囲外・実施した）

カード機構自体が壊れているのか、保存先キーだけが載らないのかを分けるため、
**別の `RestartRequired` キーを手編集して手動再読み込み**した。

1. `yagura.json` の `Ingestion:Tls:Port` を `6515` に手編集
2. `/admin/reload` から「再読み込みを実行」

```
再読み込みを実行しました（即時反映: 0 件）。
次の項目は反映にサービス再起動が必要なため、未反映のまま残っています:
Ingestion:Tls:Port
```

3. `/admin` を開くと **カードが出た**。ただし載っているキーは:

```
再起動待ちの設定変更
  Ingestion:Tls:Port   検出した再読み込み: 2026-08-16 17:54:17 (UTC+09:00)
```

**`Storage:Provider` も `Storage:SqlServer:ConnectionString` も載っていない。**

#### 結論と推定原因

- カード機構・再読み込み・キー定義はいずれも正常
  （`ConfigurationKeyMetadata.cs:89-90` で保存先 2 キーは `RestartRequired` として登録済み）
- したがって問題は「昇格の保存経路を通ると、保存先キーが**変更として検出されない**」こと
- `ConfigurationReloadService.ReloadAsync` は**メモリ上の `_lastAppliedOptions`** と比較して
  `ChangedKeys` を作る（同 169）。昇格の保存がこの基準を**先に**新しい値へ進めてしまうと、
  直後に呼ばれる `ReloadAsync` の差分は空になり、`_pendingRestartKeys` に何も積まれない（同 186-192）
- 傍証: `yagura.json` と `last-applied-configuration.json` が**同一時刻 17:51:05・同一サイズ 1821**で
  揃っており、切替の保存時点で基準側も書き換わっている

**修正は「ReloadAsync を呼ぶ」だけでは足りず、保存の前に基準を進めない（または保存経路が
直接 pending キーへ計上する）必要がある**と思われる。

### C-3-b 観測点その 2（#510）—— **合格**

再起動（17:55:01 停止 → 17:55:32 起動）後、**3 分間**のイベントログ:

```
retention / catchup を含むイベント: 0 件
（[retention-catchup-query-failed] は出ない）
総イベント数は 6 件で以後増えない
```

保持期間削除が**実際に走った**ことも `/status`「通知・動作の記録」で確認:

```
種別                      開始                                 付帯情報
古いログの自動削除を実行   2026-08-15 17:55:32 (UTC+09:00)      0
```

（新 DB のため削除対象 0 件。スキーマは 6 テーブルが正しく作成された:
`AdminAccounts` / `AdminAccountsSchemaVersion` / `LogRecords` / `SchemaMigrationHistory` / `SchemaVersion` / `SystemEvents`）

### C-4 後始末

依頼書どおり **SQL Server（`YaguraLab3`）のまま**にしてある。`YaguraLab2` は残置。
バックアップも残置。退避先フォルダ `C:\build\yagura-sqlite-evacuated` を作成した（中身は空——
画面の説明どおり、現行版は選択を記録するだけでファイル移動はしない）。

## 5. 項目 D: 拒否ダイアログの目視 —— **未実施**

**実施できなかった。**内容と、次回すぐ実施できる状態まで整えた旨を記す。

- ゲスト A へ `Yagura-0.9.3-x64.msi` を転送済み。**転送後 SHA256 照合済み**
  （`2590DE70…58DD`、ローカルと一致）。配置先 `C:\a1\Yagura-0.9.3-x64.msi`
- ゲスト A には `console` セッション（ID 2 / Administrator / Active）が存在するため、
  対話セッションで GUI を出せないかを試みた。`/IT` 付きスケジュールタスクで
  シェル関連付け起動（＝ダブルクリックと同じ経路）＋ウィンドウ文字列取得＋画面キャプチャを
  行うスクリプトを作成・配置し実行したが、**タスクが「実行中」のまま進まず**、
  `msiexec` のプロセスも生成されなかった。この console セッションは無人（実際の対話デスクトップが
  接続されていない）ため、GUI を伴うタスクが起動できないと判断した
- **RDP で人が接続すれば実施できる。**そのための資材も配置済み:
  - `C:\a1\Capture-RejectDialog.ps1`（ウィンドウ文字列 + 画面キャプチャを自動取得）
  - `C:\a1\run-capture.cmd`

RDP 接続後、`C:\a1\Yagura-0.9.3-x64.msi` をダブルクリックしてスクリーンショットを撮るだけで
本項目は完了する（あるいは上記スクリプトを実行すれば `reject-dialog.png` と
`reject-dialog-text.txt` が自動生成される）。

なお第 4 回で `/qb`（UI シーケンス）から取得済みの文面は以下で、**ダイアログに出るのと同一の文字列**である
（`LaunchCondition.Description` の解決結果）。**残っているのは「実際のダイアログ幅で全文が読めるか」の目視のみ**:

> この OS には Yagura をインストールできません(OS ビルド 14393)。対応は OS ビルド 17763 以上(Windows Server 2019 以降 / Windows 11 / Windows 10 LTSC・Enterprise)、推奨は Windows Server 2022 以降です。詳細と移行の相談先は README の「システム要件」を参照してください。

ゲスト A に Yagura がインストールされていないことは確認済み（`Program Files\Yagura`・サービス・ARP すべて無し）。

## 6. 手順どおりに進まなかった箇所（まとめ）

| # | 区分 | 内容 |
|---|---|---|
| 1 | **依頼書の前提誤り** | §2-1「main の最新」では #519 が未マージのため項目 B が実施不可になる。PR ブランチで実施した（項目 1） |
| 2 | **依頼書の想定漏れ** | §2-4「既存の 0.9.2 へアップグレード」——第 4 回 A-3 でアンインストール済みだった。0.9.2 を先に導入した |
| 3 | **依頼書の想定漏れ** | §4 C-3——**DB は自動作成されない**。画面が提示する SQL を実行する手順が要る |
| 4 | **依頼書の記載漏れ** | §4 C-3——「切り替えを実行する」の前に旧ログの扱いの選択と退避先の入力・確定が必要 |
| 5 | **環境要因** | 項目 D——無人 console セッションでは GUI タスクを起動できず未実施。RDP が要る |
| 6 | **lab 手法の逸脱** | ブラウザペインが描画されず座標クリックが機能しなかったため、UI 操作を実 DOM の `click()` で駆動した。Blazor のイベント経路は利用者クリックと同一だが、**マウス操作そのものは検証していない** |

## 7. 依頼範囲外の観測（新規）

### 7-1. 昇格直後の初回起動で、スキーマ未作成のまま照会する経路が 2 か所残っている

#510 と**同型**の問題。`[retention-catchup-query-failed]` は解消したが、
同じ「スキーマ初期化の前に保存先へ問い合わせる」経路が別に 2 件あり、
昇格後の初回起動で毎回スタックトレース付きの警告が出る。

```
[downtime-record-failed] 受信断区間の記録に失敗しました: downtime.normal-stop …（保存先の初期化後に再試行します）
  → SqlException 208: オブジェクト名 'dbo.SystemEvents' が無効です。
     IngestionHostedService.StartAsync (IngestionHostedService.cs:166)

送信元の途絶検知の起動時 seed（最終受信時刻の照会）に失敗したため、全エントリを起動時刻仮基準で追跡します
  → SqlException 208: オブジェクト名 'dbo.LogRecords' が無効です。
     ActiveNotificationMonitor.SeedSourceSilenceBaselineAsync (ActiveNotificationMonitor.cs:236)
```

**どちらも自己回復する**——受信断記録は再試行に成功しており、`/status` に
「停止・再起動による 17:55:31〜17:55:32」として残った。途絶検知もフォールバック動作が明示されている。
したがって実害は小さいが、**昇格という「利用者が最も不安な瞬間」に、初回起動だけ
スタックトレース 2 本が出る**のは #510 を潰した意図（＝この不安を消す）と整合しない。
#510 の修正が retention の呼び出し元だけを対象にしたためと思われる。

### 7-2. `[firewall-rule-mismatch]`（TCP 6514）は第 4 回から継続

Issue #518 として切り出し済みの件。TLS 受信を有効にしても受信許可規則が作られないため、
起動のたびに出続けている。本回でも全再起動で再現した。

## 8. 報告のしかた（依頼書 §6 への対応状況）

- 本報告書を `installer/lab/server2019-support-validation-result-2026-08-16-round5.md` として
  ブランチへ push する
- **Issue #510 / #512 へのコメントは未実施**——この環境から GitHub API / `gh` を使えないため
  （`gh` 未インストール、トークンの探索は環境の制限によりブロック）。
  ブランチ名をコメントする作業は依頼側でお願いしたい

## 付録: 成果物の所在

| ファイル | パス |
|---|---|
| MSI 0.9.3 | `C:\build\msi\Yagura-0.9.3-x64.msi` |
| ビルドログ | `C:\build\build-0.9.3.log` |
| アップグレードログ | `C:\build\upgrade-093.log` |
| ProgramData バックアップ（C-0） | `C:\build\backup-programdata-yagura-20260816-174516` |
| SQLite 退避先（空） | `C:\build\yagura-sqlite-evacuated` |
| ゲスト A の資材 | `10.0.0.142` の `C:\a1\`（MSI・キャプチャスクリプト・第 4 回の reject ログ） |
