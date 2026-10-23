# Regenerates the MSIX tile/logo PNGs from the app icon. Run on Windows: .\installer\make-msix-assets.ps1
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$icon = New-Object System.Drawing.Icon (Join-Path $root 'Assets\UsageMon.ico'), 256, 256
$source = $icon.ToBitmap()
$out = Join-Path $PSScriptRoot 'msix-assets'
New-Item -ItemType Directory -Force $out | Out-Null
$sizes = [ordered]@{
    'StoreLogo.png' = 50
    'Square44x44Logo.png' = 44
    'Square44x44Logo.targetsize-256.png' = 256
    'Square150x150Logo.png' = 150
}
foreach ($name in $sizes.Keys) {
    $size = $sizes[$name]
    $bitmap = New-Object System.Drawing.Bitmap $size, $size
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.InterpolationMode = 'HighQualityBicubic'
    $graphics.SmoothingMode = 'AntiAlias'
    $graphics.Clear([System.Drawing.Color]::Transparent)
    # Leave a little padding on the larger tile so the mark does not touch the edge.
    $inset = if ($size -ge 150) { [int]($size * 0.18) } else { 0 }
    $graphics.DrawImage($source, $inset, $inset, $size - 2 * $inset, $size - 2 * $inset)
    $bitmap.Save((Join-Path $out $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose(); $bitmap.Dispose()
}
$source.Dispose(); $icon.Dispose()
Get-ChildItem $out | Select-Object Name, Length
