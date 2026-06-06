Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$assetDir = Join-Path $repoRoot 'src\MetroDiagram.Viewer\Assets'
$pngPath = Join-Path $assetDir 'MetroDiagramViewerIcon-256.png'
$icoPath = Join-Path $assetDir 'MetroDiagramViewer.ico'

New-Item -ItemType Directory -Force $assetDir | Out-Null

function New-IconBitmap {
    param([int]$Size)

    $bmp = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = $Size / 256.0
    function Scale([double]$Value) { [single]($Value * $script:IconScale) }
    $script:IconScale = $s

    function RoundedRectPath([System.Drawing.RectangleF]$Rect, [single]$Radius) {
        $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
        $d = $Radius * 2
        $path.AddArc($Rect.X, $Rect.Y, $d, $d, 180, 90)
        $path.AddArc($Rect.Right - $d, $Rect.Y, $d, $d, 270, 90)
        $path.AddArc($Rect.Right - $d, $Rect.Bottom - $d, $d, $d, 0, 90)
        $path.AddArc($Rect.X, $Rect.Bottom - $d, $d, $d, 90, 90)
        $path.CloseFigure()
        $path
    }

    function DrawRoute([object[]]$RawPoints, [System.Drawing.Color]$Color, [double]$Width) {
        $pen = [System.Drawing.Pen]::new($Color, (Scale $Width))
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        $points = [System.Drawing.PointF[]]::new($RawPoints.Count)
        for ($i = 0; $i -lt $RawPoints.Count; $i++) {
            $points[$i] = [System.Drawing.PointF]::new((Scale $RawPoints[$i][0]), (Scale $RawPoints[$i][1]))
        }
        $g.DrawLines($pen, $points)
        $pen.Dispose()
    }

    $bgRect = [System.Drawing.RectangleF]::new((Scale 18), (Scale 18), (Scale 220), (Scale 220))
    $bgPath = RoundedRectPath $bgRect (Scale 38)
    $bgBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        $bgRect,
        [System.Drawing.Color]::FromArgb(255, 11, 92, 173),
        [System.Drawing.Color]::FromArgb(255, 16, 42, 67),
        45)
    $g.FillPath($bgBrush, $bgPath)

    $panelRect = [System.Drawing.RectangleF]::new((Scale 52), (Scale 50), (Scale 152), (Scale 132))
    $panelPath = RoundedRectPath $panelRect (Scale 18)
    $panelBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        $panelRect,
        [System.Drawing.Color]::FromArgb(250, 255, 255, 255),
        [System.Drawing.Color]::FromArgb(240, 217, 240, 255),
        45)
    $g.FillPath($panelBrush, $panelPath)
    $g.DrawPath([System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 199, 215, 232), (Scale 6)), $panelPath)

    DrawRoute @(@(72, 148), @(104, 116), @(132, 116), @(172, 78)) ([System.Drawing.Color]::FromArgb(255, 34, 197, 94)) 18
    DrawRoute @(@(74, 86), @(106, 118), @(136, 146), @(180, 146)) ([System.Drawing.Color]::FromArgb(255, 245, 158, 11)) 18
    DrawRoute @(@(74, 86), @(106, 118), @(136, 146), @(180, 146)) ([System.Drawing.Color]::FromArgb(232, 255, 255, 255)) 5

    foreach ($p in @(@(74, 86), @(106, 118), @(136, 146), @(180, 146))) {
        $r = Scale 12
        $circle = [System.Drawing.RectangleF]::new((Scale $p[0]) - $r, (Scale $p[1]) - $r, $r * 2, $r * 2)
        $g.FillEllipse([System.Drawing.Brushes]::White, $circle)
        $g.DrawEllipse([System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 31, 41, 55), (Scale 5)), $circle)
    }

    DrawRoute @(@(76, 204), @(180, 204)) ([System.Drawing.Color]::FromArgb(255, 229, 231, 235)) 12
    DrawRoute @(@(92, 204), @(150, 204)) ([System.Drawing.Color]::FromArgb(255, 56, 189, 248)) 6
    $r2 = Scale 9
    $smallCircle = [System.Drawing.RectangleF]::new((Scale 188) - $r2, (Scale 204) - $r2, $r2 * 2, $r2 * 2)
    $g.FillEllipse([System.Drawing.Brushes]::White, $smallCircle)
    $g.DrawEllipse([System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 31, 41, 55), (Scale 4)), $smallCircle)

    $g.Dispose()
    $bmp
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$frames = [System.Collections.ArrayList]::new()

foreach ($size in $sizes) {
    $bmp = New-IconBitmap -Size $size
    $stream = [System.IO.MemoryStream]::new()
    $bmp.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    if ($size -eq 256) {
        $bmp.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
    }

    [void]$frames.Add([pscustomobject]@{
        Size = $size
        Bytes = $stream.ToArray()
        Bitmap = $bmp
        Stream = $stream
    })
}

$file = [System.IO.File]::Create($icoPath)
$writer = [System.IO.BinaryWriter]::new($file)
$writer.Write([UInt16]0)
$writer.Write([UInt16]1)
$writer.Write([UInt16]$frames.Count)

$offset = 6 + (16 * $frames.Count)
foreach ($frame in $frames) {
    $sizeByte = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
    $writer.Write([byte]$sizeByte)
    $writer.Write([byte]$sizeByte)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]32)
    $writer.Write([UInt32]$frame.Bytes.Length)
    $writer.Write([UInt32]$offset)
    $offset += $frame.Bytes.Length
}

foreach ($frame in $frames) {
    $writer.Write($frame.Bytes)
}

$writer.Dispose()
$file.Dispose()

foreach ($frame in $frames) {
    $frame.Bitmap.Dispose()
    $frame.Stream.Dispose()
}

Write-Host "Viewer icon written to $icoPath"
Write-Host "Preview PNG written to $pngPath"
