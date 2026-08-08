# Generates Assets\AppIcon.ico — a "contribution graph behind glass" mark.
# 4x4 grid of rounded cells ramping through the GitHub contribution greens,
# with a soft diagonal glass highlight over the top.
# Emits a multi-resolution .ico (16/20/24/32/40/48/64/128/256) built from PNG frames.

param(
    [string]$OutPath = (Join-Path $PSScriptRoot '..\src\GitHubGoal\Assets\AppIcon.ico')
)

Add-Type -AssemblyName System.Drawing

# Contribution-graph ramp: empty -> full, matching GitHub's dark-theme scale.
$levels = @(
    [System.Drawing.Color]::FromArgb(255, 22, 27, 34),
    [System.Drawing.Color]::FromArgb(255, 14, 68, 41),
    [System.Drawing.Color]::FromArgb(255, 0, 109, 50),
    [System.Drawing.Color]::FromArgb(255, 38, 166, 65),
    [System.Drawing.Color]::FromArgb(255, 57, 211, 83)
)

# Which ramp level each of the 4x4 cells uses (row-major, top-left -> bottom-right).
# Rises toward the bottom-right so the mark reads as "progress".
$grid = @(
    1, 1, 3, 4,
    1, 2, 3, 4,
    2, 3, 4, 4,
    3, 4, 4, 4
)

function New-IconFrame([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = [double]$size

    # --- rounded-square glass plate -------------------------------------
    $pad = $s * 0.055
    $plate = $s - (2 * $pad)
    $plateRadius = $s * 0.22

    $platePath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $plateRadius * 2
    $platePath.AddArc($pad, $pad, $d, $d, 180, 90)
    $platePath.AddArc($pad + $plate - $d, $pad, $d, $d, 270, 90)
    $platePath.AddArc($pad + $plate - $d, $pad + $plate - $d, $d, $d, 0, 90)
    $platePath.AddArc($pad, $pad + $plate - $d, $d, $d, 90, 90)
    $platePath.CloseFigure()

    # Deep slate base so the greens stay legible on any taskbar colour.
    $baseBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF($pad, $pad)),
        (New-Object System.Drawing.PointF(($pad + $plate), ($pad + $plate))),
        [System.Drawing.Color]::FromArgb(255, 32, 38, 48),
        [System.Drawing.Color]::FromArgb(255, 15, 18, 24))
    $g.FillPath($baseBrush, $platePath)
    $baseBrush.Dispose()

    # --- contribution cells ---------------------------------------------
    $inset = $s * 0.16
    $area = $s - (2 * $inset)
    $gap = $area * 0.10
    $cell = ($area - (3 * $gap)) / 4.0
    $cellRadius = [Math]::Max(0.6, $cell * 0.26)

    for ($r = 0; $r -lt 4; $r++) {
        for ($c = 0; $c -lt 4; $c++) {
            $color = $levels[$grid[($r * 4) + $c]]
            $x = $inset + ($c * ($cell + $gap))
            $y = $inset + ($r * ($cell + $gap))

            $cp = New-Object System.Drawing.Drawing2D.GraphicsPath
            $cd = $cellRadius * 2
            if ($cd -ge $cell) {
                $cp.AddRectangle((New-Object System.Drawing.RectangleF($x, $y, $cell, $cell)))
            } else {
                $cp.AddArc($x, $y, $cd, $cd, 180, 90)
                $cp.AddArc($x + $cell - $cd, $y, $cd, $cd, 270, 90)
                $cp.AddArc($x + $cell - $cd, $y + $cell - $cd, $cd, $cd, 0, 90)
                $cp.AddArc($x, $y + $cell - $cd, $cd, $cd, 90, 90)
                $cp.CloseFigure()
            }

            $b = New-Object System.Drawing.SolidBrush($color)
            $g.FillPath($b, $cp)
            $b.Dispose()
            $cp.Dispose()
        }
    }

    # --- glass highlight -------------------------------------------------
    # A soft light wash across the upper-left, clipped to the plate.
    $oldClip = $g.Clip
    $g.SetClip($platePath)

    $gloss = New-Object System.Drawing.Drawing2D.GraphicsPath
    $gloss.AddPolygon(@(
        (New-Object System.Drawing.PointF(0, 0)),
        (New-Object System.Drawing.PointF([float]$s, 0)),
        (New-Object System.Drawing.PointF(0, [float]$s))
    ))
    $glossBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF(0, 0)),
        (New-Object System.Drawing.PointF([float]($s * 0.85), [float]($s * 0.85))),
        [System.Drawing.Color]::FromArgb(58, 255, 255, 255),
        [System.Drawing.Color]::FromArgb(0, 255, 255, 255))
    $g.FillPath($glossBrush, $gloss)
    $glossBrush.Dispose()
    $gloss.Dispose()
    $g.Clip = $oldClip

    # Hairline rim light so the plate has an edge at small sizes.
    if ($size -ge 24) {
        $penW = [Math]::Max(1.0, $s * 0.012)
        $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(70, 255, 255, 255), $penW)
        $g.DrawPath($pen, $platePath)
        $pen.Dispose()
    }

    $platePath.Dispose()
    $g.Dispose()
    return $bmp
}

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$frames = @()
foreach ($size in $sizes) {
    $bmp = New-IconFrame $size
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $frames += , @{ Size = $size; Bytes = $ms.ToArray() }
    $ms.Dispose()
    $bmp.Dispose()
}

# --- assemble the ICO container ------------------------------------------
# ICONDIR (6 bytes) + ICONDIRENTRY * n (16 bytes each) + PNG payloads.
$outDir = Split-Path -Parent $OutPath
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }

$fs = [System.IO.File]::Create($OutPath)
$bw = New-Object System.IO.BinaryWriter($fs)

$bw.Write([uint16]0)                  # reserved
$bw.Write([uint16]1)                  # type: icon
$bw.Write([uint16]$frames.Count)      # image count

$offset = 6 + (16 * $frames.Count)
foreach ($f in $frames) {
    # 256 is encoded as 0 in the single-byte width/height fields.
    $dim = if ($f.Size -ge 256) { 0 } else { $f.Size }
    $bw.Write([byte]$dim)             # width
    $bw.Write([byte]$dim)             # height
    $bw.Write([byte]0)                # palette count
    $bw.Write([byte]0)                # reserved
    $bw.Write([uint16]1)              # colour planes
    $bw.Write([uint16]32)             # bits per pixel
    $bw.Write([uint32]$f.Bytes.Length)
    $bw.Write([uint32]$offset)
    $offset += $f.Bytes.Length
}
foreach ($f in $frames) { $bw.Write($f.Bytes) }

$bw.Flush()
$bw.Dispose()
$fs.Dispose()

Write-Output "Wrote $OutPath ($((Get-Item $OutPath).Length) bytes, $($frames.Count) frames)"
