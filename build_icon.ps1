Add-Type -AssemblyName System.Drawing

# Renders the Pickets icon -- a soft sage picket fence -- at multiple
# sizes and packs them into a single multi-res app.ico.

$sizes = @(256, 64, 48, 32, 16)
$outIco = Join-Path $PSScriptRoot 'app.ico'

# Sage palette to match the in-app "sage" color scheme.
$picketFill = [System.Drawing.Color]::FromArgb(255, 0xD8, 0xE4, 0xD8) # soft cream-sage picket
$picketEdge = [System.Drawing.Color]::FromArgb(255, 0x6B, 0x82, 0x70) # darker sage outline
$railFill   = [System.Drawing.Color]::FromArgb(255, 0xA8, 0xBC, 0xA8) # mid-sage rail

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    # 4 pickets gives clear silhouette even at 16px (5 turns to mush).
    $picketCount = 4

    # Vertical layout.
    $padTop    = [int]($size * 0.10)
    $padBottom = [int]($size * 0.06)
    $pointH    = [int]($size * 0.10)        # triangular cap height
    $picketTop = $padTop
    $picketBot = $size - $padBottom

    # Horizontal layout: pickets fill the inner band with even gaps between them.
    $padSides  = [int]($size * 0.08)
    $usable    = $size - 2 * $padSides
    $picketW   = [int]($usable * 0.14)      # wide enough to read at 16px
    $totalW    = $picketCount * $picketW
    $gap       = [int](($usable - $totalW) / ($picketCount - 1))

    # Two horizontal rails behind the pickets -- visible through the gaps.
    $railH      = [Math]::Max(2, [int]($size * 0.07))
    $railTopY   = $picketTop + [int](($picketBot - $picketTop) * 0.30)
    $railBotY   = $picketTop + [int](($picketBot - $picketTop) * 0.72)

    $railBrush = New-Object System.Drawing.SolidBrush($railFill)
    $railEdgePen = New-Object System.Drawing.Pen($picketEdge, [Math]::Max(1, [int]($size * 0.012)))

    foreach ($railY in @($railTopY, $railBotY)) {
        $rect = New-Object System.Drawing.Rectangle($padSides, $railY, $usable, $railH)
        $g.FillRectangle($railBrush, $rect)
        if ($size -ge 32) { $g.DrawRectangle($railEdgePen, $rect) }
    }

    # Pickets in front -- pentagon path (pointed top).
    $picketBrush = New-Object System.Drawing.SolidBrush($picketFill)
    $picketPen   = New-Object System.Drawing.Pen($picketEdge, [Math]::Max(1, [int]($size * 0.018)))

    for ($i = 0; $i -lt $picketCount; $i++) {
        $x = $padSides + $i * ($picketW + $gap)
        $points = @(
            (New-Object System.Drawing.Point ($x), ($picketTop + $pointH)),
            (New-Object System.Drawing.Point ([int]($x + $picketW / 2)), ($picketTop)),
            (New-Object System.Drawing.Point (($x + $picketW)), ($picketTop + $pointH)),
            (New-Object System.Drawing.Point (($x + $picketW)), ($picketBot)),
            (New-Object System.Drawing.Point ($x), ($picketBot))
        )
        $g.FillPolygon($picketBrush, $points)
        if ($size -ge 24) { $g.DrawPolygon($picketPen, $points) }
    }

    $railBrush.Dispose(); $railEdgePen.Dispose()
    $picketBrush.Dispose(); $picketPen.Dispose()
    $g.Dispose()
    return $bmp
}

# Render every size to in-memory PNG bytes.
$pngBlobs = @{}
foreach ($s in $sizes) {
    $bmp = New-IconBitmap $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngBlobs[$s] = $ms.ToArray()
    $ms.Dispose()
    $bmp.Dispose()
}

# Pack into ICO format -- modern Windows accepts PNG-in-ICO for all sizes.
$fs = [System.IO.File]::Open($outIco, [System.IO.FileMode]::Create)
$bw = New-Object System.IO.BinaryWriter($fs)

$bw.Write([uint16]0)                     # reserved
$bw.Write([uint16]1)                     # type = icon
$bw.Write([uint16]$sizes.Count)          # image count

$dataOffset = 6 + 16 * $sizes.Count
foreach ($s in $sizes) {
    $blob = $pngBlobs[$s]
    $w = if ($s -ge 256) { 0 } else { [byte]$s }
    $bw.Write([byte]$w); $bw.Write([byte]$w)
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$blob.Length)
    $bw.Write([uint32]$dataOffset)
    $dataOffset += $blob.Length
}

foreach ($s in $sizes) { $bw.Write($pngBlobs[$s]) }
$bw.Dispose()
$fs.Dispose()

Write-Host "Wrote $outIco"
