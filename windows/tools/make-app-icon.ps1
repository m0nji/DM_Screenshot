# Generates windows/DMShot/Resources/AppIcon.ico from the modern DM Screenshot
# BrandDesign art: dark squircle, subtle rim/accent glint, capture corners and
# aperture mark. mac/Resources/AppIcon.svg is the visual source of truth; this
# dependency-light WPF renderer mirrors the same coordinates for Windows boxes.
# Run:  pwsh -File windows/tools/make-app-icon.ps1   (or powershell.exe -File ...)

param(
    [string]$Preview = ''
)

Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase, System.Xaml

$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$out  = Join-Path $repo 'windows/DMShot/Resources/AppIcon.ico'
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256

function C([byte]$r, [byte]$g, [byte]$b, [double]$a = 1.0) {
    [Windows.Media.Color]::FromArgb([byte]([math]::Round($a * 255)), $r, $g, $b)
}

function VBrush([object[]]$stops) {
    $g = New-Object Windows.Media.LinearGradientBrush
    $g.StartPoint = New-Object Windows.Point(0, 0)
    $g.EndPoint   = New-Object Windows.Point(0, 1)
    foreach ($s in $stops) {
        $g.GradientStops.Add((New-Object Windows.Media.GradientStop($s[1], $s[0])))
    }
    $g
}

function RBrush([object[]]$stops, [double]$cx, [double]$cy, [double]$r) {
    $g = New-Object Windows.Media.RadialGradientBrush
    $g.Center = New-Object Windows.Point($cx, $cy)
    $g.GradientOrigin = New-Object Windows.Point($cx, $cy)
    $g.RadiusX = $r
    $g.RadiusY = $r
    foreach ($s in $stops) {
        $g.GradientStops.Add((New-Object Windows.Media.GradientStop($s[1], $s[0])))
    }
    $g
}

function MarkPen([Windows.Media.Color]$color, [double]$width) {
    $pen = New-Object Windows.Media.Pen((New-Object Windows.Media.SolidColorBrush($color)), $width)
    $pen.StartLineCap = [Windows.Media.PenLineCap]::Round
    $pen.EndLineCap   = [Windows.Media.PenLineCap]::Round
    $pen.LineJoin     = [Windows.Media.PenLineJoin]::Round
    $pen
}

$corners = [Windows.Media.Geometry]::Parse('M 344 338 H 278 V 404 M 680 338 H 746 V 404 M 344 686 H 278 V 620 M 680 686 H 746 V 620')
$aperture = [Windows.Media.Geometry]::Parse('M 512 392 L 616 452 V 572 L 512 632 L 408 572 V 452 Z M 512 462 L 556 488 V 536 L 512 562 L 468 536 V 488 Z')

function Draw-Mark($dc, $pen) {
    $dc.DrawGeometry($null, $pen, $script:corners)
    $dc.DrawGeometry($null, $pen, $script:aperture)
}

function Render-Png([int]$N) {
    $rtb = New-Object Windows.Media.Imaging.RenderTargetBitmap($N, $N, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $dv  = New-Object Windows.Media.DrawingVisual
    $dc  = $dv.RenderOpen()

    $dc.PushTransform((New-Object Windows.Media.ScaleTransform(($N / 1024.0), ($N / 1024.0))))

    $plate = New-Object Windows.Media.RectangleGeometry((New-Object Windows.Rect(100, 100, 824, 824)), 185, 185)
    $dc.DrawGeometry((VBrush @(@(0.0, (C 0x24 0x24 0x2d)), @(1.0, (C 0x08 0x08 0x0d)))), $null, $plate)

    $dc.PushClip($plate)
    $dc.DrawGeometry((VBrush @(@(0.0, (C 0xff 0xff 0xff 0.18)), @(0.55, (C 0xff 0xff 0xff 0.03)), @(1.0, (C 0xff 0xff 0xff 0.0)))), $null,
        (New-Object Windows.Media.RectangleGeometry((New-Object Windows.Rect(100, 100, 824, 412)), 185, 185))
    )
    $dc.DrawGeometry((RBrush @(@(0.0, (C 0xee 0x9a 0x5d 0.14)), @(1.0, (C 0xc9 0x7b 0x4a 0.0))) 0.45 0.35 0.42), $null,
        (New-Object Windows.Media.EllipseGeometry((New-Object Windows.Point(452, 364)), 226, 226))
    )
    $dc.Pop()

    $edge = New-Object Windows.Media.Pen((New-Object Windows.Media.SolidColorBrush((C 0xff 0xff 0xff 0.12))), 3)
    $dc.DrawGeometry($null, $edge, (New-Object Windows.Media.RectangleGeometry((New-Object Windows.Rect(101.5, 101.5, 821, 821)), 183.5, 183.5)))

    # Soft stacked strokes approximate the SVG drop-shadow filter at small sizes.
    $dc.PushTransform((New-Object Windows.Media.TranslateTransform(0, 18)))
    Draw-Mark $dc (MarkPen (C 0x00 0x00 0x00 0.22) 54)
    $dc.Pop()
    $dc.PushTransform((New-Object Windows.Media.TranslateTransform(0, 6)))
    Draw-Mark $dc (MarkPen (C 0xc9 0x7b 0x4a 0.12) 46)
    $dc.Pop()
    $dc.PushTransform((New-Object Windows.Media.TranslateTransform(0, -3)))
    Draw-Mark $dc (MarkPen (C 0xff 0xff 0xff 0.10) 38)
    $dc.Pop()
    Draw-Mark $dc (MarkPen (C 0xf5 0xf5 0xf7 0.86) 40)

    $dc.Pop()
    $dc.Close()
    $rtb.Render($dv)

    $enc = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($rtb))
    $ms = New-Object IO.MemoryStream
    $enc.Save($ms)
    , $ms.ToArray()
}

if ($Preview) {
    [IO.File]::WriteAllBytes($Preview, (Render-Png 256))
    Write-Host "preview -> $Preview"
    return
}

$pngs = @{}
foreach ($s in $sizes) { $pngs[$s] = Render-Png $s }

$ms = New-Object IO.MemoryStream
$bw = New-Object IO.BinaryWriter($ms)
$bw.Write([uint16]0)             # reserved
$bw.Write([uint16]1)             # type = icon
$bw.Write([uint16]$sizes.Count)  # count

$offset = 6 + 16 * $sizes.Count
foreach ($s in $sizes) {
    $data = $pngs[$s]
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]0)
    $bw.Write([byte]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]32)
    $bw.Write([uint32]$data.Length)
    $bw.Write([uint32]$offset)
    $offset += $data.Length
}
foreach ($s in $sizes) { $bw.Write($pngs[$s]) }
$bw.Flush()
[IO.File]::WriteAllBytes($out, $ms.ToArray())
Write-Host "wrote $out ($($sizes.Count) frames: $($sizes -join ', '))"
