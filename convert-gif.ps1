<#
.SYNOPSIS
Convert a video to a high-quality looping GIF with FFmpeg and Gifski.
.DESCRIPTION
Defaults to 10 fps, a maximum width of 640 pixels, and quality 90. Keeps the
original playback speed. Reads any video format supported by FFmpeg directly.
Optionally trims and crops the source and writes a matching H.264 MP4.
Finds FFmpeg on PATH or in Kdenlive's Windows installation; ffprobe must be
alongside FFmpeg or on PATH. Finds Gifski on PATH or downloads it to the ignored
dependencies folder on first use. Temporary PNG frames are cleaned up.
Encoder: https://gif.ski/
.PARAMETER StartSeconds
Start position in decimal seconds, not a seconds-and-frames timecode.
.PARAMETER DurationSeconds
Length of the selection in seconds. Zero (the default) means the rest of the video.
.PARAMETER Crop
Optional width:height:x:y rectangle in original video pixels, before resizing.
.PARAMETER Width
Maximum GIF width, without upscaling. Zero preserves the cropped source width.
.PARAMETER Mp4Path
Optional matching silent MP4. Uses the same trim and crop at the source frame
rate and native crop size. Odd dimensions receive one pixel of edge padding
for H.264 compatibility. Width and Fps affect only the GIF.
.EXAMPLE
.\convert-gif.ps1 .\recording.mp4
.EXAMPLE
.\convert-gif.ps1 .\recording.mp4 .\demo.gif -Fps 5 -Width 480 -Quality 85
.EXAMPLE
.\convert-gif.ps1 .\recording.mp4 .\demo.gif -StartSeconds 2.5 -DurationSeconds 4 -Crop '800:400:100:200' -Width 0 -Mp4Path .\demo.mp4 -Force
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)]
    [string]$InputPath,
    [Parameter(Position = 1)]
    [string]$OutputPath,
    [ValidateRange(1, 50)]
    [int]$Fps = 10,
    [ValidateRange(0, 8192)]
    [int]$Width = 640,
    [ValidateRange(1, 100)]
    [int]$Quality = 90,
    [ValidateRange(0, 2147483647)]
    [double]$StartSeconds = 0,
    [ValidateRange(0, 2147483647)]
    [double]$DurationSeconds = 0,
    [ValidatePattern('^[1-9][0-9]*:[1-9][0-9]*:[0-9]+:[0-9]+$')]
    [string]$Crop,
    [string]$Mp4Path,
    [string]$FfmpegPath,
    [string]$GifskiPath,
    [switch]$Force
)

# Provenance (2026-11-09): Probably Stolen UI demos looked good at 10 fps and
# Gifski quality 90. On one 640x360 clip, Gifski produced 2.84 MB versus 6.59 MB
# with FFmpeg's optimized palette and no dithering, with better sampled frames.
# Native-size cropping (-Width 0) kept UI text sharp; a 4.2s, 720x328 crop was
# 309 KB. These are footage-specific observations, not universal size guarantees.
$ErrorActionPreference = 'Stop'
$inputFile = (Get-Item -LiteralPath $InputPath).FullName
if (-not (Test-Path -LiteralPath $inputFile -PathType Leaf)) {
    throw "Input must be a video file: $InputPath"
}
if (-not $OutputPath) { $OutputPath = [IO.Path]::ChangeExtension($inputFile, '.gif') }
$outputFile = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)
$outputs = @(@{ Path = $outputFile; Extension = '.gif'; StagedName = 'output.gif' })
if ($Mp4Path) {
    $mp4File = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Mp4Path)
    $outputs += @{ Path = $mp4File; Extension = '.mp4'; StagedName = 'output.mp4' }
}
foreach ($output in $outputs) {
    if ([IO.Path]::GetExtension($output.Path) -ine $output.Extension) {
        throw "Output must have a $($output.Extension) extension: $($output.Path)"
    }
    if ($inputFile -ieq $output.Path) { throw 'Input and output must be different files.' }
    if (Test-Path -LiteralPath $output.Path -PathType Container) { throw "Output is a directory: $($output.Path)" }
    if ((Test-Path -LiteralPath $output.Path) -and -not $Force) {
        throw "Output already exists: $($output.Path). Use -Force to replace it."
    }
}

