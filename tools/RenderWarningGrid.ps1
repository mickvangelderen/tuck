<#
.SYNOPSIS
Render the six warning sprite sheets as a labeled, looping two-frame WebP.
.DESCRIPTION
Requires Windows (System.Drawing and Segoe UI) and FFmpeg with libwebp_anim.
Edit $warnings and $style below to change labels, order, spacing, and colors.
PNG previews are retained in artifacts/warning-grid. The canvas is transparent.
.EXAMPLE
powershell -NoProfile -ExecutionPolicy Bypass -File tools/RenderWarningGrid.ps1
.EXAMPLE
.\tools\RenderWarningGrid.ps1 -FrameMilliseconds 1400 -FontSize 20 -IconScale 5
#>
[CmdletBinding()]
param(
    [string]$OutputPath,
    [ValidateRange(1, 8192)][int]$Width = 1170,
    [ValidateRange(1, 8192)][int]$Height = 330,
    [ValidateRange(20, 60000)][int]$FrameMilliseconds = 1000,
    [ValidateRange(1, 8)][int]$IconScale = 4,
    [ValidateRange(8, 72)][int]$FontSize = 22,
    [string]$FontFamily = 'Segoe UI Semibold',
    [string]$FfmpegPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputPath) { $OutputPath = Join-Path $repoRoot 'media/warning-grid.webp' }
$warnings = @(
    @{ File = 'warning-stolen.png'; Label = 'Stolen' },
    @{ File = 'warning-empty-battery.png'; Label = 'Low battery' },
    @{ File = 'warning-hydrate.png'; Label = 'Out of water' },
    @{ File = 'warning-missing-ingredients.png'; Label = 'Missing input' },
    @{ File = 'warning-spill.png'; Label = 'Overflow risk' },
    @{ File = 'warning-filter.png'; Label = 'Depleted filter' }
)
$style = @{
    CellWidth = 352; CellHeight = 116
    ColumnGap = 24; RowGap = 20; Padding = 24; IconTextGap = 20; Radius = 14
    Panel = '#252331'; Border = '#494452'; Text = '#F3EFE3'
}

if ([IO.Path]::GetExtension($OutputPath) -ine '.webp') { throw 'OutputPath must end in .webp.' }
$outputFile = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)
if (-not $FfmpegPath) {
    $ffmpegCommand = Get-Command ffmpeg -CommandType Application -ErrorAction SilentlyContinue
    if ($ffmpegCommand) { $FfmpegPath = $ffmpegCommand.Source }
    else {
        $FfmpegPath = @("$env:ProgramFiles\kdenlive\bin\ffmpeg.exe", "$env:LOCALAPPDATA\Programs\kdenlive\bin\ffmpeg.exe") |
            Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    }
}
if (-not $FfmpegPath -or -not (Test-Path -LiteralPath $FfmpegPath -PathType Leaf)) {
    throw 'Install FFmpeg/Kdenlive or pass -FfmpegPath.'
}
$gridWidth = 3 * $style.CellWidth + 2 * $style.ColumnGap
$gridHeight = 2 * $style.CellHeight + $style.RowGap
if ($gridWidth -gt $Width -or $gridHeight -gt $Height) { throw 'Grid exceeds canvas; reduce cell sizes/gaps or increase Width/Height.' }
$left = [Math]::Floor(($Width - $gridWidth) / 2)
$top = [Math]::Floor(($Height - $gridHeight) / 2)
$iconSize = 16 * $IconScale
$textWidth = $style.CellWidth - 2 * $style.Padding - $iconSize - $style.IconTextGap
if ($textWidth -le 0 -or $iconSize -gt $style.CellHeight - 16) { throw 'Icons do not fit the cells.' }
$workDir = Join-Path $repoRoot 'artifacts/warning-grid'
New-Item -ItemType Directory -Path $workDir -Force | Out-Null
$resources = [Collections.Generic.List[IDisposable]]::new()
try {
    $sheets = foreach ($warning in $warnings) {
        $sheet = [Drawing.Bitmap]::FromFile((Join-Path $repoRoot ('assets/warnings/' + $warning.File)))
        $resources.Add($sheet)
        if ($sheet.Width -ne 32 -or $sheet.Height -ne 16) { throw "Expected two 16x16 frames: $($warning.File)" }
        $sheet
    }
    $font = [Drawing.Font]::new($FontFamily, $FontSize, [Drawing.FontStyle]::Regular, [Drawing.GraphicsUnit]::Pixel)
    $resources.Add($font)
    if ($font.Name -ne $FontFamily) { throw "Font '$FontFamily' is not installed." }
    $panelBrush = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($style.Panel))
    $textBrush = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($style.Text))
    $borderPen = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml($style.Border), 1)
    $format = [Drawing.StringFormat]::new()
    foreach ($resource in @($panelBrush, $textBrush, $borderPen, $format)) { $resources.Add($resource) }
    $format.LineAlignment = [Drawing.StringAlignment]::Center
    $format.FormatFlags = [Drawing.StringFormatFlags]::NoWrap

    # Supersample panels/text, then draw icons at integer scale to preserve their pixel edges.
    $oversample = 3
    $lettering = [Drawing.Bitmap]::new($Width * $oversample, $Height * $oversample)
    $resources.Add($lettering)
    $graphics = [Drawing.Graphics]::FromImage($lettering)
    $resources.Add($graphics)
    $graphics.Clear([Drawing.Color]::Transparent)
    $graphics.ScaleTransform($oversample, $oversample)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    for ($index = 0; $index -lt 6; $index++) {
        $x = $left + ($index % 3) * ($style.CellWidth + $style.ColumnGap)
        $y = $top + [Math]::Floor($index / 3) * ($style.CellHeight + $style.RowGap)
        $labelSize = $graphics.MeasureString($warnings[$index].Label, $font)
        if ($labelSize.Width -gt $textWidth -or $labelSize.Height -gt $style.CellHeight - 16) {
            throw "Label '$($warnings[$index].Label)' does not fit; reduce FontSize or widen cells."
        }
        $path = [Drawing.Drawing2D.GraphicsPath]::new()
        try {
            $diameter = 2 * $style.Radius
            $right = $x + $style.CellWidth - $diameter
            $bottom = $y + $style.CellHeight - $diameter
            $path.AddArc($x, $y, $diameter, $diameter, 180, 90)
            $path.AddArc($right, $y, $diameter, $diameter, 270, 90)
            $path.AddArc($right, $bottom, $diameter, $diameter, 0, 90)
            $path.AddArc($x, $bottom, $diameter, $diameter, 90, 90)
            $path.CloseFigure()
            $graphics.FillPath($panelBrush, $path)
            $graphics.DrawPath($borderPen, $path)
        }
        finally { $path.Dispose() }
        $textRect = [Drawing.RectangleF]::new($x + $style.Padding + $iconSize + $style.IconTextGap, $y, $textWidth, $style.CellHeight)
        $graphics.DrawString($warnings[$index].Label, $font, $textBrush, $textRect, $format)
    }
    $graphics.Flush()
    for ($frameIndex = 0; $frameIndex -lt 2; $frameIndex++) {
        $frame = [Drawing.Bitmap]::new($Width, $Height)
        $frameGraphics = [Drawing.Graphics]::FromImage($frame)
        try {
            $frameGraphics.Clear([Drawing.Color]::Transparent)
            $frameGraphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $frameGraphics.DrawImage($lettering, [Drawing.Rectangle]::new(0, 0, $Width, $Height))
            $frameGraphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
            $frameGraphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::Half
            for ($index = 0; $index -lt 6; $index++) {
                $x = $left + ($index % 3) * ($style.CellWidth + $style.ColumnGap) + $style.Padding
                $y = $top + [Math]::Floor($index / 3) * ($style.CellHeight + $style.RowGap) + [Math]::Floor(($style.CellHeight - $iconSize) / 2)
                $destination = [Drawing.Rectangle]::new($x, $y, $iconSize, $iconSize)
                $frameGraphics.DrawImage($sheets[$index], $destination, $frameIndex * 16, 0, 16, 16, [Drawing.GraphicsUnit]::Pixel)
            }
            $frame.Save((Join-Path $workDir ("frame-{0:D2}.png" -f $frameIndex)), [Drawing.Imaging.ImageFormat]::Png)
        }
        finally { $frameGraphics.Dispose(); $frame.Dispose() }
    }
}
finally { for ($index = $resources.Count - 1; $index -ge 0; $index--) { $resources[$index].Dispose() } }

