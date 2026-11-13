<#
.SYNOPSIS
Builds and publishes Tuck through the logged-in Steam desktop client.
.DESCRIPTION
Edit workshop.json for the title. Manage visibility on Steam's Workshop page.
Keep Steam online as the Workshop item's owner.
CHANGELOG.md must have an empty Unreleased section followed by a release section
whose version matches the staged manifest. That section becomes Steam's change note.
Description media must be committed in HEAD with no staged or working-tree changes.
The GitHub origin and HEAD SHA determine raw.githubusercontent.com URLs.
PrepareOnly validates inputs and writes complete offline description and change note previews.
CheckOnly also verifies public media URLs and Steam ownership, and reports slideshow changes without uploading.
Push the commit to the public GitHub origin before checking or publishing.
For a new item, set publishedFileId to 0. Publishing saves its new ID immediately;
commit that change and keep the ID for subsequent releases.
Description media is hosted on GitHub; the cover image is uploaded to Steam.
Slideshow images come only from media/slideshow/, ordered by leading filename number
(01-demo.gif, 02-hotkeys.png), then filename. Each must be PNG, JPG or GIF under 1 MB.
Uploads use content hashes as filenames. Publishing replaces changed slots and removes
surplus images, while preserving manually attached videos and the separate cover image.
A missing slideshow folder leaves existing images alone; an empty folder clears them.
Use .gitkeep to retain an intentionally empty folder in Git.
Generated descriptions and the recovery ID live under artifacts/workshop/.
Use package.ps1 for ZIP releases.
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
    (Join-Path $PSScriptRoot 'CHANGELOG.md'),
    (Join-Path $targetDir 'mod'),
    (Join-Path $PSScriptRoot 'media/tuck.png'),
    (Join-Path $PSScriptRoot 'artifacts/workshop')
)
dotnet run --project (Join-Path $PSScriptRoot 'tools/WorkshopPublish') -c Release -- @publisherArgs
if ($LASTEXITCODE -ne 0) { throw "Workshop $mode failed (exit $LASTEXITCODE)." }
