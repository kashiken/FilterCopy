[CmdletBinding()]
param([string]$Archive)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$patterns = @{
    'Windows user profile path' = '[A-Za-z]:[\\/]Users[\\/][^\\/\s]+'
    'Unix user home path' = '/(?:home|Users)/[^/\s]+'
    'Private key' = '-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----'
    'GitHub token' = '(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,})'
    'AWS access key' = '(?:AKIA|ASIA)[A-Z0-9]{16}'
    'Email address' = '[A-Za-z0-9._%+-]{1,64}@[A-Za-z0-9.-]{1,253}\.[A-Za-z]{2,24}'
}
$privateNames = @($env:USERNAME, [IO.Path]::GetFileName($env:USERPROFILE), $env:COMPUTERNAME) | Where-Object { $_ -and $_.Length -ge 4 } | Select-Object -Unique
$problems = [Collections.Generic.List[string]]::new()
function Test-Bytes([byte[]]$Bytes, [string]$Name) {
    $Name = $Name.Replace('\', '/')
    $text = [Text.Encoding]::UTF8.GetString($Bytes) + "`n" + [Text.Encoding]::Unicode.GetString($Bytes)
    foreach ($entry in $patterns.GetEnumerator()) {
        # Vendor runtime metadata can contain example addresses or random binary matches.
        # Check addresses in our own files; still check every file for local identity/paths and tokens.
        if ($entry.Key -eq 'Email address' -and (($Name -match '\.dll$' -and $Name -notmatch '(?:^|/)FilterCopy\.dll$') -or $Name -match '^licenses/')) { continue }
        if ($text -cmatch $entry.Value) { $problems.Add("$Name : $($entry.Key)") }
    }
    foreach ($privateName in $privateNames) {
        if ($text.IndexOf($privateName, [StringComparison]::OrdinalIgnoreCase) -ge 0) { $problems.Add("$Name : local identity") }
    }
}
if ($Archive) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $Archive).ProviderPath)
    try {
        foreach ($entry in $zip.Entries) {
            if (-not $entry.Name) { continue }
            if ($entry.FullName -match '(?i)(?:^|/)(?:\.git|\.aws|\.codex|\.env|history\.json)(?:/|$)|\.(?:pdb|pfx|p12|key|pem|log|binlog)$') { $problems.Add("$($entry.FullName) : excluded file type") }
            $memory = [IO.MemoryStream]::new(); $stream = $entry.Open()
            try { $stream.CopyTo($memory); Test-Bytes $memory.ToArray() $entry.FullName }
            finally { $stream.Dispose(); $memory.Dispose() }
        }
    } finally { $zip.Dispose() }
} else {
    $tracked = & git -C $projectRoot ls-files
    if ($LASTEXITCODE -ne 0) { throw 'Initialize Git and stage the intended source files before scanning.' }
    if (-not $tracked) { throw 'No staged/tracked files to scan.' }
    foreach ($name in $tracked) {
        Test-Bytes ([IO.File]::ReadAllBytes((Join-Path $projectRoot $name))) $name
    }
}
if ($problems.Count -gt 0) { throw ("Review these findings before publishing:`n" + ($problems -join "`n")) }
Write-Host 'Public-file scan passed (paths, local identities, email addresses, known token formats and private keys).'
