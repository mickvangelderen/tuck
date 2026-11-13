param([string]$GameDir, [switch]$Optional)

$ErrorActionPreference = 'Stop'
if ($GameDir) {
    if (-not (Test-Path -LiteralPath (Join-Path $GameDir 'Probably Stolen.exe'))) {
        throw "Game installation not found at '$GameDir'."
    }
    return (Resolve-Path -LiteralPath $GameDir).Path
}
foreach ($key in @(
    'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 4348910',
    'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 4348910'
)) {
    $location = (Get-ItemProperty -LiteralPath $key -Name InstallLocation -ErrorAction SilentlyContinue).InstallLocation
    if ($location -and (Test-Path -LiteralPath (Join-Path $location 'Probably Stolen.exe'))) { return $location }
}
if (-not $Optional) { throw 'Steam installation not found. Pass -GameDir with the game installation folder.' }
