# Generates src\AlienHelper\Assets\app.ico: an original alien-head glyph on a dark rounded tile (not the Alienware trademark).
# Each size is drawn from vectors, so small sizes stay crisp. Usage: powershell -File tools\make-icon.ps1
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'src\AlienHelper\Assets\app.ico'
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null

function P([double]$x, [double]$y, [double]$s) { New-Object System.Drawing.PointF ([float]($x * $s)), ([float]($y * $s)) }

function Draw-Icon([int]$s, [switch]$Square) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)

    # tile: rounded for the app icon; full square for Store tiles (the Store rounds them itself)
    $tile = New-Object System.Drawing.Drawing2D.GraphicsPath
    if ($Square) { $tile.AddRectangle((New-Object System.Drawing.RectangleF 0, 0, $s, $s)) }
    else {
        $r = [Math]::Max(2, $s * 0.22); $d = $r * 2
        $tile.AddArc(0, 0, $d, $d, 180, 90); $tile.AddArc($s - $d - 1, 0, $d, $d, 270, 90)
        $tile.AddArc($s - $d - 1, $s - $d - 1, $d, $d, 0, 90); $tile.AddArc(0, $s - $d - 1, $d, $d, 90, 90)
    }
    $tile.CloseFigure()
    $tb = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.RectangleF 0, 0, $s, $s), ([System.Drawing.ColorTranslator]::FromHtml('#1C2230')), ([System.Drawing.ColorTranslator]::FromHtml('#0B0D12')), 90
    $g.FillPath($tb, $tile)

    # head: wide cranium tapering to a narrow chin
    $k = 1.0
    $head = New-Object System.Drawing.Drawing2D.GraphicsPath
    $head.AddBezier((P 0.50 0.90 $s), (P 0.31 0.84 $s), (P 0.16 0.62 $s), (P 0.17 0.42 $s))
    $head.AddBezier((P 0.17 0.42 $s), (P 0.18 0.20 $s), (P 0.33 0.10 $s), (P 0.50 0.10 $s))
    $head.AddBezier((P 0.50 0.10 $s), (P 0.67 0.10 $s), (P 0.82 0.20 $s), (P 0.83 0.42 $s))
    $head.AddBezier((P 0.83 0.42 $s), (P 0.84 0.62 $s), (P 0.69 0.84 $s), (P 0.50 0.90 $s))
    $head.CloseFigure()
    $hb = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.RectangleF 0, ($s * 0.1), $s, ($s * 0.8)), ([System.Drawing.ColorTranslator]::FromHtml('#5FE3FF')), ([System.Drawing.ColorTranslator]::FromHtml('#1288D6')), 90
    $g.FillPath($hb, $head)

    # eyes: slanted almonds
    $eye = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#06080C'))
    foreach ($side in -1, 1) {
        $state = $g.Save()
        $g.TranslateTransform([float]($s * (0.50 + 0.155 * $side)), [float]($s * 0.52))
        $g.RotateTransform([float](-28 * $side))
        $w = $s * 0.25; $h = $s * 0.115
        $g.FillEllipse($eye, [float](-$w / 2), [float](-$h / 2), [float]$w, [float]$h)
        $g.Restore($state)
    }
    $g.Dispose()
    return $bmp
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = foreach ($s in $sizes) {
    $b = Draw-Icon $s
    $ms = New-Object System.IO.MemoryStream
    $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $b.Dispose()
    , $ms.ToArray()
}

# ICO container with PNG-compressed entries
$fs = [System.IO.File]::Create($out)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $pngs[$i].Length
    $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s }))); $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $w.Write([byte]0); $w.Write([byte]0); $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$len); $w.Write([uint32]$offset)
    $offset += $len
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Close()

# preview for README / review
$prev = Draw-Icon 256
$prev.Save((Join-Path $root 'src\AlienHelper\Assets\icon-preview.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$prev.Dispose()

# MSIX logos (installer\msix\Package.appxmanifest)
$msix = Join-Path $root 'src\AlienHelper\Assets\Msix'
New-Item -ItemType Directory -Force $msix | Out-Null
foreach ($l in @(@{ n = 'Square44x44Logo'; s = 44 }, @{ n = 'Square150x150Logo'; s = 150 }, @{ n = 'StoreLogo'; s = 50 })) {
    $b = Draw-Icon $l.s
    $b.Save((Join-Path $msix "$($l.n).png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $b.Dispose()
}
# Microsoft Store listing: 1:1 app tile icons (Partner Center → Store listing → Store logos)
$store = Join-Path $root 'store-listing\icons'
New-Item -ItemType Directory -Force $store | Out-Null
foreach ($n in 300, 150, 71) {
    $b = Draw-Icon $n -Square
    $b.Save((Join-Path $store "AppTile-$($n)x$($n).png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $b.Dispose()
}
"wrote $out"
