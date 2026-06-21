# Generates windows/DMShot/Resources/TrayIcon.ico — the Windows tray glyph.
#
# The tray shows ONLY the camera (no viewfinder corner brackets), enlarged to read at
# 16-24px. Two variants:
#   -Variant tile  : dark squircle + white camera (matches the app icon, no brackets)
#   -Variant glyph : white camera only on transparent (matches neighbour tray glyphs)
# -Preview <path> renders a single 128px PNG instead of writing the .ico (for review).
#
# WPF rasterizer (no ImageMagick/rsvg on the build box). See make-app-icon.ps1.

param(
    [ValidateSet('tile', 'glyph')] [string]$Variant = 'tile',
    [string]$Preview = ''
)

Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase, System.Xaml
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$out  = Join-Path $repo 'windows/DMShot/Resources/TrayIcon.ico'
$sizes = 16, 20, 24, 32, 40, 48

$FILL = 492.0 / 412.0          # squircle fill (tile variant), same as the app icon
$CAM_TILE  = 2.4               # camera enlargement inside the squircle
$CAM_GLYPH = 3.6               # camera enlargement filling the bare glyph

function C([byte]$r, [byte]$g, [byte]$b, [double]$a = 1.0) {
    [Windows.Media.Color]::FromArgb([byte]([math]::Round($a * 255)), $r, $g, $b)
}
function VBrush([object[]]$stops) {
    $g = New-Object Windows.Media.LinearGradientBrush
    $g.StartPoint = New-Object Windows.Point(0, 0); $g.EndPoint = New-Object Windows.Point(0, 1)
    foreach ($s in $stops) { $g.GradientStops.Add((New-Object Windows.Media.GradientStop($s[1], $s[0]))) }
    $g
}

# Soft off-white (#ECECF0) camera body (rounded body + flash bump, lens hole cut out) + centre
# dot, in macOS AppIcon.svg coordinates (centred ~512,509.5). Off-white (not pure white) so the
# glyph reads slightly grey instead of stark white (parity with AppIcon.svg).
function Draw-Camera($dc) {
    $white = New-Object Windows.Media.SolidColorBrush((C 0xEC 0xEC 0xF0))
    $body = New-Object Windows.Media.RectangleGeometry((New-Object Windows.Rect(412, 455, 200, 132)), 30, 30)
    $bump = New-Object Windows.Media.RectangleGeometry((New-Object Windows.Rect(468, 432, 88, 34)), 14, 14)
    $bodyU = New-Object Windows.Media.CombinedGeometry([Windows.Media.GeometryCombineMode]::Union, $body, $bump)
    $lens  = New-Object Windows.Media.EllipseGeometry((New-Object Windows.Point(512, 521)), 46, 46)
    $holed = New-Object Windows.Media.CombinedGeometry([Windows.Media.GeometryCombineMode]::Exclude, $bodyU, $lens)
    $dc.DrawGeometry($white, $null, $holed)
    $dc.DrawGeometry($white, $null, (New-Object Windows.Media.EllipseGeometry((New-Object Windows.Point(512, 521)), 14, 14)))
}

function Render-Png([int]$N) {
    $rtb = New-Object Windows.Media.Imaging.RenderTargetBitmap($N, $N, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $dv  = New-Object Windows.Media.DrawingVisual
    $dc  = $dv.RenderOpen()
    $dc.PushTransform((New-Object Windows.Media.ScaleTransform(($N / 1024.0), ($N / 1024.0))))

    if ($Variant -eq 'tile') {
        $dc.PushTransform((New-Object Windows.Media.ScaleTransform($FILL, $FILL, 512, 512)))
        $plate = New-Object Windows.Media.RectangleGeometry((New-Object Windows.Rect(100, 100, 824, 824)), 185, 185)
        $dc.DrawGeometry((VBrush @(@(0.0, (C 0x21 0x21 0x2b)), @(1.0, (C 0x0c 0x0c 0x12)))), $null, $plate)
        $dc.PushClip($plate)
        $dc.DrawGeometry((VBrush @(@(0.0, (C 0xff 0xff 0xff 0.22)), @(0.5, (C 0xff 0xff 0xff 0.03)), @(1.0, (C 0xff 0xff 0xff 0.0)))), $null,
            (New-Object Windows.Media.RectangleGeometry((New-Object Windows.Rect(100, 100, 824, 412)), 185, 185)))
        $dc.Pop()
        $edge = New-Object Windows.Media.Pen((New-Object Windows.Media.SolidColorBrush((C 0xff 0xff 0xff 0.12))), 3)
        $dc.DrawGeometry($null, $edge, (New-Object Windows.Media.RectangleGeometry((New-Object Windows.Rect(101.5, 101.5, 821, 821)), 183.5, 183.5)))
        $dc.PushTransform((New-Object Windows.Media.ScaleTransform($CAM_TILE, $CAM_TILE, 512, 509.5)))
        Draw-Camera $dc
        $dc.Pop(); $dc.Pop()
    }
    else {
        $dc.PushTransform((New-Object Windows.Media.ScaleTransform($CAM_GLYPH, $CAM_GLYPH, 512, 509.5)))
        Draw-Camera $dc
        $dc.Pop()
    }

    $dc.Pop(); $dc.Close(); $rtb.Render($dv)
    $enc = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($rtb))
    $ms = New-Object IO.MemoryStream; $enc.Save($ms); , $ms.ToArray()
}

if ($Preview) {
    [IO.File]::WriteAllBytes($Preview, (Render-Png 128))
    Write-Host "preview ($Variant) -> $Preview"; return
}

$pngs = @{}; foreach ($s in $sizes) { $pngs[$s] = Render-Png $s }
$ms = New-Object IO.MemoryStream; $bw = New-Object IO.BinaryWriter($ms)
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
foreach ($s in $sizes) {
    $data = $pngs[$s]
    $bw.Write([byte]$s); $bw.Write([byte]$s); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$data.Length); $bw.Write([uint32]$offset)
    $offset += $data.Length
}
foreach ($s in $sizes) { $bw.Write($pngs[$s]) }
$bw.Flush(); [IO.File]::WriteAllBytes($out, $ms.ToArray())
Write-Host "wrote $out (variant=$Variant; $($sizes -join ', '))"
