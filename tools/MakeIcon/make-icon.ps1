# Génère l'icône de l'application (PNG 16→1024 + app.ico) dans src/OpenDMXBridge/Assets.
# Style macOS : carré arrondi (rayon ≈ 22,5 %), dégradé bleu → violet, reflet de verre, trois faders blancs.
# Usage : pwsh tools/MakeIcon/make-icon.ps1

Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$out = Join-Path $root "src\OpenDMXBridge\Assets"
New-Item -ItemType Directory -Force $out | Out-Null

function RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function DrawIcon([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = [float]$size
    $pad = $s * 0.04                     # petite marge transparente (macOS laisse respirer l'icône)
    $w = $s - 2 * $pad
    $radius = $w * 0.225

    # Ombre portée douce
    if ($size -ge 32) {
        $shadow = RoundedPath ($pad) ($pad + $s * 0.02) $w $w $radius
        $sb = New-Object System.Drawing.Drawing2D.PathGradientBrush $shadow
        $sb.CenterColor = [System.Drawing.Color]::FromArgb(70, 20, 30, 80)
        $sb.SurroundColors = @([System.Drawing.Color]::FromArgb(0, 20, 30, 80))
        $g.FillPath($sb, $shadow)
    }

    # Corps : dégradé bleu ciel → bleu → violet
    $body = RoundedPath $pad $pad $w $w $radius
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush (
        [System.Drawing.PointF]::new($pad, $pad), [System.Drawing.PointF]::new($pad + $w, $pad + $w),
        [System.Drawing.Color]::FromArgb(255, 0x5A, 0xC8, 0xFA), [System.Drawing.Color]::FromArgb(255, 0x5E, 0x5C, 0xE6))
    $blend = New-Object System.Drawing.Drawing2D.ColorBlend 3
    $blend.Colors = @([System.Drawing.Color]::FromArgb(255, 0x64, 0xD2, 0xFF), [System.Drawing.Color]::FromArgb(255, 0x0A, 0x84, 0xFF), [System.Drawing.Color]::FromArgb(255, 0x5E, 0x5C, 0xE6))
    $blend.Positions = @([float]0, [float]0.55, [float]1)
    $grad.InterpolationColors = $blend
    $g.FillPath($grad, $body)

    # Reflet de verre : moitié haute légèrement éclaircie
    $g.SetClip($body)
    $hl = New-Object System.Drawing.Drawing2D.LinearGradientBrush (
        [System.Drawing.PointF]::new(0, $pad), [System.Drawing.PointF]::new(0, $pad + $w * 0.55),
        [System.Drawing.Color]::FromArgb(110, 255, 255, 255), [System.Drawing.Color]::FromArgb(0, 255, 255, 255))
    $g.FillRectangle($hl, $pad, $pad, $w, $w * 0.55)
    $g.ResetClip()

    # Bord lumineux fin
    if ($size -ge 32) {
        $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(120, 255, 255, 255)), ([Math]::Max(1, $s / 128))
        $g.DrawPath($pen, (RoundedPath ($pad + 0.5) ($pad + 0.5) ($w - 1) ($w - 1) $radius))
    }

    # Trois faders : rails discrets + curseurs blancs à hauteurs différentes (niveaux DMX)
    $cx = $pad + $w / 2
    $gap = $w * 0.19
    $railW = [Math]::Max(1.0, $w * 0.055)
    $railTop = $pad + $w * 0.26
    $railH = $w * 0.48
    $knobW = $w * 0.15
    $knobH = $w * 0.075
    $levels = @(0.72, 0.35, 0.55)   # position du curseur (0 = bas, 1 = haut)
    $rail = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(90, 255, 255, 255))
    $knob = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $fill = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(230, 255, 255, 255))
    for ($i = 0; $i -lt 3; $i++) {
        $x = $cx + ($i - 1) * $gap
        # rail
        $g.FillPath($rail, (RoundedPath ($x - $railW / 2) $railTop $railW $railH ($railW / 2)))
        # partie « allumée » sous le curseur
        $ky = $railTop + $railH * (1 - $levels[$i])
        $g.FillPath($fill, (RoundedPath ($x - $railW / 2) $ky $railW ($railTop + $railH - $ky) ($railW / 2)))
        # curseur avec ombre légère
        if ($size -ge 32) {
            $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(60, 0, 20, 60))),
                (RoundedPath ($x - $knobW / 2) ($ky - $knobH / 2 + $s * 0.008) $knobW $knobH ($knobH / 2)))
        }
        $g.FillPath($knob, (RoundedPath ($x - $knobW / 2) ($ky - $knobH / 2) $knobW $knobH ($knobH / 2)))
    }

    $g.Dispose()
    return $bmp
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256, 512, 1024
$pngs = @{}
foreach ($sz in $sizes) {
    $bmp = DrawIcon $sz
    $file = Join-Path $out "app-$sz.png"
    $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs[$sz] = [System.IO.File]::ReadAllBytes($file)
    $bmp.Dispose()
}
Copy-Item (Join-Path $out "app-1024.png") (Join-Path $out "app.png") -Force

# ICO multi-tailles avec entrées PNG (supporté depuis Vista)
$icoSizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $ms
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$icoSizes.Count)
$offset = 6 + 16 * $icoSizes.Count
foreach ($sz in $icoSizes) {
    $data = $pngs[$sz]
    $dim = if ($sz -ge 256) { 0 } else { $sz }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$data.Length); $bw.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($sz in $icoSizes) { $bw.Write($pngs[$sz]) }
$bw.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $out "app.ico"), $ms.ToArray())

# On ne garde que les PNG utiles au dépôt
foreach ($sz in $sizes) { if ($sz -notin 256, 1024) { Remove-Item (Join-Path $out "app-$sz.png") -Force } }
Get-ChildItem $out | Select-Object Name, Length
