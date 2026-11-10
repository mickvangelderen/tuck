dotnet build "$PSScriptRoot/src/Tuck.Mod/Tuck.Mod.csproj" -t:Deploy @args
exit $LASTEXITCODE
