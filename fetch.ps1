param([string]$GameDir)

$ErrorActionPreference = 'Stop'

if (-not $GameDir) {
    Write-Host 'Resolving game installation from Steam...'
    $GameDir = dotnet msbuild "$PSScriptRoot/src/Tuck.Mod/Tuck.Mod.csproj" -nologo -getProperty:GameDir
    if ($LASTEXITCODE -ne 0) { throw 'Could not resolve the game installation.' }
}
if (-not $GameDir) { throw 'Steam installation not found. Pass -GameDir with the game installation folder.' }
Write-Host "Using game installation at $GameDir"

# Taking ownership of the output directory before performing work is intentional design.
$dependencyDir = Join-Path $PSScriptRoot 'dependencies/probably-stolen'
$oldDependencyDir = "$dependencyDir.old"
New-Item -ItemType Directory -Path (Split-Path $dependencyDir) -Force | Out-Null
$hadPrevious = $true
try { [IO.Directory]::Move($dependencyDir, $oldDependencyDir) }
catch [IO.DirectoryNotFoundException] { $hadPrevious = $false }
catch [IO.IOException], [UnauthorizedAccessException] {
    if (($_.Exception.HResult -band 0xFFFF) -in @(5, 32, 33)) {
        throw "Cannot rename '$dependencyDir'. Close VS Code and other programs watching or using this folder, then rerun fetch.ps1 from an external terminal. Existing dependencies have not been changed."
    }
    if ($_.Exception.HResult -ne 0x800700B7) { throw } # ERROR_ALREADY_EXISTS
    throw "'$oldDependencyDir' exists from an interrupted fetch; restore or delete it first."
}
if ($hadPrevious) { Write-Host "Moved previous dependency to $oldDependencyDir" }

try {
    # Copy assemblies from game.
    $managedDir = Join-Path $GameDir 'Probably Stolen_Data/Managed'
    $assembliesDir = Join-Path $dependencyDir 'assemblies'
    New-Item -ItemType Directory -Path $assembliesDir -Force | Out-Null
    Write-Host "Copying game assemblies from $managedDir..."
    Get-ChildItem -LiteralPath $managedDir -Filter '*.dll' -File | Copy-Item -Destination $assembliesDir

    # Capture both the manifest compatibility version and the public release label.
    $gameSettingsPath = Join-Path $GameDir 'Probably Stolen_Data/globalgamemanagers'
    $gameVersionPath = Join-Path $dependencyDir 'game-version.txt'
    $gameDisplayVersionPath = Join-Path $dependencyDir 'game-display-version.txt'
    $assemblyPath = Join-Path $assembliesDir 'Assembly-CSharp.dll'
    # Player builds strip type trees; pin the definitions used to read PlayerSettings.
    $classDataPath = Join-Path $dependencyDir 'unity-classdata.tpk'
    Write-Host 'Fetching Unity class definitions...'
    $classData = Invoke-RestMethod -Uri 'https://api.github.com/repos/nesrak1/UABEA/git/blobs/a68aff1288d95bd6a58ec6ad99813ea9c59e0abc'
    [IO.File]::WriteAllBytes($classDataPath, [Convert]::FromBase64String($classData.content))
    dotnet run --project "$PSScriptRoot/tools/ReadGameVersion" -- $gameSettingsPath $classDataPath $assemblyPath $gameVersionPath $gameDisplayVersionPath
    if ($LASTEXITCODE -ne 0) { throw "Game version extraction failed (exit $LASTEXITCODE)." }

    # Decompile assembly.
    $decompiledDir = Join-Path $dependencyDir 'decompiled'
    Write-Host 'Decompiling Assembly-CSharp.dll...'
    dotnet tool execute ilspycmd@11.2.0.10050 -- -p --disable-updatecheck -r $assembliesDir -o $decompiledDir $assemblyPath
    if ($LASTEXITCODE -ne 0) { throw "Decompilation failed (exit $LASTEXITCODE)." }
}
catch {
    if (Test-Path -LiteralPath $dependencyDir) { Remove-Item -LiteralPath $dependencyDir -Recurse -Force }
    if ($hadPrevious) {
        [IO.Directory]::Move($oldDependencyDir, $dependencyDir)
        Write-Warning 'Fetch failed; restored the previous dependency.'
    }
    throw
}

if ($hadPrevious) {
    Write-Host 'Removing previous dependency...'
    Remove-Item -LiteralPath $oldDependencyDir -Recurse -Force
}
Write-Host "Fetched game dependency to $dependencyDir"
