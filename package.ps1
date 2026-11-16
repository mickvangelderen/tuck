dotnet build "$PSScriptRoot/src/Tuck.Mod/Tuck.Mod.csproj" -t:Package -tl:off @args
exit $LASTEXITCODE
