# FilterCopy

Windows用の「フィルタして貼り付け」ユーティリティ。対象を含める条件と除外する条件で絞り込み、フォルダ構造を保ってWindows Shellで一括コピーします。通常検索・ワイルドカード・正規表現、検索履歴、ライト／ダーク表示に対応しています。

**利用する方へ：[ユーザーガイド](docs/USER_GUIDE.md)**

ビルド済みの配布物は、このリポジトリのReleasesから取得できます。

このREADMEには開発・検証に関する情報をまとめています。導入、右クリック登録、検索条件の書き方、コピー操作、更新・解除方法はユーザーガイドを参照してください。

## 開発環境

- Windows、.NET 10 SDK
- Visual Studioを使う場合は「.NETデスクトップ開発」ワークロード
- C# / WPF / XAML。外部NuGetパッケージへの依存はありません。

## ビルドと起動

ビルド前に、通知領域から常駐中のFilterCopyを終了してください。

```powershell
dotnet build FilterCopy.csproj -c Release
dotnet run --project FilterCopy.csproj -c Release --no-build -- --destination "D:\Backup"
```

実行ファイルは`bin\Release\net10.0-windows\FilterCopy.exe`です。起動時にはエクスプローラーでファイルをCtrl+Cしておく必要があります。

開発用ビルドをエクスプローラーへ登録する場合は、リポジトリの`powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Install.ps1`を使います。既存のReleaseビルドを登録するだけで、ビルドやダウンロードは行いません。`-ExePath`で別の実行ファイル、`-NoStartup`で自動起動なし、`-WhatIf`で変更なしの確認を指定できます。解除には`scripts\Uninstall.ps1`を使います。

## 構成

| ファイル | 役割 |
| --- | --- |
| `Core.cs` | ファイル列挙、包含・除外フィルタ、コピー計画、履歴保存 |
| `MainWindow.xaml` / `.xaml.cs` | 条件入力、非同期検索、ツリー・件数表示 |
| `ShellCopy.cs` | 専用STAスレッドからの`IFileOperation`呼び出し |
| `HotkeyHost.cs` / `TrayTheme.cs` | 常駐、ホットキー、エクスプローラー連携 |
| `AppVersion.cs` / `Notice.xaml` | バージョン・MITライセンス・詳細表示 |
| `Tests/` | 外部テストフレームワークを使わない検証プログラム |
| `scripts/` | 登録・解除、アイコン生成、配布準備、公開ファイル検査 |

コピー処理は全ファイルを1つの`IFileOperation`へ登録し、`PerformOperations`を一度だけ呼びます。コピー先の相対パスと必要な親フォルダはアプリ側で準備します。走査・検索はUIスレッド外で実行し、古い検索はキャンセルします。履歴は包含・除外条件と共通の検索オプションを1組として保存し、除外条件のない旧形式も読み込めます。

## 検証

```powershell
dotnet run --project Tests\FilterCopy.Tests.csproj -c Release
dotnet run --project Tests\FilterCopy.Tests.csproj -c Release --no-build -- --preview
```

前者は一時フォルダで検索・除外・履歴互換・衝突防止・キャンセル・実際のShellコピーを検証します。後者はWindowsの設定やクリップボードを変更せずに両テーマの画面を`artifacts\previews`へ出力します。上書き確認、進捗UI、右クリック、ホットキー、複数タブは実際のデスクトップでも確認してください。

## バージョンと配布

メジャーは1固定で、アプリのビルド成功ごとに`version.txt`のマイナー番号を1増やします。失敗・設計時ビルド・`--no-build`では増えません。同じチェックアウトのビルドは並列実行しないでください。

配布ZIPの作成・検査・GitHubリリースの手順は[RELEASE.md](RELEASE.md)にまとめています。ZIP内の`README.md`には[ユーザーガイド](docs/USER_GUIDE.md)を同梱し、この開発用READMEは含めません。ReleaseビルドではPDBを生成せず、ソースパスを固定パスへ置換します。履歴・ローカル設定・生成物はGitの追跡対象外です。

## アイコンとライセンス

アイコンの元データは`Assets\FilterCopy.svg`です。次のコマンドで複数解像度ICOとPNGを再生成できます。

```powershell
powershell -NoProfile -STA -ExecutionPolicy Bypass -File .\scripts\Build-Icon.ps1
```

FilterCopyは[MITライセンス](LICENSE)です。自己完結型ZIPには.NETのライセンス・第三者の権利表記も同梱します。
