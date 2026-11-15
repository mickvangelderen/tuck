param([string]$GameDir)

$ErrorActionPreference = 'Stop'
$GameDir = & "$PSScriptRoot/tools/FindGame.ps1" -GameDir $GameDir
dotnet build "$PSScriptRoot/src/Tuck.Mod/Tuck.Mod.csproj" -t:Deploy "-p:GameDir=$GameDir" @args
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Start-Process 'steam://run/4348910'
exit 0
