<#
.SYNOPSIS
Builds and publishes Tuck through the logged-in Steam desktop client.
.DESCRIPTION
Edit workshop.json for the title, visibility (private/public/friendsOnly/unlisted),
and change note. Keep Steam online as the Workshop item's owner.
PrepareOnly validates inputs and writes an offline description preview.
CheckOnly also queries Steam for ownership and existing images, without uploading.
For a new item, set publishedFileId to 0. Publishing saves its new ID immediately;
commit that change and keep the ID for subsequent releases.
Images and content are published in one invocation. Generated descriptions and
the recovery ID live under artifacts/workshop/. Use package.ps1 for ZIP releases.
.EXAMPLE
.\publish.ps1 -PrepareOnly
.EXAMPLE
.\publish.ps1 -CheckOnly
.EXAMPLE
.\publish.ps1
#>
[CmdletBinding(DefaultParameterSetName = 'Publish')]
param(
    [Parameter(Mandatory = $true, ParameterSetName = 'Prepare')]
    [switch]$PrepareOnly,

    [Parameter(Mandatory = $true, ParameterSetName = 'Check')]
    [switch]$CheckOnly
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src/Tuck.Mod/Tuck.Mod.csproj'
dotnet build $project -c Release -t:StageMod
if ($LASTEXITCODE -ne 0) { throw "Release staging failed (exit $LASTEXITCODE)." }

$targetDir = dotnet msbuild $project -nologo -property:Configuration=Release -getProperty:TargetDir
if ($LASTEXITCODE -ne 0) { throw 'Could not resolve the Release output directory.' }

$mode = if ($PrepareOnly) { 'prepare' } elseif ($CheckOnly) { 'check' } else { 'publish' }
$publisherArgs = @(
    $mode,
    (Join-Path $PSScriptRoot 'workshop.json'),
    (Join-Path $PSScriptRoot 'README.md'),
    (Join-Path $targetDir 'mod'),
    (Join-Path $PSScriptRoot 'media/tuck.png'),
    (Join-Path $PSScriptRoot 'artifacts/workshop')
)
dotnet run --project (Join-Path $PSScriptRoot 'tools/WorkshopPublish') -c Release -- @publisherArgs
if ($LASTEXITCODE -ne 0) { throw "Workshop $mode failed (exit $LASTEXITCODE)." }