$stagedWebp = Join-Path $workDir 'warning-grid.webp'
& $FfmpegPath -hide_banner -loglevel error -nostdin -y -framerate "1000/$FrameMilliseconds" -i (Join-Path $workDir 'frame-%02d.png') -frames:v 2 -c:v libwebp_anim -lossless 1 -compression_level 6 -loop 0 $stagedWebp
if ($LASTEXITCODE -ne 0) { throw "FFmpeg failed (exit $LASTEXITCODE)." }

# Check the animation itself before replacing the previous render.
$bytes = [IO.File]::ReadAllBytes($stagedWebp)
$durations = @()
$loop = -1
$hasAlpha = $false
$canvasWidth = $canvasHeight = 0
for ($offset = 12; $offset + 8 -le $bytes.Length;) {
    $chunk = [Text.Encoding]::ASCII.GetString($bytes, $offset, 4)
    $size = [BitConverter]::ToUInt32($bytes, $offset + 4)
    $data = $offset + 8
    if ($data + $size -gt $bytes.Length) { throw 'Truncated WebP output.' }
    if ($chunk -eq 'VP8X') {
        $hasAlpha = ($bytes[$data] -band 16) -ne 0
        $canvasWidth = 1 + $bytes[$data+4] + 256 * $bytes[$data+5] + 65536 * $bytes[$data+6]
        $canvasHeight = 1 + $bytes[$data+7] + 256 * $bytes[$data+8] + 65536 * $bytes[$data+9]
    }
    if ($chunk -eq 'ANIM') { $loop = [BitConverter]::ToUInt16($bytes, $data + 4) }
    if ($chunk -eq 'ANMF') { $durations += $bytes[$data+12] + 256 * $bytes[$data+13] + 65536 * $bytes[$data+14] }
    $offset += 8 + $size + ($size % 2)
}
if ($canvasWidth -ne $Width -or $canvasHeight -ne $Height -or -not $hasAlpha -or $loop -ne 0 -or $durations.Count -ne 2 -or
    @($durations | Where-Object { $_ -ne $FrameMilliseconds }).Count -ne 0) { throw 'WebP dimensions, transparency, frame count, timing, or looping are incorrect.' }
New-Item -ItemType Directory -Path (Split-Path -Parent $outputFile) -Force | Out-Null
Copy-Item -LiteralPath $stagedWebp -Destination $outputFile -Force
Write-Host ("Created {0} ({1}x{2}, two {3} ms frames, {4:N0} bytes)." -f $outputFile, $Width, $Height, $FrameMilliseconds, $bytes.Length)
Write-Host "PNG previews: $workDir"
