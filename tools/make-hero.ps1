# Microsoft Store 16:9 "Super hero art": store-listing\hero\SuperHero-3840x2160.png and -1920x1080.png.
# No text (the Store overlays the title and buttons, bottom left), so one image serves every language.
# Drawn from vectors: the app icon ("AH" monogram tile) on a dark gradient, with faint chart lines behind it.
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

    # glow behind the icon (right)
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

    # fan curve: a smooth rising line with points, lower right (behind the icon)
    $curve = @(@(1700, 1900), @(2050, 1880), @(2350, 1760), @(2650, 1560), @(2950, 1300), @(3300, 1080), @(3840, 1080))
    $cp = $curve | ForEach-Object { New-Object System.Drawing.PointF ([float]($_[0] * $k)), ([float]($_[1] * $k)) }
    $cpen = New-Object System.Drawing.Pen (C '#1FB6FF' 90), ([float](7 * $k))
    $g.DrawLines($cpen, [System.Drawing.PointF[]]$cp)
    $dot = New-Object System.Drawing.SolidBrush (C '#BFEFFF' 120)
    foreach ($p in $cp[1..5]) { $d = 22 * $k; $g.FillEllipse($dot, [float]($p.X - $d / 2), [float]($p.Y - $d / 2), [float]$d, [float]$d) }

    # the app icon: the "AH" monogram on a dark rounded tile (same as tools\make-icon.ps1), centred at ($cx, $cy)
    $s = 1100 * $k
    $ox = $cx - $s / 2; $oy = $cy - $s / 2
    $r = $s * 0.22
    $tile = New-Object System.Drawing.Drawing2D.GraphicsPath
    $tile.AddArc([float]$ox, [float]$oy, [float](2 * $r), [float](2 * $r), 180, 90)
    $tile.AddArc([float]($ox + $s - 2 * $r), [float]$oy, [float](2 * $r), [float](2 * $r), 270, 90)
    $tile.AddArc([float]($ox + $s - 2 * $r), [float]($oy + $s - 2 * $r), [float](2 * $r), [float](2 * $r), 0, 90)
    $tile.AddArc([float]$ox, [float]($oy + $s - 2 * $r), [float](2 * $r), [float](2 * $r), 90, 90)
    $tile.CloseFigure()
    # soft outer glow
    foreach ($i in 1..10) {
        $gp = New-Object System.Drawing.Pen (C '#5FE3FF' ([int](14 - $i))), ([float](($i * 12) * $k))
        $gp.LineJoin = 'Round'
        $g.DrawPath($gp, $tile)
    }
    $tb = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.RectangleF ([float]$ox), ([float]$oy), ([float]$s), ([float]$s)), (C '#1C2230'), (C '#0B0D12'), 90
    $g.FillPath($tb, $tile)
    $mono = New-Object System.Drawing.Drawing2D.GraphicsPath
    $mono.FillMode = 'Winding'   # overlapping glyph contours must not cut holes
    $mono.AddString('AH', (New-Object System.Drawing.FontFamily 'Segoe UI'), [int][System.Drawing.FontStyle]::Bold, 100,
        (New-Object System.Drawing.PointF 0, 0), [System.Drawing.StringFormat]::GenericTypographic)
    $mbx = $mono.GetBounds()
    $fit = [Math]::Min($s * 0.70 / $mbx.Width, $s * 0.52 / $mbx.Height)
    $m = New-Object System.Drawing.Drawing2D.Matrix
    $m.Translate([float]($ox + ($s - $mbx.Width * $fit) / 2), [float]($oy + ($s - $mbx.Height * $fit) / 2))
    $m.Scale([float]$fit, [float]$fit)
    $m.Translate([float](-$mbx.X), [float](-$mbx.Y))
    $mono.Transform($m)
    $mb = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.RectangleF ([float]$ox), ([float]($oy + $s * 0.2)), ([float]$s), ([float]($s * 0.6))), (C '#5FE3FF'), (C '#1288D6'), 90
    $g.FillPath($mb, $mono)

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