if (-not $FfmpegPath) {
    $ffmpegCommand = Get-Command ffmpeg -CommandType Application -ErrorAction SilentlyContinue
    if ($ffmpegCommand) { $FfmpegPath = $ffmpegCommand.Source }
    else {
        $candidates = @(
            "$env:ProgramFiles\kdenlive\bin\ffmpeg.exe",
            "$env:LOCALAPPDATA\Programs\kdenlive\bin\ffmpeg.exe"
        )
        $FfmpegPath = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    }
}
if (-not $FfmpegPath -or -not (Test-Path -LiteralPath $FfmpegPath -PathType Leaf)) {
    throw 'FFmpeg was not found. Install FFmpeg/Kdenlive or pass -FfmpegPath with the path to ffmpeg.exe.'
}
$FfmpegPath = (Get-Item -LiteralPath $FfmpegPath).FullName
$ffprobePath = Join-Path (Split-Path -Parent $FfmpegPath) 'ffprobe.exe'
if (-not (Test-Path -LiteralPath $ffprobePath -PathType Leaf)) {
    $probeCommand = Get-Command ffprobe -CommandType Application -ErrorAction SilentlyContinue
    if (-not $probeCommand) { throw 'ffprobe was not found alongside FFmpeg or on PATH.' }
    $ffprobePath = $probeCommand.Source
}
$probeJson = & $ffprobePath -v error -select_streams v:0 -show_entries stream=width,height -of json $inputFile
if ($LASTEXITCODE -ne 0) { throw "ffprobe failed (exit $LASTEXITCODE)." }
$video = ($probeJson | ConvertFrom-Json).streams | Select-Object -First 1
if (-not $video) { throw 'Input has no video stream.' }

$sourceWidth = [int]$video.width
$sourceHeight = [int]$video.height
$cropFilter = @()
if ($Crop) {
    $rectangle = @($Crop.Split(':') | ForEach-Object { [int]$_ })
    if ([long]$rectangle[0] + $rectangle[2] -gt $sourceWidth -or
        [long]$rectangle[1] + $rectangle[3] -gt $sourceHeight) {
        throw "Crop $Crop exceeds the source dimensions ${sourceWidth}x${sourceHeight}."
    }
    $sourceWidth = $rectangle[0]
    $cropFilter = @("crop=${Crop}:exact=1")
}
$gifWidth = $sourceWidth
if ($Width -gt 0) { $gifWidth = [Math]::Min($Width, $sourceWidth) }

# Seek while decoding, then limit the output duration. Format decimal arguments
# independently of the current locale (e.g. a comma decimal separator).
$inputArgs = @('-hide_banner', '-loglevel', 'error', '-nostdin')
if ($StartSeconds -gt 0) { $inputArgs += @('-ss', $StartSeconds.ToString('R', [Globalization.CultureInfo]::InvariantCulture)) }
$inputArgs += @('-i', $inputFile)
if ($DurationSeconds -gt 0) { $inputArgs += @('-t', $DurationSeconds.ToString('R', [Globalization.CultureInfo]::InvariantCulture)) }
$inputArgs += @('-map', '0:v:0', '-an')

