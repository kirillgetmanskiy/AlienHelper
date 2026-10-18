# Microsoft Store screenshots: renders the app's windows in every language (AlienHelper.exe --screenshot, nothing is
# written to the hardware) and lays them out on 1920x1080 canvases with a caption.
# Output: store-listing\<lang>\screenshots\01-main.png ... 04-keys.png
param([string]$Exe = "$PSScriptRoot\..\publish-dev\AlienHelper.exe", [string[]]$Langs = @('ru', 'en', 'fr', 'es', 'ja', 'zh'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Resolve-Path "$PSScriptRoot\.."

$captions = @{
    ru = @('Режимы, вентиляторы и мониторинг в одном окне', 'Графики: температуры, частоты, ядра, SSD, сеть', 'Свои кривые вентиляторов для CPU и GPU', 'Подсветка каждой клавиши, шаблоны и анимации')
    en = @('Power modes, fans and monitoring in one window', 'Charts: temperatures, clocks, cores, SSD, network', 'Custom fan curves for CPU and GPU', 'Per-key lighting, templates and animations')
    fr = @('Modes, ventilateurs et surveillance dans une fenêtre', 'Graphiques : températures, fréquences, cœurs, SSD, réseau', 'Courbes de ventilateurs pour CPU et GPU', 'Éclairage par touche, modèles et animations')
    es = @('Modos, ventiladores y monitorización en una ventana', 'Gráficos: temperaturas, frecuencias, núcleos, SSD, red', 'Curvas de ventiladores propias para CPU y GPU', 'Iluminación por tecla, plantillas y animaciones')
    ja = @('電源モード、ファン、モニタリングをひとつのウィンドウで', 'グラフ: 温度、クロック、コア、SSD、ネットワーク', 'CPU と GPU のカスタム ファン カーブ', 'キーごとのライティング、テンプレート、アニメーション')
    zh = @('电源模式、风扇和监控集于一个窗口', '图表：温度、频率、核心、SSD、网络', 'CPU 和 GPU 的自定义风扇曲线', '逐键灯光、模板和动画')
}
$tagline = @{
    ru = 'Alien Helper · для ноутбуков Dell Alienware'; en = 'Alien Helper · for Dell Alienware laptops'
    fr = 'Alien Helper · pour portables Dell Alienware'; es = 'Alien Helper · para portátiles Dell Alienware'
    ja = 'Alien Helper · Dell Alienware ノート PC 向け'; zh = 'Alien Helper · 适用于 Dell Alienware 笔记本'
}
$fonts = @{ ja = 'Yu Gothic UI'; zh = 'Microsoft YaHei UI' }

function New-Canvas([string]$lang, [int]$index) {
    $bmp = New-Object System.Drawing.Bitmap 1920, 1080
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'; $g.TextRenderingHint = 'AntiAliasGridFit'
    $rect = New-Object System.Drawing.Rectangle 0, 0, 1920, 1080
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.ColorTranslator]::FromHtml('#0A0C10')), ([System.Drawing.ColorTranslator]::FromHtml('#17202C')), 35
    $g.FillRectangle($bg, $rect)
    $face = if ($fonts[$lang]) { $fonts[$lang] } else { 'Segoe UI Semibold' }
    $cf = New-Object System.Drawing.Font $face, 30, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $tf = New-Object System.Drawing.Font ($(if ($fonts[$lang]) { $fonts[$lang] } else { 'Segoe UI' })), 18, ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
    $g.DrawString($captions[$lang][$index], $cf, [System.Drawing.Brushes]::White, 60, 34)
    $accent = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#1FB6FF'))
    $g.DrawString($tagline[$lang], $tf, $accent, 62, 82)
    return @{ Bmp = $bmp; G = $g }
}

# draws an image scaled to fit the box (never enlarged), centred, with a soft shadow
function Put($g, [string]$path, [int]$x, [int]$y, [int]$w, [int]$h) {
    $img = [System.Drawing.Image]::FromFile($path)
    $s = [Math]::Min(1.0, [Math]::Min($w / $img.Width, $h / $img.Height))
    $dw = [int]($img.Width * $s); $dh = [int]($img.Height * $s)
    $dx = $x + [int](($w - $dw) / 2); $dy = $y + [int](($h - $dh) / 2)
    for ($i = 1; $i -le 12; $i++) {
        $sh = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(10, 0, 0, 0))
        $g.FillRectangle($sh, $dx - $i, $dy - $i + 6, $dw + 2 * $i, $dh + 2 * $i)
    }
    $g.DrawImage($img, $dx, $dy, $dw, $dh)
    $img.Dispose()
}

foreach ($lang in $Langs) {
    $raw = Join-Path $root "shots\store\$lang"
    $out = Join-Path $root "store-listing\$lang\screenshots"
    New-Item -ItemType Directory -Force $raw, $out | Out-Null
    # main window: monitoring, power and modes; lighting and brightness live in other shots
    $p = Start-Process $Exe -ArgumentList '--screenshot', $raw, $lang, 'theme=dark', 'hide=net,screen,lights' -PassThru -Wait
    if (-not (Test-Path "$raw\main.png")) { throw "render failed for $lang" }

    $c = New-Canvas $lang 0
    Put $c.G "$raw\main.png" 120 140 760 920
    Put $c.G "$raw\overlay0.png" 960 160 420 880
    Put $c.G "$raw\overlay1.png" 1440 160 380 880
    $c.Bmp.Save("$out\01-main.png", [System.Drawing.Imaging.ImageFormat]::Png); $c.G.Dispose(); $c.Bmp.Dispose()

    $c = New-Canvas $lang 1
    Put $c.G "$raw\charts.png" 60 130 1800 920
    $c.Bmp.Save("$out\02-charts.png", [System.Drawing.Imaging.ImageFormat]::Png); $c.G.Dispose(); $c.Bmp.Dispose()

    $c = New-Canvas $lang 2
    Put $c.G "$raw\curves.png" 60 140 1800 900
    $c.Bmp.Save("$out\03-curves.png", [System.Drawing.Imaging.ImageFormat]::Png); $c.G.Dispose(); $c.Bmp.Dispose()

    $c = New-Canvas $lang 3
    Put $c.G "$raw\keys.png" 60 140 1800 900
    $c.Bmp.Save("$out\04-keys.png", [System.Drawing.Imaging.ImageFormat]::Png); $c.G.Dispose(); $c.Bmp.Dispose()
    "$lang done"
}
