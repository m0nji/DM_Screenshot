# Generates windows/DMShot/Resources/AppIcon.ico from the DM camera-in-viewfinder art.
#
# Why this exists (Windows-specific deviation from the macOS source of truth):
#   macOS keeps the Apple icon grid (824x824 plate inset in a 1024 canvas, ~10% margin)
#   because the OS composites its own squircle/shadow around it. Windows has no such grid:
#   taskbar/Alt-Tab icons are expected to FILL the tile. The shared macOS AppIcon.svg art,
#   rendered at that 80% fill, reads tiny on the dark Windows taskbar (the near-black plate
#   blends into the bar, leaving only the small white motif). So Windows uses the *same*
#   motif but scaled to nearly fill the canvas. macOS art is untouched.
#
# No ImageMagick/rsvg/inkscape on the build box, so we rasterize with WPF directly.
# Run:  pwsh -File windows/tools/make-app-icon.ps1   (or powershell.exe -File ...)

Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase, System.Xaml

$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$out  = Join-Path $repo 'windows/DMShot/Resources/AppIcon.ico'
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256

# Uniform scale of the macOS art about the canvas centre so the 824px plate fills the
# 1024 canvas with a small (20px) margin: half-extent 412 -> 492  =>  S = 492/412.
$FILL = 492.0 / 412.0

# The near-black plate blends into the dark taskbar, so the only thing the eye reads as
# "the icon" is the white motif — at the macOS grid size it's ~50% of the plate and looks
# tiny next to neighbours that fill their tile. Enlarge the motif (Windows-only; macOS art
# untouched) about the canvas centre so it fills ~70% of the plate and reads at full size.
$MOTIF = 1.35

function New-Brush-Vertical([object[]]$stops, [double]$bx, [double]$by, [double]$bw, [double]$bh) {
    $g = New-Object Windows.Media.LinearGradientBrush
    $g.StartPoint = New-Object Windows.Point(0, 0)
    $g.EndPoint   = New-Object Windows.Point(0, 1)
    foreach ($s in $stops) {
        $g.GradientStops.Add((New-Object Windows.Media.GradientStop($s[1], $s[0])))
    }
    # MappingMode stays relative (default), so it spans whatever geometry it fills.
    $g
}

function C([byte]$r, [byte]$g, [byte]$b, [double]$a = 1.0) {
    [Windows.Media.Color]::FromArgb([byte]([math]::Round($a * 255)), $r, $g, $b)
}

function Render-Png([int]$N) {
    $rtb = New-Object Windows.Media.Imaging.RenderTargetBitmap($N, $N, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $dv  = New-Object Windows.Media.DrawingVisual
    $dc  = $dv.RenderOpen()

    # 1024 design space -> N px, then scale art to fill about centre (512,512).
    $dc.PushTransform((New-Object Windows.Media.ScaleTransform(($N / 1024.0), ($N / 1024.0))))
    $dc.PushTransform((New-Object Windows.Media.ScaleTransform($FILL, $FILL, 512, 512)))

    # --- Squircle plate: top-down gradient #21212b -> #0c0c12 ---
    $plate = New-Object Windows.Media.RectangleGeometry((New-Object Windows.Rect(100, 100, 824, 824)), 185, 185)
    $bg = New-Brush-Vertical @(@(0.0, (C 0x21 0x21 0x2b)), @(1.0, (C 0x0c 0x0c 0x12)))
    $dc.DrawGeometry($bg, $null, $plate)

    # --- Glass rim highlight on the upper half (clipped to the plate) ---
    $dc.PushClip($plate)
    $rim = New-Brush-Vertical @(@(0.0, (C 0xff 0xff 0xff 0.22)), @(0.5, (C 0xff 0xff 0xff 0.03)), @(1.0, (C 0xff 0xff 0xff 0.0)))
    $dc.DrawGeometry($rim, $null, (New-Object Windows.Media.RectangleGeometry((New-Object Windows.Rect(100, 100, 824, 412)), 185, 185)))
    $dc.Pop()

    # --- Faint white edge stroke ---
    $edgePen = New-Object Windows.Media.Pen((New-Object Windows.Media.SolidColorBrush((C 0xff 0xff 0xff 0.12))), 3)
    $dc.DrawGeometry($null, $edgePen, (New-Object Windows.Media.RectangleGeometry((New-Object Windows.Rect(101.5, 101.5, 821, 821)), 183.5, 183.5)))

    # --- Camera-in-viewfinder motif (pure white), same coords as macOS AppIcon.svg ---
    # Enlarged about the centre (Windows-only) so the white art reads at full tile size.
    $dc.PushTransform((New-Object Windows.Media.ScaleTransform($MOTIF, $MOTIF, 512, 512)))
    $white = New-Object Windows.Media.SolidColorBrush([Windows.Media.Colors]::White)

    $bracketPen = New-Object Windows.Media.Pen($white, 40)
    $bracketPen.StartLineCap = [Windows.Media.PenLineCap]::Round
    $bracketPen.EndLineCap   = [Windows.Media.PenLineCap]::Round
    $bracketPen.LineJoin     = [Windows.Media.PenLineJoin]::Round
    $brackets = [Windows.Media.Geometry]::Parse('M300,384 L300,300 L384,300 M640,300 L724,300 L724,384 M300,640 L300,724 L384,724 M724,640 L724,724 L640,724')
    $dc.DrawGeometry($null, $bracketPen, $brackets)

    # Camera body = (rounded body + flash bump) with the lens hole cut out, plus a centre dot.
    $body = New-Object Windows.Media.RectangleGeometry((New-Object Windows.Rect(412, 455, 200, 132)), 30, 30)
    $bump = New-Object Windows.Media.RectangleGeometry((New-Object Windows.Rect(468, 432, 88, 34)), 14, 14)
    $bodyU = New-Object Windows.Media.CombinedGeometry([Windows.Media.GeometryCombineMode]::Union, $body, $bump)
    $lens  = New-Object Windows.Media.EllipseGeometry((New-Object Windows.Point(512, 521)), 46, 46)
    $holed = New-Object Windows.Media.CombinedGeometry([Windows.Media.GeometryCombineMode]::Exclude, $bodyU, $lens)
    $dc.DrawGeometry($white, $null, $holed)
    $dc.DrawGeometry($white, $null, (New-Object Windows.Media.EllipseGeometry((New-Object Windows.Point(512, 521)), 14, 14)))

    $dc.Pop()            # MOTIF scale
    $dc.Pop(); $dc.Pop()
    $dc.Close()
    $rtb.Render($dv)

    $enc = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($rtb))
    $ms = New-Object IO.MemoryStream
    $enc.Save($ms)
    , $ms.ToArray()
}

# --- Assemble a PNG-compressed .ico (ICONDIR + entries + PNG payloads) ---
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
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))  # width  (0 => 256)
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))  # height (0 => 256)
    $bw.Write([byte]0)            # palette count
    $bw.Write([byte]0)            # reserved
    $bw.Write([uint16]1)          # color planes
    $bw.Write([uint16]32)         # bits per pixel
    $bw.Write([uint32]$data.Length)
    $bw.Write([uint32]$offset)
    $offset += $data.Length
}
foreach ($s in $sizes) { $bw.Write($pngs[$s]) }
$bw.Flush()
[IO.File]::WriteAllBytes($out, $ms.ToArray())
Write-Host "wrote $out ($($sizes.Count) frames: $($sizes -join ', '))"
