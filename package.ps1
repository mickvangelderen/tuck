dotnet build "$PSScriptRoot/src/Tuck.Mod/Tuck.Mod.csproj" -t:Package @args
exit $LASTEXITCODE
