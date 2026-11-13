param(
    [string]$Repository,
    [string]$Revision,
    [string]$WorktreePath,
    [string]$GameDir,
    [string]$PropsPath,
    [switch]$Refresh,
    [switch]$Installed,
    [switch]$Push,
    [switch]$Unsigned
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/tools/GameSnapshots.ps1"
$configPath = Join-Path $PSScriptRoot 'game-cache.local.json'
if (Test-Path -LiteralPath $configPath) {
    $config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
    if (-not $Repository) { $Repository = $config.repository }
    if (-not $Revision) { $Revision = $config.revision }
    if (-not $WorktreePath) { $WorktreePath = $config.worktreePath }
    if (-not $GameDir) { $GameDir = $config.gameDir }
    if (-not $PropsPath) { $PropsPath = $config.propsPath }
}
if (-not $Repository) { $Repository = '../probably-stolen-assemblies' }
if (-not $WorktreePath) { $WorktreePath = 'probably-stolen-assemblies' }
if (-not $PropsPath) { $PropsPath = 'GameReferences.local.props' }
# Resolve paths relative to this mod, including when fetch is called from elsewhere.
$Repository = [IO.Path]::GetFullPath([IO.Path]::Combine($PSScriptRoot, $Repository))
$WorktreePath = [IO.Path]::GetFullPath([IO.Path]::Combine($PSScriptRoot, $WorktreePath))
$PropsPath = [IO.Path]::GetFullPath([IO.Path]::Combine($PSScriptRoot, $PropsPath))
if ($GameDir) { $GameDir = [IO.Path]::GetFullPath([IO.Path]::Combine($PSScriptRoot, $GameDir)) }
if (-not $Revision) { $Installed = $true }
if ($Push -and -not $Installed) { throw 'Use -Installed -Push to publish an import.' }
if (-not [IO.Directory]::Exists($Repository)) { throw "Bare game repository not found: $Repository. Use git clone --bare or git init --bare first." }
function Resolve-GameSelection([string]$Reference) {
    $location = if ($hasWorktree) { $WorktreePath } else { $Repository }
    $identity = if ($hasWorktree) { '--show-toplevel' } else { '--is-bare-repository' }
    # One Git call resolves the revision and checks ownership before any reset or cleanup.
    $result = @(& git @gameGitOptions -C $location rev-parse --path-format=absolute --git-common-dir $identity --verify --quiet "$Reference^{commit}")
    $code = $LASTEXITCODE
    if ($result.Count -lt 2 -or -not (Test-GamePath $result[0] $Repository)) { throw "Expected the configured bare repository: $Repository" }
    if ($hasWorktree) {
        if (-not (Test-GamePath $result[1] $WorktreePath)) { throw "Worktree path does not match its Git root: $WorktreePath" }
    }
    elseif ($result[1] -ne 'true') { throw "Expected a bare Git repository: $Repository" }
    if ($code -eq 0) { return $result[2] }
    if ($code -ne 1) { throw "Could not resolve $Reference (exit $code)." }
    return $null
}

$deadline = [DateTime]::UtcNow.AddMinutes(2)
while ($true) {
    try { $cacheLock = [IO.File]::Open((Join-Path $Repository 'cache.lock'), 'OpenOrCreate', 'ReadWrite', 'None'); break }
    catch [IO.IOException] {
        if ([DateTime]::UtcNow -ge $deadline) { throw }
        Start-Sleep -Milliseconds 100
    }
}
try {
    $hasWorktree = [IO.Directory]::Exists($WorktreePath)
    if ($hasWorktree -and -not [IO.File]::Exists((Join-Path $WorktreePath '.git'))) {
        throw "Worktree path already exists and is not a linked Git worktree: $WorktreePath"
    }
    $pinned = $Revision -match '\A[0-9a-fA-F]{40}\z'
    $reference = if ($Installed) { 'HEAD' } elseif ($pinned) { $Revision } else { "refs/tags/$Revision" }
    $commit = Resolve-GameSelection $reference
    if ($Refresh -or $Installed -or -not $commit) {
        Update-GameRepository $Repository
        if (-not $Installed) { $commit = Resolve-GameSelection $reference }
    }
    if ($Installed -or -not $commit) {
        if (-not $Installed -and $pinned) { throw "Snapshot '$Revision' is unavailable." }
        $GameDir = & "$PSScriptRoot/tools/FindGame.ps1" -GameDir $GameDir -Optional:(-not $Installed)
        if (-not $GameDir) { throw "Snapshot '$Revision' is unavailable. Import a matching installation with -Installed." }
        $snapshot = Read-InstalledGameSnapshot $Repository $GameDir
        if (-not $Installed -and $Revision -ne $snapshot.'display-version' -and $Revision -ne $snapshot.'unity-version') {
            throw "Requested '$Revision', but the installed game is $($snapshot.'display-version') / $($snapshot.'unity-version')."
        }
        $commit = Import-GameSnapshot $Repository (Join-Path $GameDir 'Probably Stolen_Data/Managed') $snapshot -Unsigned:$Unsigned
        if ($Push) {
            $branch = 'refs/heads/' + $snapshot.'unity-version'
            $refs = @('push', '--atomic', 'origin', "${branch}:${branch}")
            foreach ($label in @($snapshot.'display-version', $snapshot.'unity-version' | Select-Object -Unique)) {
                $refs += "+refs/tags/${label}:refs/tags/${label}"
            }
            Invoke-GameGit $Repository $refs | Out-Null
        }
    }

    if ($hasWorktree) {
        Invoke-GameGit $WorktreePath @('checkout', '--quiet', '--detach', '--force', $commit, '--') | Out-Null
        Invoke-GameGit $WorktreePath @('clean', '--quiet', '-ffdx') | Out-Null
    }
    else {
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($WorktreePath)) | Out-Null
        try { Invoke-GameGit $Repository @('worktree', 'add', '--quiet', '--detach', $WorktreePath, $commit) | Out-Null }
        catch {
            Invoke-GameGit $Repository @('worktree', 'remove', '--force', $WorktreePath) -AllowFailure | Out-Null
            throw
        }
    }
    $snapshot = Read-GameSnapshot ([IO.File]::ReadAllText((Join-Path $WorktreePath 'snapshot.json')))
    if (-not [IO.File]::Exists((Join-Path $WorktreePath 'lib/Assembly-CSharp.dll')) -or -not [IO.Directory]::Exists((Join-Path $WorktreePath 'src'))) {
        throw "Incomplete game snapshot: $commit"
    }
    $properties = [ordered]@{
        GameSnapshotDir = $WorktreePath
        GameAssembliesDir = Join-Path $WorktreePath 'lib'
        GameUnityVersion = $snapshot.'unity-version'
        GameDisplayVersion = $snapshot.'display-version'
        GameSnapshotCommit = $commit
    }
    $xml = @('<Project>', '  <PropertyGroup>') + @(foreach ($name in $properties.Keys) {
        "    <$name>$([Security.SecurityElement]::Escape($properties[$name]))</$name>"
    }) + @('  </PropertyGroup>', '</Project>', '')
    $text = $xml -join "`r`n"
    if (-not [IO.File]::Exists($PropsPath) -or [IO.File]::ReadAllText($PropsPath) -cne $text) {
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($PropsPath)) | Out-Null
        [IO.File]::WriteAllText("$PropsPath.tmp", $text)
        if ([IO.File]::Exists($PropsPath)) { [IO.File]::Replace("$PropsPath.tmp", $PropsPath, [NullString]::Value) }
        else { [IO.File]::Move("$PropsPath.tmp", $PropsPath) }
    }
    Write-Host "Selected $($snapshot.'display-version') / $($snapshot.'unity-version') at $commit"
}
finally { $cacheLock.Dispose() }
