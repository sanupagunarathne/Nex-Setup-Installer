# Regenerate the Windows icon and sidebar PNG from the STAK.svg geometry.
# Uses only Windows' System.Drawing; no external image packages are needed.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetRoot = Join-Path $PSScriptRoot '../NexInstaller.App/Assets'
$bitmap = [System.Drawing.Bitmap]::new(1024, 1024)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.ScaleTransform(4, 4)
$tile = [System.Drawing.Drawing2D.GraphicsPath]::new()
foreach ($arc in @(@(0,0,104,104,180,90), @(152,0,104,104,270,90), @(152,152,104,104,0,90), @(0,152,104,104,90,90))) {
    $tile.AddArc($arc[0],$arc[1],$arc[2],$arc[3],$arc[4],$arc[5])
}
$tile.CloseFigure()
$background = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#121212'))
$graphics.FillPath($background, $tile)
function Draw-Layer([int[]]$Coordinates, [int]$Alpha) {
    $points = for ($index = 0; $index -lt $Coordinates.Length; $index += 2) {
        [System.Drawing.PointF]::new($Coordinates[$index], $Coordinates[$index + 1])
    }
    $brush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb($Alpha,250,245,248))
    try { $graphics.FillPolygon($brush, [System.Drawing.PointF[]]$points) } finally { $brush.Dispose() }
}
Draw-Layer @(36,154,128,200,220,154,220,180,128,226,36,180) 153
Draw-Layer @(36,113,128,159,220,113,220,139,128,185,36,139) 204
Draw-Layer @(36,80,128,34,220,80,128,126) 255
Draw-Layer @(36,80,128,126,220,80,220,106,128,152,36,106) 230
$graphics.Dispose()
$background.Dispose()
$tile.Dispose()
$sizes = @(16,24,32,48,64,128,256)
$images = foreach ($size in $sizes) {
    $scaled = [System.Drawing.Bitmap]::new($size, $size)
    $canvas = [System.Drawing.Graphics]::FromImage($scaled)
    $canvas.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $canvas.DrawImage($bitmap,0,0,$size,$size)
    $canvas.Dispose()
    $stream = [System.IO.MemoryStream]::new()
    $scaled.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    if ($size -eq 256) { [System.IO.File]::WriteAllBytes((Join-Path $assetRoot 'STAK.png'), $stream.ToArray()) }
    ,$stream.ToArray()
    $stream.Dispose()
    $scaled.Dispose()
}
$bitmap.Dispose()
$writer = [System.IO.BinaryWriter]::new([System.IO.File]::Create((Join-Path $assetRoot 'STAK.ico')))
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
    foreach ($bytes in $images) { $writer.Write([byte[]]$bytes) }
} finally { $writer.Dispose() }
