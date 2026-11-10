$ErrorActionPreference = 'Stop'

$steamCmdDir = Join-Path $PSScriptRoot 'dependencies/steamcmd'
$steamCmdPath = Join-Path $steamCmdDir 'steamcmd.exe'

if (-not (Test-Path -LiteralPath $steamCmdPath -PathType Leaf)) {
    New-Item -ItemType Directory -Path $steamCmdDir -Force | Out-Null
    $archivePath = Join-Path $steamCmdDir 'steamcmd.zip'
    Write-Host 'Downloading SteamCMD...'
    Invoke-WebRequest -UseBasicParsing -Uri 'https://client-update.steamstatic.com/installer/steamcmd.zip' -OutFile $archivePath
    Expand-Archive -LiteralPath $archivePath -DestinationPath $steamCmdDir -Force
}

return $steamCmdPath
