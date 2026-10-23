$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$images = @()
foreach ($size in $sizes) {
    $bitmap = New-Object System.Drawing.Bitmap $size,$size
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.ScaleTransform($size / 32.0, $size / 32.0)
    $background = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255,40,106,180))
    $foreground = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $stream = New-Object System.IO.MemoryStream
    try {
        $graphics.FillEllipse($background, 1, 1, 30, 30)
        $graphics.FillRectangle($foreground, 8, 16, 3, 7)
        $graphics.FillRectangle($foreground, 14, 11, 3, 12)
        $graphics.FillRectangle($foreground, 20, 7, 3, 16)
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        $images += ,$stream.ToArray()
    } finally { $stream.Dispose(); $foreground.Dispose(); $background.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
}
New-Item -ItemType Directory -Force "$PSScriptRoot/Assets" | Out-Null
$output = [System.IO.File]::Create("$PSScriptRoot/Assets/UsageMon.ico")
$writer = New-Object System.IO.BinaryWriter $output
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($index = 0; $index -lt $sizes.Count; $index++) {
        $dimension = if ($sizes[$index] -eq 256) { 0 } else { $sizes[$index] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$images[$index].Length); $writer.Write([uint32]$offset)
        $offset += $images[$index].Length
    }
    foreach ($png in $images) { $writer.Write([byte[]]$png) }
} finally { $writer.Dispose(); $output.Dispose() }
