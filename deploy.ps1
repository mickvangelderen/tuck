param([string]$GameDir)

$ErrorActionPreference = 'Stop'
$GameDir = & "$PSScriptRoot/tools/FindGame.ps1" -GameDir $GameDir
dotnet build "$PSScriptRoot/src/Tuck.Mod/Tuck.Mod.csproj" -t:Deploy "-p:GameDir=$GameDir" @args
exit $LASTEXITCODE
