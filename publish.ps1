<#
.SYNOPSIS
Builds and publishes Tuck to Steam Workshop.
.DESCRIPTION
The description is generated from README.md as Steam BBCode, omitting images
and local link destinations. Edit workshop.template.vdf for the title, change
note, and visibility (2 = private, 0 = public). The generated VDF and a readable
description.txt live under Git-ignored artifacts/workshop/.
SteamCMD prompts for authentication; credentials are never stored in the VDF.
By default, fetch-steamcmd.ps1 downloads SteamCMD into dependencies/ if missing.
After creating an item, this script saves its ID in the template. Commit that
change and keep the ID for subsequent releases.
.EXAMPLE
.\publish.ps1 -PrepareOnly
.EXAMPLE
.\publish.ps1 -SteamUsername your_account
.EXAMPLE
.\publish.ps1 -SteamUsername your_account -SteamCmd C:\SteamCMD\steamcmd.exe
#>
[CmdletBinding(DefaultParameterSetName = 'Publish')]
param(
    [Parameter(Mandatory = $true, ParameterSetName = 'Publish')]
    [ValidateNotNullOrEmpty()]
    [string]$SteamUsername,

    [Parameter(ParameterSetName = 'Publish')]
    [string]$SteamCmd,

    [Parameter(Mandatory = $true, ParameterSetName = 'Prepare')]
    [switch]$PrepareOnly
)

$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding($false)
$templatePath = Join-Path $PSScriptRoot 'workshop.template.vdf'
$outputDir = Join-Path $PSScriptRoot 'artifacts/workshop'
$vdfPath = Join-Path $outputDir 'workshop.vdf'
$descriptionPath = Join-Path $outputDir 'description.txt'
$idPattern = '(?m)^(\s*"publishedfileid"\s+")(\d+)(")'

function Get-WorkshopId([string]$Text) {
    $matches = [regex]::Matches($Text, $idPattern)
    if ($matches.Count -ne 1) { throw 'Expected exactly one numeric publishedfileid in the Workshop VDF.' }
    return $matches[0].Groups[2].Value
}

function Save-WorkshopId([string]$Id) {
    # Read again so SteamCMD's interactive login does not leave us writing stale metadata.
    $current = [IO.File]::ReadAllText($templatePath)
    $currentId = Get-WorkshopId $current
    if ($currentId -eq $Id) { return }
    if ($currentId -ne '0') { throw "Workshop ID conflict: template has $currentId, upload file has $Id. Keep $vdfPath to recover." }
    $updated = [regex]::Replace($current, $idPattern, [System.Text.RegularExpressions.MatchEvaluator]{
        param($match)
        $match.Groups[1].Value + $Id + $match.Groups[3].Value
    })
    [IO.File]::WriteAllText($templatePath, $updated, $utf8)
    Write-Host "Saved Workshop ID $Id in workshop.template.vdf. Commit this change."
}

function ConvertTo-VdfString([string]$Text) {
    return $Text.Replace('\', '\\').Replace('"', '\"')
}

$template = [IO.File]::ReadAllText($templatePath)
[void](Get-WorkshopId $template)
if (Test-Path -LiteralPath $vdfPath) {
    $previousId = Get-WorkshopId ([IO.File]::ReadAllText($vdfPath))
    if ($previousId -ne '0') {
        # Recover an ID left behind by an interrupted upload before replacing its VDF.
        Save-WorkshopId $previousId
        $template = [IO.File]::ReadAllText($templatePath)
    }
}

if (-not $PrepareOnly) {
    if (-not $SteamCmd) { $SteamCmd = & (Join-Path $PSScriptRoot 'fetch-steamcmd.ps1') }
    $steamCommand = Get-Command $SteamCmd -CommandType Application, ExternalScript -ErrorAction Stop
}

New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
dotnet run --file (Join-Path $PSScriptRoot 'tools/WorkshopDescription.cs') -- (Join-Path $PSScriptRoot 'README.md') $descriptionPath
if ($LASTEXITCODE -ne 0) { throw 'README to Steam BBCode conversion failed.' }
$description = [IO.File]::ReadAllText($descriptionPath).TrimEnd()

$project = Join-Path $PSScriptRoot 'src/Tuck.Mod/Tuck.Mod.csproj'
dotnet build $project -c Release -t:Package
if ($LASTEXITCODE -ne 0) { throw "Release packaging failed (exit $LASTEXITCODE)." }

$targetDir = dotnet msbuild $project -nologo -property:Configuration=Release -getProperty:TargetDir
if ($LASTEXITCODE -ne 0) { throw 'Could not resolve the Release output directory.' }
$contentFolder = (Resolve-Path -LiteralPath (Join-Path $targetDir 'mod')).Path
$previewFile = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot 'media/tuck.png')).Path
if (-not (Test-Path -LiteralPath (Join-Path $contentFolder 'manifest.xml') -PathType Leaf)) {
    throw 'The staged mod is missing manifest.xml.'
}

foreach ($placeholder in '{{CONTENT_FOLDER}}', '{{PREVIEW_FILE}}', '{{DESCRIPTION}}') {
    if (-not $template.Contains($placeholder)) { throw "Template is missing $placeholder." }
}
$vdf = $template.Replace('{{CONTENT_FOLDER}}', (ConvertTo-VdfString $contentFolder)).Replace('{{PREVIEW_FILE}}', (ConvertTo-VdfString $previewFile)).Replace('{{DESCRIPTION}}', (ConvertTo-VdfString $description))
[IO.File]::WriteAllText($vdfPath, $vdf, $utf8)
Write-Host "Prepared $vdfPath"
Write-Host "Description preview: $descriptionPath"
if ($PrepareOnly) { return }

# Keep SteamCMD attached to the console for password/Steam Guard prompts. Its exit
# code alone does not describe the upload result; only inspect this run's new log text.
$workshopLogPath = Join-Path (Split-Path $steamCommand.Source) 'logs/workshop_log.txt'
$previousLog = if (Test-Path -LiteralPath $workshopLogPath) { [IO.File]::ReadAllText($workshopLogPath) } else { '' }
try {
    & $steamCommand.Source +login $SteamUsername +workshop_build_item $vdfPath +quit
    $steamExitCode = $LASTEXITCODE
}
finally {
    # Steam can allocate the item before an upload fails. Preserve that ID for retries.
    $publishedId = Get-WorkshopId ([IO.File]::ReadAllText($vdfPath))
    if ($publishedId -ne '0') { Save-WorkshopId $publishedId }
}

$currentLog = if (Test-Path -LiteralPath $workshopLogPath) { [IO.File]::ReadAllText($workshopLogPath) } else { '' }
$newLog = if ($currentLog.StartsWith($previousLog, [StringComparison]::Ordinal)) {
    $currentLog.Substring($previousLog.Length)
} else {
    # SteamCMD can replace the log instead of appending to it.
    $currentLog
}
$results = [regex]::Matches($newLog, '(?m)\[AppID 4348910\] Upload finished for workshop item (\d+) : ([^\r\n]+)')
if ($results.Count -eq 0 -or $results[$results.Count - 1].Groups[1].Value -ne $publishedId -or
    $results[$results.Count - 1].Groups[2].Value.Trim() -ne 'OK') {
    throw "SteamCMD did not confirm a successful upload for item $publishedId (exit $steamExitCode). Check $workshopLogPath."
}
Write-Host "Workshop upload confirmed: https://steamcommunity.com/sharedfiles/filedetails/?id=$publishedId (SteamCMD exit $steamExitCode)."
exit 0
