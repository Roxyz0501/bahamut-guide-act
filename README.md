# Bahamut Guide for ACT

絶バハムートのSPESPEタイムラインにある画像通知を、ギミック単位で表示ON/OFF・一括移動・拡大縮小するACTプラグインです。戦闘ログの判定・タイミング・音声は既存のSPESPE / ACT.Hojoringが担当します。

## インストールと初回設定

1. [最新の安定版](https://github.com/Roxyz0501/bahamut-guide-act/releases/latest)の`BahamutGuide-vX.Y.Z.zip`を専用フォルダーへ展開します。
2. ACTのPluginsから`BahamutGuide.dll`を追加します。Windows / .NET Framework 4.8 / ACTが必要です。戦闘表示にはSPESPEが必要です。
3. 「タイムライン」→「元タイムライン」で、編集中のローカルXMLを「元XML」に指定します。
4. 「更新確認／初回読み込み」で差分を確認し、「確認した内容を取り込む」を押します。初回取り込みだけは手動更新オプションがOFFでも可能です。
5. 「SPESPEのXML」には、SPESPEで実際に読み込んでいる既存の絶バハムートXMLを指定します。**元XMLとは別のファイル**を指定してください。新しい有効タイムラインを重複作成しません。
6. 「画像フォルダー」に、使用中のHojoringの`resources/images`を指定します。
7. 「ギミック表示」で調整し、「SPESPEのXMLへ適用」を押した後、SPESPEでタイムラインを再読み込みします。

プラグインはタイムラインや画像を同梱しません。既存の画像を引き続き使います。元XML更新で新しい画像が必要になった場合は、その画像も参照フォルダーに用意してください。

## ギミック別の操作

- P2ドラゴン配置：円・四角・数字・「調整あり」をひとつのグループとして扱います。各レイヤーの相対位置と元の画像倍率を維持して拡大縮小します。
- P2ネールのセリフ：全パターンの位置・倍率・表示をまとめて変更します。プレビューのパターン一覧から個々のカンペを確認できます。
- その他：XMLのフェーズごとに画像通知をまとめます。画像のないフェーズは一覧に出ません。フェーズ名は元XMLの表記を使用します。
- プレビューの金枠をドラッグすると、同じギミックの画像全体の座標が変わります。X/YはWPFのDIP座標です。複数モニターで座標系が異なる場合は実際のSPESPE表示も確認してください。
- 「固定・クリック透過」でプレビューを固定できます。ドラゴンは通常配置と「北から2体目が南」の調整あり配置を確認できます。いずれも配置例であり、戦闘の実データではありません。
- グループ初期化、すべてON/OFF、直前のXMLへ戻す操作を用意しています。元XMLですでに無効な画像は勝手に有効化しません。

プレビューは設定変更を即時に表示します。**戦闘中の表示設定への反映にはXMLへの適用とSPESPEの再読み込みが必要です。** 適用は戦闘外で行ってください。文字通知・音声・戦闘タイムラインの行そのもののON/OFFは対象外です。画像通知の`left`、`top`、`scale`、`enabled`だけを変更し、判定・遅延・消去条件・スクリプトを維持します。

## 元XMLの更新：手動・任意

「元XMLの手動更新を許可する」は**既定OFF**です。定期監視、自動取り込み、起動時取り込みはありません。プラグイン起動時には最後に受け入れたスナップショットだけを読みます。

元XMLを別のエディターで更新したら、オプションをONにし、更新確認→差分確認→取り込み→必要なときにSPESPE側へ適用、の順で操作します。取り込みは元XML自体を書き換えません。確認後に元XMLが変わった場合は再確認が必要です。

同じグループIDの位置・倍率・ON/OFFを維持します。新規グループは元XMLの配置を使い、削除されたグループの設定は後から復活できるよう保持します。フェーズ名が変更された場合は新規グループとして扱うため、配置を確認してください。差分表示は先頭200行までです。大きな変更は元ファイルでも確認してください。

## 保存・復元と安全性

設定はメモリに即時反映し、60秒ごとの固定周期で変更時だけ保存します。変更がなければ一時ファイルも作成しません。ACTの正常終了、プラグイン無効化時には最新の設定を保存してから終了します。強制終了や電源断では最後の保存以後の設定変更が失われる場合があります。

取り込み済みXMLはプラグイン配置先の`GuideData/baseline.xml`に保存します。元XML更新のバックアップは`GuideData/Backups/Source`、SPESPE側への適用前バックアップは`GuideData/Backups/Timeline`に保存します。バックアップをディスクへ書き込み、内容を確認してから対象を書き換えます。失敗時は元データへ戻し、変更済み状態を保持します。バックアップは自動削除しません。

「直前のXMLへ戻す」で直前の適用を取り消せます。復元後もSPESPEで再読み込みしてください。元スナップショット自体を戻す場合はSourceバックアップを作業用XMLにコピーし、手動更新として取り込んでください。

対象XMLが外部で変わった場合は上書きを中止します。外部変更を取り込む必要がある場合は、変更内容を確認して元XMLへ反映してください。その後、適用先を「参照」から選び直すと、次回適用では現在のファイルを新しいバックアップとして保護します。単に選び直して適用すると、外部の編集内容は取り込み済みスナップショットで置き換わるので、差分を先に確認してください。

DTD・外部エンティティ、32MiBを超えるXML、5,000件超の画像通知を拒否します。元XMLのRazorやスクリプトは本プラグインでは実行しません。SPESPEは従来どおり実行するため、信頼するローカルXMLを使用してください。画像の自動ダウンロードはありません。

プレビューは静止画で、継続的な描画タイマーはありません。画像キャッシュは最大16件・64MiB、画像は1件20MiB・16メガピクセル、合成画像は16メガピクセルまでです。画像フォルダー選択やソースの再取り込みでキャッシュを無効化します。画像ファイル更新後は画像フォルダーを選び直してください。非表示・固定切替でウィンドウを再生成しません。

## 言語・支援

英語・日本語・簡体字中国語・韓国語に対応します。初回のみOS言語から選択し、その後は保存した選択を使用します。翻訳キー不足時は英語へフォールバックします。元XMLのフェーズ名やカンペ画像の内容は翻訳しません。

支援は完全に任意です。支援しなくても全機能を利用でき、機能差はありません。[Roxyz0501の開発をKo-fiで支援](https://ko-fi.com/roxyz0501)。支援タブのボタンを押したときだけブラウザを開きます。

## プラグイン本体の更新

元XMLの更新とは独立した「更新」タブがあります。[専用GitHub Releases](https://github.com/Roxyz0501/bahamut-guide-act/releases)を使用し、起動時の確認は既定ONで変更できます。draftとprereleaseを除外し、SemVerで比較します。更新がある場合は画面に表示し、「後で」で保留できます。ネットワークエラーでも通常機能を継続します。

明示的に「更新をダウンロードして準備」を選んだ場合だけダウンロードします。HTTPSの許可されたGitHub配布経路、所有者・リポジトリ・タグ・asset名、SHA-256、ZIP内のファイル名、DLLとUpdaterのアセンブリ名・会社名・バージョンを検証します。トークンは不要です。検証後、ACTを終了してDLLを手動で置き換えてください。終了後も動くアップデータは起動しません。

Releaseには`BahamutGuide-vX.Y.Z.zip`と`BahamutGuide-vX.Y.Z.sha256`を公開します。ZIP直下は`BahamutGuide.dll`、`BahamutGuide.Updater.exe`、`README.md`だけです。マニフェストは`SHA256  ZIPファイル名`の1行です。

## 開発・検証

```powershell
./build.ps1 -ActPath 'path/to/Advanced Combat Tracker.exe' -UiTests
./tests/BahamutGuide.Tests/bin/Release/net48/BahamutGuide.Tests.exe --timeline 'path/to/source.xml' 'path/to/resources/images'
./tests/BahamutGuide.Tests/bin/Release/net48/BahamutGuide.Tests.exe --release
```

`--ui`は前面ウィンドウを開かず4言語の設定画面をPNGへ描画します。`--timeline`は対象のコピーだけを使い、元のXMLを変更しません。現在編集中の86画像通知のXMLを対象とする任意検証です。`--release`は実際の公開ZIP・SHA-256を取得して検証します。

本体とUpdaterのバージョンを揃え、`main`と`vX.Y.Z`タグをpushすると、GitHub Actionsがビルド・テスト・ZIP内容を検証してから安定版を公開します。外部ActionsはコミットSHA固定、ビルド権限はread、Releaseジョブだけcontents:writeです。ACT本体・ユーザーのXML・画像・Config・ログは公開しません。本プラグインのライセンスは未選定です。

### 検証結果と負荷

実XMLの86通知を、ドラゴン62・セリフ18・元XMLのP4画像2・P9画像4に分類しました。検証用コピーで、表示属性以外のXMLの維持、相対座標と倍率、OFF、復元、元ファイル未変更を確認しています。実際の戦闘中のSPESPE描画は未検証です。

ソース読み込み・取り込み・適用は明示操作だけです。画面切替によるDB/API通信・業務データ書き込みは0回。未変更の設定チェック・終了保存は0回の書き込みです（自動テストで確認）。本体更新チェックは1分間メモリを再利用し、期限後はETagで確認します。模擬通信で初回1回、1分以内の再確認0回、期限後1回・304本文0バイトを確認しています。起動後の定期ポーリングはありません。ネットワークキャッシュは1応答・最大1MiB・1分、有効な応答のみ保存し、失敗時の無制限再試行はしません。

## English summary

An ACT companion for existing SPESPE Bahamut timelines. Group image notices by mechanic; move and scale all layers together; enable or disable each group; inspect static previews. Explicitly apply the XML and reload SPESPE for battle changes. Local source updates are opt-in and require review and acceptance, while group settings survive updates. No timeline scripts or combat-log parsing are executed by this plugin. Timelines and images are not distributed.
