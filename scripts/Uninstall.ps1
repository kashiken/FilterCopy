$ErrorActionPreference = 'Stop'
foreach ($name in @('Directory\Background', 'Directory', 'Drive')) {
    [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree("Software\Classes\$name\shell\FilterCopy", $false)
}
$startupLink = Join-Path ([Environment]::GetFolderPath('Startup')) 'FilterCopy.lnk'
if (Test-Path -LiteralPath $startupLink) { Remove-Item -LiteralPath $startupLink }
Write-Host '右クリック登録と自動起動を解除しました。常駐中の場合は通知領域のFilterCopyから終了してください。アプリ本体と履歴は残しています。'
