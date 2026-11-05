# Run with Windows PowerShell -STA. No external tools or packages are required.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase
$assetDirectory = Join-Path (Split-Path -Parent $PSScriptRoot) 'Assets'
[xml]$svg = [System.IO.File]::ReadAllText((Join-Path $assetDirectory 'FilterCopy.svg'))
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$frames = @()
foreach ($size in $sizes) {
    $visual = New-Object System.Windows.Media.DrawingVisual
    $context = $visual.RenderOpen()
    try {
        $context.PushTransform((New-Object System.Windows.Media.ScaleTransform(($size / 64.0), ($size / 64.0))))
        foreach ($path in $svg.svg.path) {
            $brush = [System.Windows.Media.BrushConverter]::new().ConvertFromInvariantString($path.fill)
            $context.DrawGeometry($brush, $null, [System.Windows.Media.Geometry]::Parse($path.d))
        }
        $context.Pop()
    } finally { $context.Close() }
    $bitmap = [System.Windows.Media.Imaging.RenderTargetBitmap]::new($size, $size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $memory = New-Object System.IO.MemoryStream
    try { $encoder.Save($memory); $frames += ,($memory.ToArray()) } finally { $memory.Dispose() }
}
$output = [System.IO.File]::Create((Join-Path $assetDirectory 'FilterCopy.ico'))
$writer = [System.IO.BinaryWriter]::new($output)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
} finally { $writer.Dispose() }
[System.IO.File]::WriteAllBytes((Join-Path $assetDirectory 'FilterCopy.png'), [byte[]]$frames[-1])
Write-Host 'Generated FilterCopy.ico (16-256px) and FilterCopy.png from FilterCopy.svg.'