$gifskiDir = Join-Path $PSScriptRoot 'dependencies/gifski'
if ($GifskiPath -and -not (Test-Path -LiteralPath $GifskiPath -PathType Leaf)) {
    throw "Gifski was not found: $GifskiPath"
}
if (-not $GifskiPath) {
    $gifskiCommand = Get-Command gifski -CommandType Application -ErrorAction SilentlyContinue
    if ($gifskiCommand) { $GifskiPath = $gifskiCommand.Source }
    else { $GifskiPath = Join-Path $gifskiDir 'win/gifski.exe' }
}
if (-not (Test-Path -LiteralPath $GifskiPath -PathType Leaf)) {
    New-Item -ItemType Directory -Path $gifskiDir -Force | Out-Null
    $archivePath = Join-Path $gifskiDir 'gifski-1.32.0.zip'
    Write-Host 'Downloading Gifski 1.32.0...'
    $ProgressPreference = 'SilentlyContinue'
    Invoke-WebRequest -UseBasicParsing -Uri 'https://gif.ski/gifski-1.32.0.zip' -OutFile $archivePath -TimeoutSec 120
    $expectedHash = '1BE600C94FE3FF4A8E63757863E403A3DFDBEF968BC4047865F1173D72D67EA3'
    if ((Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash -ne $expectedHash) {
        throw 'The Gifski download has an unexpected checksum; refusing to extract it.'
    }
    Expand-Archive -LiteralPath $archivePath -DestinationPath $gifskiDir -Force
}

$workRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'artifacts'))
$workDir = Join-Path $workRoot ('gif-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $workDir -Force | Out-Null
try {
    Write-Host "Extracting frames at $Fps fps (width $gifWidth)..."
    # Reduce the frame rate before extraction; encoding all source frames at a
    # lower GIF frame rate would slow down playback instead of dropping frames.
    $filters = $cropFilter + @("fps=$Fps")
    if ($gifWidth -lt $sourceWidth) { $filters += "scale=w=${gifWidth}:h=-1:flags=lanczos" }
    & $FfmpegPath @inputArgs -vf ($filters -join ',') (Join-Path $workDir 'frame-%06d.png')
    if ($LASTEXITCODE -ne 0) { throw "FFmpeg failed (exit $LASTEXITCODE)." }
    if (-not (Test-Path -LiteralPath (Join-Path $workDir 'frame-000001.png'))) {
        throw 'The selection produced no frames. Check the trim times or increase Fps for very short clips.'
    }

    Write-Host "Encoding with Gifski (quality $Quality)..."
    $stagedGif = Join-Path $workDir 'output.gif'
    # Gifski expands this wildcard itself, avoiding Windows command-line length
    # limits on long clips. PNG frames also avoid additional chroma subsampling.
    & $GifskiPath --quiet --fps $Fps --width $gifWidth --quality $Quality --repeat 0 --output $stagedGif (Join-Path $workDir 'frame-*.png')
    if ($LASTEXITCODE -ne 0) { throw "Gifski failed (exit $LASTEXITCODE)." }

    if ($Mp4Path) {
        Write-Host 'Encoding matching MP4 at the source frame rate...'
        $mp4Filters = $cropFilter + @('pad=ceil(iw/2)*2:ceil(ih/2)*2')
        & $FfmpegPath @inputArgs -vf ($mp4Filters -join ',') -c:v libx264 -crf 16 -preset slow -pix_fmt yuv420p -movflags +faststart (Join-Path $workDir 'output.mp4')
        if ($LASTEXITCODE -ne 0) { throw "MP4 encoding failed (exit $LASTEXITCODE)." }
    }

    # Publish only after every requested encode succeeds.
    foreach ($output in $outputs) {
        New-Item -ItemType Directory -Path (Split-Path -Parent $output.Path) -Force | Out-Null
        Move-Item -LiteralPath (Join-Path $workDir $output.StagedName) -Destination $output.Path -Force:$Force
        $result = Get-Item -LiteralPath $output.Path
        Write-Host ('Created {0} ({1:N2} MB)' -f $result.FullName, ($result.Length / 1000000))
    }
}
finally {
    # Only remove this invocation's generated directory inside artifacts.
    $resolvedWorkDir = [IO.Path]::GetFullPath($workDir)
    if (-not $resolvedWorkDir.StartsWith($workRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean up outside artifacts: $resolvedWorkDir"
    }
    Remove-Item -LiteralPath $resolvedWorkDir -Recurse -Force
}
