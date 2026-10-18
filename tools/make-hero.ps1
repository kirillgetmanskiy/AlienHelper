# Microsoft Store 16:9 "Super hero art": store-listing\hero\SuperHero-3840x2160.png and -1920x1080.png.
# No text (the Store overlays the title and buttons, bottom left), so one image serves every language.
# Drawn from vectors: the alien head of the app icon on a dark gradient, with faint chart lines behind it.
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'store-listing\hero'
New-Item -ItemType Directory -Force $out | Out-Null

function C([string]$hex, [int]$a = 255) { [System.Drawing.Color]::FromArgb($a, [System.Drawing.ColorTranslator]::FromHtml($hex)) }

function Draw-Hero([int]$W, [int]$H) {
    $k = $W / 3840.0
    $bmp = New-Object System.Drawing.Bitmap $W, $H
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.InterpolationMode = 'HighQualityBicubic'

    # background: deep diagonal gradient
    $rect = New-Object System.Drawing.RectangleF 0, 0, $W, $H
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, (C '#05070B'), (C '#0E1C2C'), 20
    $g.FillRectangle($bg, $rect)

    # glow behind the head (right)
    $cx = 2720 * $k; $cy = 1060 * $k
    # one smooth radial gradient (stacked circles would show rings)
    $rad = 1600 * $k
    $ell = New-Object System.Drawing.Drawing2D.GraphicsPath
    $ell.AddEllipse([float]($cx - $rad), [float]($cy - $rad * 0.8), [float]($rad * 2), [float]($rad * 1.6))
    $glow = New-Object System.Drawing.Drawing2D.PathGradientBrush $ell
    $glow.CenterPoint = New-Object System.Drawing.PointF ([float]$cx), ([float]$cy)
    $glow.CenterColor = C '#1FB6FF' 85
    $glow.SurroundColors = @((C '#1FB6FF' 0))
    $blend = New-Object System.Drawing.Drawing2D.Blend 4
    $blend.Factors = [float[]]@(0, 0.25, 0.6, 1); $blend.Positions = [float[]]@(0, 0.35, 0.7, 1)
    $glow.Blend = $blend
    $g.FillPath($glow, $ell)

    # faint grid
    $grid = New-Object System.Drawing.Pen (C '#3A6A8C' 22), ([float](2 * $k))
    for ($x = 0; $x -le 3840; $x += 160) { $g.DrawLine($grid, [float]($x * $k), 0, [float]($x * $k), [float]$H) }
    for ($y = 0; $y -le 2160; $y += 160) { $g.DrawLine($grid, 0, [float]($y * $k), [float]$W, [float]($y * $k)) }

    # chart lines: temperature, fan speed and clock, drifting across the upper half
    $rnd = New-Object System.Random 7
    foreach ($ln in @(@{ c = '#FF6A3D'; base = 620; amp = 120; a = 70 }, @{ c = '#3DDC97'; base = 820; amp = 90; a = 60 }, @{ c = '#9B7BFF'; base = 470; amp = 150; a = 55 })) {
        $pts = New-Object 'System.Collections.Generic.List[System.Drawing.PointF]'
        $phase = $rnd.NextDouble() * 6
        for ($x = 0; $x -le 3840; $x += 40) {
            $y = $ln.base + [Math]::Sin($x / 310.0 + $phase) * $ln.amp * 0.6 + [Math]::Sin($x / 97.0 + $phase * 2) * $ln.amp * 0.25 + ($rnd.NextDouble() - 0.5) * 30
            $pts.Add((New-Object System.Drawing.PointF ([float]($x * $k)), ([float]($y * $k))))
        }
        $pen = New-Object System.Drawing.Pen (C $ln.c $ln.a), ([float](5 * $k))
        $pen.LineJoin = 'Round'
        $g.DrawLines($pen, $pts.ToArray())
    }

    # fan curve: a smooth rising line with points, lower right (behind the head's shoulder)
    $curve = @(@(1700, 1900), @(2050, 1880), @(2350, 1760), @(2650, 1560), @(2950, 1300), @(3300, 1080), @(3840, 1080))
    $cp = $curve | ForEach-Object { New-Object System.Drawing.PointF ([float]($_[0] * $k)), ([float]($_[1] * $k)) }
    $cpen = New-Object System.Drawing.Pen (C '#1FB6FF' 90), ([float](7 * $k))
    $g.DrawLines($cpen, [System.Drawing.PointF[]]$cp)
    $dot = New-Object System.Drawing.SolidBrush (C '#BFEFFF' 120)
    foreach ($p in $cp[1..5]) { $d = 22 * $k; $g.FillEllipse($dot, [float]($p.X - $d / 2), [float]($p.Y - $d / 2), [float]$d, [float]$d) }

    # the alien head (same shape as the app icon), centred at ($cx, $cy)
    $s = 1500 * $k
    $ox = $cx - $s / 2; $oy = $cy - $s / 2
    function P([double]$x, [double]$y) { New-Object System.Drawing.PointF ([float]($ox + $x * $s)), ([float]($oy + $y * $s)) }
    $head = New-Object System.Drawing.Drawing2D.GraphicsPath
    $head.AddBezier((P 0.50 0.90), (P 0.31 0.84), (P 0.16 0.62), (P 0.17 0.42))
    $head.AddBezier((P 0.17 0.42), (P 0.18 0.20), (P 0.33 0.10), (P 0.50 0.10))
    $head.AddBezier((P 0.50 0.10), (P 0.67 0.10), (P 0.82 0.20), (P 0.83 0.42))
    $head.AddBezier((P 0.83 0.42), (P 0.84 0.62), (P 0.69 0.84), (P 0.50 0.90))
    $head.CloseFigure()
    # soft outer glow
    foreach ($i in 1..10) {
        $gp = New-Object System.Drawing.Pen (C '#5FE3FF' ([int](14 - $i))), ([float](($i * 12) * $k))
        $gp.LineJoin = 'Round'
        $g.DrawPath($gp, $head)
    }
    $hb = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.RectangleF ([float]$ox), ([float]($oy + $s * 0.1)), ([float]$s), ([float]($s * 0.8))), (C '#5FE3FF'), (C '#1288D6'), 90
    $g.FillPath($hb, $head)
    $eye = New-Object System.Drawing.SolidBrush (C '#06080C')
    foreach ($side in -1, 1) {
        $state = $g.Save()
        $g.TranslateTransform([float]($ox + $s * (0.50 + 0.155 * $side)), [float]($oy + $s * 0.52))
        $g.RotateTransform([float](-28 * $side))
        $w = $s * 0.25; $h = $s * 0.115
        $g.FillEllipse($eye, [float](-$w / 2), [float](-$h / 2), [float]$w, [float]$h)
        $g.Restore($state)
    }

    # vignette towards the bottom, where the Store puts the title
    $vr = New-Object System.Drawing.RectangleF 0, ([float]($H * 0.55)), $W, ([float]($H * 0.45 + 1))
    $vg = New-Object System.Drawing.Drawing2D.LinearGradientBrush $vr, (C '#05070B' 0), (C '#05070B' 200), 90
    $g.FillRectangle($vg, $vr)

    $g.Dispose()
    return $bmp
}

foreach ($size in @(@(3840, 2160), @(1920, 1080))) {
    $b = Draw-Hero $size[0] $size[1]
    $file = Join-Path $out "SuperHero-$($size[0])x$($size[1]).png"
    $b.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
    $b.Dispose()
    "wrote $file"
}
