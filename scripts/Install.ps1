[CmdletBinding(SupportsShouldProcess)]
param([switch]$NoStartup, [string]$ExePath, [switch]$FrameworkDependent)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ExePath)) {
    $packagedExe = Join-Path $PSScriptRoot 'FilterCopy.exe'
    $ExePath = if (Test-Path -LiteralPath $packagedExe -PathType Leaf) { $packagedExe }
        else { Join-Path $projectRoot 'bin\Release\net10.0-windows\FilterCopy.exe' }
}
if (-not (Test-Path -LiteralPath $ExePath -PathType Leaf)) {
    throw "実行ファイルがありません: $ExePath`n先に dotnet build FilterCopy.csproj -c Release を実行するか、-ExePath で既存の実行ファイルを指定してください。"
}
$exePath = (Resolve-Path -LiteralPath $ExePath).ProviderPath
$appDirectory = Split-Path -Parent $exePath
if (-not $PSCmdlet.ShouldProcess($exePath, '現在のユーザーの右クリックメニュー・自動起動を登録')) { return }
$entries = @(
    @{ Key = 'Software\Classes\Directory\Background\shell\FilterCopy'; Target = '%V' },
    @{ Key = 'Software\Classes\Directory\shell\FilterCopy'; Target = '%1' },
    @{ Key = 'Software\Classes\Drive\shell\FilterCopy'; Target = '%1' }
)
foreach ($entry in $entries) {
    $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($entry.Key)
    try { $key.SetValue('', 'フィルタして貼り付け'); $key.SetValue('Icon', $exePath) } finally { $key.Dispose() }
    $commandKey = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($entry.Key + '\command')
    try { $commandKey.SetValue('', ('"{0}" --destination "{1}\."' -f $exePath, $entry.Target)) } finally { $commandKey.Dispose() }
}
if (-not $NoStartup) {
    $startupLink = Join-Path ([Environment]::GetFolderPath('Startup')) 'FilterCopy.lnk'
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($startupLink)
    $shortcut.TargetPath = $exePath
    $shortcut.Arguments = '--resident'
    $shortcut.WorkingDirectory = $appDirectory
    $shortcut.Save()
    Start-Process -FilePath $exePath -ArgumentList '--resident' -WindowStyle Hidden
}
Write-Host "登録しました: $exePath"
Write-Host 'コピー元をCtrl+C → コピー先で右クリック（その他のオプション）→ フィルタして貼り付け'
Write-Host '常駐時はCtrl+Alt+Vでも開けます。'
