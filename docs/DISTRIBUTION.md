# FilterCopy

Windows x64用の、フィルタして貼り付けるユーティリティです。

FilterCopyはMITライセンスです。全文は同梱の`LICENSE`およびアプリの「バージョン情報」で確認できます。Copyright (c) 2026 FilterCopy contributors。

## 起動・登録

1. ZIPを保存しておきたい場所に展開します。
2. `FilterCopy.exe`を起動できます。
3. 右クリック・自動起動を登録するには、展開したフォルダで実行します。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Install.ps1
```

登録スクリプトは同じフォルダの実行ファイルを使います。ビルドやダウンロードは行いません。管理者権限は不要です。登録後はフォルダを移動しないでください。常駐が不要な場合は`-NoStartup`を付けます。

`self-contained`版は.NETランタイムを同梱しています。`framework-dependent`版には.NET 10 Desktop Runtimeが必要です。未署名のため、Windowsの保護機能によって確認画面が出る場合があります。

自己完結型に同梱する.NETのライセンスと第三者の権利表記は`licenses`フォルダにあります。

## 使い方

1. エクスプローラーでコピー元のファイルやフォルダを選び、Ctrl+C。
2. コピー先で右クリック →「その他のオプション」→「フィルタして貼り付け」。常駐時はCtrl+Alt+Vも使えます。
3. 通常／ワイルドカード／正規表現を選び、絞り込みます。例: ワイルドカードで`*.dll`。
4. 展開されたツリーと件数を確認して「コピー」。選んだフォルダからの階層を保持します。

Windowsのライト／ダーク設定に追従します。バージョンは画面上部または通知領域の「バージョン情報」で確認できます。

空フォルダとリンクはコピー対象外です。中断時にはコピー済みのファイルや作成したフォルダが残る場合があります。タブなどでコピー先を判別できない場合は選択画面が表示されます。

## 解除

通知領域からFilterCopyを終了した後、実行します。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Uninstall.ps1
```

実行ファイルと履歴は残します。履歴は`%LOCALAPPDATA%\FilterCopy\history.json`に保存されます。共有するZIPに利用中の履歴や設定を追加しないでください。
