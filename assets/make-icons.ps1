# Erzeugt MonSwitch.ico und MonSwitchPaused.ico (mehrere Größen) mit System.Drawing.
# Aufruf: pwsh -File assets/make-icons.ps1
# Kleine Größen werden als 32-Bit-DIB, große als PNG eingebettet.

Add-Type -AssemblyName System.Drawing

function New-RoundedRect([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $p = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-IconBitmap([int]$s, [bool]$paused) {
    $bmp = [System.Drawing.Bitmap]::new($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    if ($paused) {
        $bezel = [System.Drawing.Color]::FromArgb(255, 70, 74, 80)
        $screenTop = [System.Drawing.Color]::FromArgb(255, 150, 154, 160)
        $screenBottom = [System.Drawing.Color]::FromArgb(255, 112, 116, 122)
    } else {
        $bezel = [System.Drawing.Color]::FromArgb(255, 24, 48, 84)
        $screenTop = [System.Drawing.Color]::FromArgb(255, 70, 160, 255)
        $screenBottom = [System.Drawing.Color]::FromArgb(255, 22, 104, 214)
    }

    # Monitorgehäuse
    $mx = $s * 0.04; $my = $s * 0.10; $mw = $s * 0.92; $mh = $s * 0.66
    $radius = [Math]::Max(1.0, $s * 0.08)
    $body = New-RoundedRect $mx $my $mw $mh $radius
    $g.FillPath([System.Drawing.SolidBrush]::new($bezel), $body)

    # Bildschirmfläche
    $inset = [Math]::Max(1.0, $s * 0.07)
    $sx = $mx + $inset; $sy = $my + $inset; $sw = $mw - 2 * $inset; $sh = $mh - 2 * $inset
    $screenRect = [System.Drawing.RectangleF]::new($sx, $sy, $sw, $sh)
    $grad = [System.Drawing.Drawing2D.LinearGradientBrush]::new($screenRect, $screenTop, $screenBottom, 90.0)
    $screen = New-RoundedRect $sx $sy $sw $sh ([Math]::Max(0.5, $radius * 0.5))
    $g.FillPath($grad, $screen)

    # Standfuß
    $neckW = $s * 0.16; $neckH = $s * 0.10
    $g.FillRectangle([System.Drawing.SolidBrush]::new($bezel), ($s - $neckW) / 2, $my + $mh, $neckW, $neckH)
    $baseW = $s * 0.48; $baseH = [Math]::Max(1.0, $s * 0.08)
    $basePath = New-RoundedRect (($s - $baseW) / 2) ($my + $mh + $neckH) $baseW $baseH ([Math]::Max(0.5, $baseH / 2))
    $g.FillPath([System.Drawing.SolidBrush]::new($bezel), $basePath)

    # Umschaltpfeile (oben nach rechts, unten nach links)
    $penW = [Math]::Max(1.2, $s * 0.075)
    $pen = [System.Drawing.Pen]::new([System.Drawing.Color]::White, $penW)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $ax1 = $sx + $sw * 0.20; $ax2 = $sx + $sw * 0.80
    $ay1 = $sy + $sh * 0.32; $ay2 = $sy + $sh * 0.70
    $head = $sh * 0.20
    $g.DrawLine($pen, $ax1, $ay1, $ax2, $ay1)
    $g.DrawLines($pen, [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new($ax2 - $head, $ay1 - $head),
        [System.Drawing.PointF]::new($ax2, $ay1),
        [System.Drawing.PointF]::new($ax2 - $head, $ay1 + $head)))
    $g.DrawLine($pen, $ax2, $ay2, $ax1, $ay2)
    $g.DrawLines($pen, [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new($ax1 + $head, $ay2 - $head),
        [System.Drawing.PointF]::new($ax1, $ay2),
        [System.Drawing.PointF]::new($ax1 + $head, $ay2 + $head)))

    $g.Dispose()
    return $bmp
}

function Get-DibBytes([System.Drawing.Bitmap]$bmp) {
    $w = $bmp.Width; $h = $bmp.Height
    $ms = [System.IO.MemoryStream]::new()
    $bw = [System.IO.BinaryWriter]::new($ms)
    $maskRow = [int]([Math]::Floor(($w + 31) / 32) * 4)
    $bw.Write([int]40); $bw.Write([int]$w); $bw.Write([int]($h * 2))
    $bw.Write([int16]1); $bw.Write([int16]32); $bw.Write([int]0)
    $bw.Write([int]($w * $h * 4 + $maskRow * $h))
    $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0)
    for ($y = $h - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $w; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $bw.Write([byte]$c.B); $bw.Write([byte]$c.G); $bw.Write([byte]$c.R); $bw.Write([byte]$c.A)
        }
    }
    for ($y = $h - 1; $y -ge 0; $y--) {
        $row = [byte[]]::new($maskRow)
        for ($x = 0; $x -lt $w; $x++) {
            if ($bmp.GetPixel($x, $y).A -lt 128) { $row[[int][Math]::Floor($x / 8)] = $row[[int][Math]::Floor($x / 8)] -bor (0x80 -shr ($x % 8)) }
        }
        $bw.Write($row)
    }
    $bw.Flush()
    return $ms.ToArray()
}

function Write-Ico([string]$path, [bool]$paused) {
    $sizes = 16, 20, 24, 32, 40, 48, 64, 256
    $images = foreach ($s in $sizes) {
        $bmp = New-IconBitmap $s $paused
        if ($s -ge 64) {
            $ms = [System.IO.MemoryStream]::new()
            $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
            , $ms.ToArray()
        } else {
            , (Get-DibBytes $bmp)
        }
        $bmp.Dispose()
    }
    $out = [System.IO.MemoryStream]::new()
    $bw = [System.IO.BinaryWriter]::new($out)
    $bw.Write([int16]0); $bw.Write([int16]1); $bw.Write([int16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $s = $sizes[$i]; $data = $images[$i]
        $dim = if ($s -ge 256) { 0 } else { $s }
        $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
        $bw.Write([int16]1); $bw.Write([int16]32)
        $bw.Write([int]$data.Length); $bw.Write([int]$offset)
        $offset += $data.Length
    }
    foreach ($data in $images) { $bw.Write([byte[]]$data) }
    $bw.Flush()
    [System.IO.File]::WriteAllBytes($path, $out.ToArray())
    Write-Host "Geschrieben: $path ($($out.Length) Bytes)"
}

Write-Ico (Join-Path $PSScriptRoot 'MonSwitch.ico') $false
Write-Ico (Join-Path $PSScriptRoot 'MonSwitchPaused.ico') $true

# Vorschau (nur zur Kontrolle, nicht eingecheckt)
if ($args -contains '-Preview') {
    $prev = [System.Drawing.Bitmap]::new(420, 140)
    $g = [System.Drawing.Graphics]::FromImage($prev)
    $g.Clear([System.Drawing.Color]::FromArgb(255, 32, 32, 32))
    $x = 4
    foreach ($s in 16, 24, 32, 48, 64) { $b = New-IconBitmap $s $false; $g.DrawImage($b, $x, 4, $s, $s); $b = New-IconBitmap $s $true; $g.DrawImage($b, $x, 72, $s, $s); $x += $s + 10 }
    $big = New-IconBitmap 128 $false; $g.DrawImage($big, 280, 4, 128, 128)
    $g.Dispose()
    $prev.Save((Join-Path $args[($args.IndexOf('-Preview') + 1)] 'icon-preview.png'), [System.Drawing.Imaging.ImageFormat]::Png)
}
