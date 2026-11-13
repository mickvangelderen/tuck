# Run with powershell -NoProfile -ExecutionPolicy Bypass -File tools/GameSnapshots.Tests.ps1
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/GameSnapshots.ps1"
$artifacts = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../artifacts'))
$testRoot = Join-Path $artifacts ('game-snapshot-tests with spaces-' + [Guid]::NewGuid().ToString('N'))
$repository = Join-Path $testRoot 'probably-stolen-assemblies'
$mod = Join-Path $testRoot 'mod'
$worktree = Join-Path $mod 'probably-stolen-assemblies'
$props = Join-Path $mod 'GameReferences.local.props'
$libraries = Join-Path $testRoot 'installed'
$script:assertions = 0

function Assert($Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    $script:assertions++
}
function Assert-Fails([scriptblock]$Action, [string]$Message) {
    try { & $Action }
    catch { Assert ($_.Exception.Message -like "*$Message*") "Unexpected error: $_"; return }
    throw "Expected failure: $Message"
}
function New-Version([string]$Display = '100-Rev13', [string]$Unity = '0.46D') {
    return [pscustomobject]@{ 'display-version' = $Display; 'unity-version' = $Unity; 'decompiler-version' = '11.2.0.10050' }
}
function Set-Dll([string]$Text) { [IO.File]::WriteAllText("$libraries/Assembly-CSharp.dll", $Text) }
function Initialize-Repository([string]$Path) {
    Invoke-GameGit $testRoot @('init', '--quiet', '--bare', $Path) | Out-Null
    Invoke-GameGit $Path @('config', 'user.name', 'Snapshot tests') | Out-Null
    Invoke-GameGit $Path @('config', 'user.email', 'snapshot@example.invalid') | Out-Null
    Invoke-GameGit $Path @('config', 'commit.gpgsign', 'false') | Out-Null
}
$counter = [pscustomobject]@{ Count = 0 }
$decompile = {
    param($Lib, $Src)
    $counter.Count++
    [IO.Directory]::CreateDirectory($Src) | Out-Null
    [IO.File]::WriteAllText((Join-Path $Src 'Game.cs'), [IO.File]::ReadAllText((Join-Path $Lib 'Assembly-CSharp.dll')))
}.GetNewClosure()

