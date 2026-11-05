# FilterCopy

Windows用の「フィルタして貼り付け」ユーティリティ。C# / .NET 10 / WPF製。

MITライセンスで公開します。全文は[LICENSE](LICENSE)とアプリの「バージョン情報」に記載しています。同梱.NETのライセンス・第三者の権利表記は配布ZIPの`licenses`フォルダにあります。

画面・詳細ダイアログ・通知領域メニューはWindowsのアプリのライト／ダーク設定に追従します。高コントラスト設定にも対応します。アイコンは「フォルダ＋フィルタ」で、実行ファイル・ウィンドウ・通知領域・右クリックメニューに共通で使用します。

アイコンの元データは`Assets\FilterCopy.svg`。`powershell -NoProfile -STA -ExecutionPolicy Bypass -File .\scripts\Build-Icon.ps1`で16〜256pxの複数解像度ICOとプレビューPNGを再生成できます。

## 使い方

1. エクスプローラーでコピー元のファイル・フォルダを選び、**Ctrl+C**。
2. コピー先へ移動し、右クリック → **フィルタして貼り付け**。Windows 11では「その他のオプション」内に表示されます。
3. 検索条件を入力。通常検索／ワイルドカード／正規表現、大文字小文字の区別、ファイル名／相対パスを選べます。
4. ツリーと `一致件数 / 全ファイル数` を確認して **コピー**。

常駐時はエクスプローラー上の **Ctrl+Alt+V** でも開けます。通知領域アイコンから起動・終了できます。エクスプローラーのタブなどによりコピー先を一意に取得できない場合はフォルダ選択画面で指定します。通常起動でもコピー先を選択できます。

「通常」は部分一致、「ワイルドカード」は名前全体への一致です。ワイルドカードの`*`は0文字以上の任意の文字列、`?`は任意の1文字を表します。例: `*.dll`、`image-??.png`。相対パス検索では区切り文字にも`*`が一致します。正規表現では従来通り`\.dll$`などが使えます。検索モードも履歴に保存します。ツリーは初期表示時と絞り込み更新時にすべて展開し、手動で折りたたむこともできます。

## ビルド・起動

Windowsと.NET 10 SDK（Visual Studioの場合は「.NETデスクトップ開発」）が必要です。外部NuGetパッケージは使っていません。

```powershell
dotnet build FilterCopy.csproj -c Release
dotnet run --project FilterCopy.csproj -- --destination "D:\Backup"
```

ビルド後の実行ファイル: `bin\Release\net10.0-windows\FilterCopy.exe`。

## バージョンと配布

メジャーは1固定で、アプリのビルド成功ごとに`version.txt`のマイナー番号を1増やします。画面上部と通知領域メニューの「バージョン情報」、ウィンドウタイトル、実行ファイルのプロパティで確認できます。失敗したビルドと`--no-build`では増えません。同じチェックアウトでのビルドは並列実行しないでください。

配布用ZIPは次のコマンドで作成できます。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Prepare-Release.ps1
```

既存の.NET 10 Desktop Runtimeを使う小さな配布物には`-FrameworkDependent`を指定します。詳細は[RELEASE.md](RELEASE.md)を参照してください。配布スクリプトは新しい発行先から必要な実行ファイルのみを選び、登録・解除スクリプトと利用手順を同梱します。ReleaseビルドはPDBを生成せず、コンパイラのソースパスも固定パスへ置換します。履歴やローカル設定、ビルド成果物はGitの追跡対象外です。

## エクスプローラーへ登録

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Install.ps1
```

スクリプトは既存の`bin\Release\net10.0-windows\FilterCopy.exe`を、現在のユーザーの右クリックメニューとスタートアップへ登録して常駐を開始します。ビルドや発行は行いません。実行ファイルがない場合は、ビルド方法を表示して終了します。管理者権限は不要です。実行には.NET 10 Desktop Runtimeが必要です。登録後は実行ファイルと隣接するDLLなどを移動しないでください。常駐不要なら`-NoStartup`を指定します。再登録前は通知領域から常駐アプリを終了してください。

別のビルドや発行済みファイルを使う場合は、パスを指定します。

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Install.ps1 -ExePath .\artifacts\app\FilterCopy.exe
```

`-WhatIf`で登録内容を変更せずに対象を確認できます。以前の`-FrameworkDependent`引数は互換性のため受け付けますが、処理には影響しません。

解除:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Uninstall.ps1
```

解除時は実行ファイル・検索履歴を残します。履歴は`%LOCALAPPDATA%\FilterCopy\history.json`に最大30件保存します。コピー時または画面を閉じる際に、最後に検索に成功した条件を保存します。

## コピーの仕様

- 選んだフォルダ自体からの相対階層を保持。単独ファイルはコピー先直下へ配置します。
- 一致したファイルと親フォルダだけを表示・コピーします。空フォルダはコピーしません。
- `IFileOperation`を専用STAスレッドで使用。全ファイルを`CopyItem`で予約し、`PerformOperations`を一度だけ呼びます。上書き確認・進捗・中断はWindows ShellのUIを使用します。
- 必要なコピー先フォルダは予約前に作ります。中断・失敗時には作成済みフォルダやコピー済みファイルが残る場合があります。コピー元は変更しません。
- 選択の重複は除外。同一コピー先になる複数のコピー元、同一ファイルへのコピー、コピー元フォルダ配下へのコピーは拒否します。
- 通常のファイルシステムが対象です。ZIP内や仮想フォルダ内の項目は対象外です。コピー元のリンク／ジャンクションは走査せず、読み取り不可の項目とともに「走査の詳細」に表示します。コピー先のフォルダ経路にリンク／ジャンクションがある場合も拒否します。
- 走査と検索はバックグラウンド処理。正規表現は1回の照合に200msの制限を設けています。走査中はコピーできません。
- Windows 11の新しい右クリックメニューへの直接統合は含みません。従来メニューで利用できます。

## 検証

```powershell
dotnet run --project Tests\FilterCopy.Tests.csproj -c Release
```

一時フォルダを使い、検索・階層・衝突・キャンセルと実際のShellコピー（100ファイルを含む）を検証します。上書き確認、進捗画面の見た目、右クリック、ホットキー、複数タブでの動作は実際のデスクトップで確認してください。

`dotnet run --project Tests\FilterCopy.Tests.csproj -c Release -- --preview`でライト／ダーク両方の画面を`artifacts\previews`へ出力できます。Windowsの設定とクリップボードは変更しません。
