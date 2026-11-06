[CmdletBinding()]
param([switch]$FrameworkDependent, [string]$PublishedDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$buildId = [Guid]::NewGuid().ToString('N')
$publishDirectory = Join-Path $projectRoot ('artifacts\staging\' + $buildId)
$releaseDirectory = Join-Path $projectRoot 'artifacts\releases'
if ($PublishedDirectory) {
    $publishDirectory = (Resolve-Path -LiteralPath $PublishedDirectory).ProviderPath
} else {
    $selfContained = if ($FrameworkDependent) { 'false' } else { 'true' }
    dotnet publish (Join-Path $projectRoot 'FilterCopy.csproj') -c Release -r win-x64 --self-contained $selfContained -o $publishDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
}
$runtimeConfig = Get-Content -LiteralPath (Join-Path $publishDirectory 'FilterCopy.runtimeconfig.json') -Raw | ConvertFrom-Json
$hasBundledRuntime = $null -ne $runtimeConfig.runtimeOptions.includedFrameworks
if ($FrameworkDependent.IsPresent -eq $hasBundledRuntime) { throw 'Publication runtime type does not match the requested package type.' }
$binaryInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $publishDirectory 'FilterCopy.exe'))
$version = $binaryInfo.ProductVersion
if ($version -notmatch '^1\.[0-9]+$') { throw "Unexpected application version: $version" }
$flavor = if ($FrameworkDependent) { 'framework-dependent' } else { 'self-contained' }
$baseName = "FilterCopy-$version-win-x64-$flavor"
$packageDirectory = Join-Path $projectRoot ('artifacts\packages\' + $baseName + '-' + $buildId)
$null = New-Item -ItemType Directory -Path $packageDirectory -Force
$null = New-Item -ItemType Directory -Path $releaseDirectory -Force
# Package only runtime files from this fresh publication. Never add the source tree wholesale.
foreach ($file in Get-ChildItem -LiteralPath $publishDirectory -Recurse -File) {
    if ($file.Extension -in @('.exe', '.dll', '.json', '.dat')) {
        $relative = $file.FullName.Substring($publishDirectory.Length).TrimStart([char]'\')
        $target = Join-Path $packageDirectory $relative
        $null = New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force
        Copy-Item -LiteralPath $file.FullName -Destination $target
    }
}
foreach ($name in @('Install.ps1', 'Uninstall.ps1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $packageDirectory
}
if ($hasBundledRuntime) {
    $assets = Get-Content -LiteralPath (Join-Path $projectRoot 'obj\project.assets.json') -Raw | ConvertFrom-Json
    foreach ($framework in $runtimeConfig.runtimeOptions.includedFrameworks) {
        $packName = $framework.name.ToLowerInvariant() + '.runtime.win-x64'
        $packDirectory = $null
        foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
            $candidate = Join-Path $folder ($packName + '\' + $framework.version)
            if (Test-Path -LiteralPath $candidate -PathType Container) { $packDirectory = $candidate; break }
        }
        if (-not $packDirectory) { throw "Runtime license package not found: $packName" }
        $licenseDirectory = Join-Path $packageDirectory ('licenses\' + $framework.name)
        $null = New-Item -ItemType Directory -Path $licenseDirectory -Force
        foreach ($name in @('LICENSE', 'LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT')) {
            $document = Join-Path $packDirectory $name
            if (Test-Path -LiteralPath $document -PathType Leaf) { Copy-Item -LiteralPath $document -Destination $licenseDirectory }
        }
        if (-not (Get-ChildItem -LiteralPath $licenseDirectory -File)) { throw "Runtime license missing: $packName" }
    }
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\USER_GUIDE.md') -Destination (Join-Path $packageDirectory 'README.md')
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $packageDirectory
$zip = Join-Path $releaseDirectory ($baseName + '.zip')
if (Test-Path -LiteralPath $zip) { throw "Archive already exists: $zip" }
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem -LiteralPath $packageDirectory -Recurse -File) {
        $relative = $file.FullName.Substring($packageDirectory.Length).TrimStart([char]'\').Replace('\', '/')
        $null = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $relative, [IO.Compression.CompressionLevel]::Optimal)
    }
} finally { $archive.Dispose() }
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText(($zip + '.sha256'), ($hash + '  ' + [IO.Path]::GetFileName($zip) + "`n"), [Text.Encoding]::ASCII)
$notes = "# FilterCopy $version`n`nWindows x64 / $flavor`n`n- Include/exclude filters with normal, wildcard and regular-expression modes, saved together in history.`n- Expanded file tree and Shell batch copy preserving folder structure.`n- System light/dark theme and Explorer context menu / Ctrl+Alt+V integration.`n`nSee the user guide in README.md inside the ZIP for setup. Unsigned build.`n"
[IO.File]::WriteAllText((Join-Path $releaseDirectory ($baseName + '-notes.md')), $notes, [Text.UTF8Encoding]::new($false))
Write-Host "Created: $zip"
Write-Host "SHA256: $hash"