[IO.Directory]::CreateDirectory("$mod/tools") | Out-Null
[IO.Directory]::CreateDirectory($libraries) | Out-Null
Copy-Item -LiteralPath "$PSScriptRoot/../fetch.ps1" -Destination "$mod/fetch.ps1"
Copy-Item -LiteralPath "$PSScriptRoot/GameSnapshots.ps1", "$PSScriptRoot/FindGame.ps1" -Destination "$mod/tools"
$fetch = "$mod/fetch.ps1"
try {
    Initialize-Repository $repository
    Set-Dll 'A'
    $first = Import-GameSnapshot $repository $libraries (New-Version) -Decompile $decompile
    Assert ($first -match '\A[0-9a-f]{40}\z') 'Import did not return a commit.'
    # Existing snapshots may have been written by another serializer or on another OS.
    $legacy = Join-Path $testRoot 'legacy-format'
    Invoke-GameGit $repository @('worktree', 'add', '--quiet', '--detach', $legacy, $first) | Out-Null
    $legacyJson = [IO.File]::ReadAllText("$legacy/snapshot.json").Replace("`n", "`r`n")
    [IO.File]::WriteAllText("$legacy/snapshot.json", $legacyJson)
    Invoke-GameGit $legacy @('add', 'snapshot.json') | Out-Null
    Invoke-GameGit $legacy @('commit', '--quiet', '-m', 'Use legacy metadata formatting') | Out-Null
    $first = Invoke-GameGit $legacy @('rev-parse', 'HEAD')
    foreach ($reference in @('refs/heads/0.46D', 'refs/tags/0.46D', 'refs/tags/100-Rev13')) {
        Invoke-GameGit $repository @('update-ref', $reference, $first) | Out-Null
    }
    Invoke-GameGit $repository @('worktree', 'remove', '--force', $legacy) | Out-Null
    Assert ((Import-GameSnapshot $repository $libraries (New-Version) -Decompile $decompile) -eq $first) 'Unchanged import created a new commit.'
    Assert ($counter.Count -eq 1) 'Unchanged import decompiled again.'
    Set-Dll 'B'
    $second = Import-GameSnapshot $repository $libraries (New-Version) -Decompile $decompile
    Assert ($second -ne $first) 'Changed DLLs with the same labels did not create a commit.'
    Assert ((Invoke-GameGit $repository @('rev-parse', "$second^")) -eq $first) 'Import lost its branch history.'
    foreach ($tag in @('100-Rev13', '0.46D')) {
        Assert ((Invoke-GameGit $repository @('rev-parse', "refs/tags/$tag")) -eq $second) "Tag $tag did not move."
    }
    Set-Dll 'A'
    $third = Import-GameSnapshot $repository $libraries (New-Version) -Decompile $decompile
    Assert ($third -ne $first -and $third -ne $second) 'A -> B -> A reused a historical commit instead of appending.'
    Assert ((Invoke-GameGit $repository @('rev-parse', "$third^{tree}")) -eq (Invoke-GameGit $repository @('rev-parse', "$first^{tree}"))) 'A -> B -> A did not reuse identical trees.'
    Assert ($counter.Count -eq 2) 'A -> B -> A decompiled A again.'
    $otherVersion = Import-GameSnapshot $repository $libraries (New-Version '101' '0.47') -Decompile $decompile
    Assert ($counter.Count -eq 2) 'New labels prevented source reuse.'
    Assert ((Invoke-GameGit $repository @('rev-list', '--count', $otherVersion)) -eq '1') 'Different unity-version inherited another branch.'

    # Defaults and config are resolved against the script's directory, not the current location.
    [IO.File]::WriteAllText("$mod/game-cache.local.json", '{"revision":"100-Rev13"}')
    Push-Location $testRoot
    try { & $fetch } finally { Pop-Location }
    Assert ((Invoke-GameGit $worktree @('rev-parse', 'HEAD')) -eq $third) 'Saved revision/default paths were not used.'
    [xml]$selected = [IO.File]::ReadAllText($props)
    Assert ($selected.Project.PropertyGroup.GameAssembliesDir -eq (Join-Path $worktree 'lib')) 'Props select the wrong lib directory.'
    $timestamp = [IO.File]::GetLastWriteTimeUtc($props)
    & $fetch
    Assert ([IO.File]::GetLastWriteTimeUtc($props) -eq $timestamp) 'Unchanged fetch rewrote build properties.'
    & $fetch -Revision $first
    Assert ((Invoke-GameGit $worktree @('rev-parse', 'HEAD')) -eq $first) 'Explicit revision did not override config.'

    # A restore repairs edits and removes generated/ignored files without moving an attached branch.
    Invoke-GameGit $worktree @('switch', '--quiet', '-c', 'user-branch', $first) | Out-Null
    [IO.File]::WriteAllText("$repository/info/exclude", "generated/`n")
    [IO.Directory]::CreateDirectory("$worktree/generated") | Out-Null
    [IO.Directory]::CreateDirectory("$worktree/src/obj") | Out-Null
    [IO.File]::WriteAllText("$worktree/generated/cache", 'ignored')
    [IO.File]::WriteAllText("$worktree/src/obj/project.assets.json", 'editor output')
    [IO.File]::WriteAllText("$worktree/lib/Assembly-CSharp.dll", 'edited')
    & $fetch
    Assert ([IO.File]::ReadAllText("$worktree/lib/Assembly-CSharp.dll") -eq 'A') 'Fetch did not restore the DLL.'
    Assert (-not [IO.Directory]::Exists("$worktree/generated") -and -not [IO.Directory]::Exists("$worktree/src/obj")) 'Fetch did not clean generated files.'
    Assert ((Invoke-GameGit $repository @('rev-parse', 'refs/heads/user-branch')) -eq $first) 'Restore moved an attached branch.'
    Assert ($null -eq (Invoke-GameGit $worktree @('symbolic-ref', '-q', 'HEAD') -AllowFailure)) 'Restore left HEAD attached.'

    $otherWorktree = Join-Path $testRoot 'other-mod/game'
    & $fetch -Revision $second -WorktreePath $otherWorktree -PropsPath "$testRoot/other-mod/game.props"
    Assert ([IO.File]::ReadAllText("$otherWorktree/src/Game.cs") -eq 'B' -and [IO.File]::ReadAllText("$worktree/src/Game.cs") -eq 'A') 'One mod changed another mod''s worktree.'
    Set-Dll 'C'
    Assert-Fails { Import-GameSnapshot $repository $libraries (New-Version) -Decompile { throw 'decompiler failed' } } 'decompiler failed'
    Assert ((Invoke-GameGit $repository @('rev-parse', 'refs/tags/100-Rev13')) -eq $third) 'Failed import moved a tag.'
    Assert (@([IO.Directory]::GetDirectories($repository, 'import-*')).Count -eq 0) 'Failed import left staging files.'
    Assert-Fails { Import-GameSnapshot $repository $libraries (New-Version 'invalid..label') -Decompile $decompile } 'check-ref-format failed'
    $before = [IO.File]::ReadAllText($props)
    Assert-Fails { & $fetch -Revision ('1' * 40) } 'unavailable'
    Assert ([IO.File]::ReadAllText($props) -eq $before) 'Missing commit replaced build settings.'

    $unrelated = Join-Path $testRoot 'unrelated'
    [IO.Directory]::CreateDirectory($unrelated) | Out-Null
    [IO.File]::WriteAllText("$unrelated/keep.txt", 'keep')
    Assert-Fails { & $fetch -WorktreePath $unrelated } 'not a linked Git worktree'
    Assert ([IO.File]::ReadAllText("$unrelated/keep.txt") -eq 'keep') 'Fetch changed an unrelated folder.'
    $anotherRepository = Join-Path $testRoot 'another.git'
    Initialize-Repository $anotherRepository
    Assert-Fails { & $fetch -Repository $anotherRepository } 'configured bare repository'
    Assert ((Invoke-GameGit $worktree @('rev-parse', 'HEAD')) -eq $third) 'Fetch changed a worktree belonging to another repository.'
    Assert-Fails { & $fetch -Repository "$testRoot/missing" } 'not found'
    Invoke-GameGit $testRoot @('init', '--quiet', "$testRoot/ordinary") | Out-Null
    Assert-Fails { & $fetch -Repository "$testRoot/ordinary" -WorktreePath "$testRoot/not-created" } 'bare repository'

    # Local tags remain local until explicitly refreshed; missing tags trigger a fetch.
    $remote = Join-Path $testRoot 'remote.git'
    Initialize-Repository $remote
    Invoke-GameGit $remote @('config', 'uploadpack.allowFilter', 'true') | Out-Null
    Invoke-GameGit $repository @('remote', 'add', 'origin', $remote) | Out-Null
    Invoke-GameGit $repository @('push', '--quiet', 'origin', '--all') | Out-Null
    Invoke-GameGit $repository @('push', '--quiet', 'origin', '--tags') | Out-Null
    $reader = Join-Path $testRoot 'reader.git'
    Invoke-GameGit $testRoot @('clone', '--quiet', '--bare', '--filter=blob:none', ([Uri]$remote).AbsoluteUri, $reader) | Out-Null
    $readerWorktree = Join-Path $testRoot 'reader-mod/game'
    $readerProps = Join-Path $testRoot 'reader-mod/game.props'
    & $fetch -Repository $reader -WorktreePath $readerWorktree -PropsPath $readerProps
    Invoke-GameGit $reader @('remote', 'set-url', 'origin', "$testRoot/offline") | Out-Null
    & $fetch -Repository $reader -WorktreePath $readerWorktree -PropsPath $readerProps
    Assert ((Invoke-GameGit $readerWorktree @('rev-parse', 'HEAD')) -eq $third) 'Known tag did not work offline.'
    Invoke-GameGit $reader @('remote', 'set-url', 'origin', ([Uri]$remote).AbsoluteUri) | Out-Null
    Set-Dll 'B'
    $fourth = Import-GameSnapshot $repository $libraries (New-Version) -Decompile $decompile
    Invoke-GameGit $repository @('push', '--quiet', 'origin', 'refs/heads/0.46D', '+refs/tags/100-Rev13', '+refs/tags/0.46D') | Out-Null
    & $fetch -Repository $reader -WorktreePath $readerWorktree -PropsPath $readerProps
    Assert ((Invoke-GameGit $readerWorktree @('rev-parse', 'HEAD')) -eq $third) 'Known tag was unnecessarily refreshed.'
    & $fetch -Repository $reader -WorktreePath $readerWorktree -PropsPath $readerProps -Refresh
    Assert ((Invoke-GameGit $readerWorktree @('rev-parse', 'HEAD')) -eq $fourth) 'Explicit refresh missed a moved tag.'
    $fifth = Import-GameSnapshot $repository $libraries (New-Version '100-Rev15') -Decompile $decompile
    Invoke-GameGit $repository @('push', '--quiet', 'origin', 'refs/heads/0.46D', 'refs/tags/100-Rev15', '+refs/tags/0.46D') | Out-Null
    & $fetch -Repository $reader -WorktreePath $readerWorktree -PropsPath $readerProps -Revision 100-Rev15
    Assert ((Invoke-GameGit $readerWorktree @('rev-parse', 'HEAD')) -eq $fifth) 'Missing tag was not fetched.'
    Assert ($counter.Count -eq 2) 'Recorded DLLs were decompiled again.'

    Initialize-Repository $reader
    Set-Dll 'A'
    $readerImport = Import-GameSnapshot $reader $libraries (New-Version) -Decompile $decompile
    Assert ((Invoke-GameGit $reader @('rev-parse', "$readerImport^")) -eq $fifth) 'Import did not follow the newer remote branch.'
    Set-Dll 'C'
    Import-GameSnapshot $repository $libraries (New-Version) -Decompile $decompile | Out-Null
    Invoke-GameGit $repository @('push', '--quiet', 'origin', 'refs/heads/0.46D', '+refs/tags/100-Rev13', '+refs/tags/0.46D') | Out-Null
    Update-GameRepository $reader
    Assert-Fails { Import-GameSnapshot $reader $libraries (New-Version) -Decompile $decompile } 'histories diverged'
    Assert ((Invoke-GameGit $reader @('rev-parse', 'refs/heads/0.46D')) -eq $readerImport) 'Divergence replaced local history.'
    Write-Host "Passed $script:assertions snapshot checks."
}
finally {
    $full = [IO.Path]::GetFullPath($testRoot)
    if (-not (Test-GamePath ([IO.Path]::GetDirectoryName($full)) $artifacts) -or -not [IO.Path]::GetFileName($full).StartsWith('game-snapshot-tests with spaces-')) { throw 'Unexpected test cleanup path.' }
    Remove-Item -LiteralPath $full -Recurse -Force
}
