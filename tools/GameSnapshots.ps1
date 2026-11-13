# Shared by fetch.ps1 and its local Git integration tests.
$gameGitOptions = @('-c', 'core.longpaths=true', '-c', 'core.autocrlf=false', '-c', 'core.filemode=false')

function Invoke-GameGit([string]$Repository, [string[]]$Arguments, [switch]$AllowFailure) {
    $ErrorActionPreference = 'Continue'
    if ($AllowFailure) { $output = & git @gameGitOptions -C $Repository @Arguments 2>$null }
    else { $output = & git @gameGitOptions -C $Repository @Arguments }
    if ($LASTEXITCODE -ne 0) {
        if ($AllowFailure) { return $null }
        throw "Git $($Arguments[0]) failed (exit $LASTEXITCODE)."
    }
    return ($output -join "`n").Trim()
}

function Test-GamePath([string]$First, [string]$Second) {
    return [IO.Path]::GetFullPath($First).TrimEnd('/\') -eq [IO.Path]::GetFullPath($Second).TrimEnd('/\')
}

function Read-GameSnapshot([string]$Json) {
    $snapshot = $Json | ConvertFrom-Json
    foreach ($name in @('display-version', 'unity-version', 'decompiler-version')) {
        if ([string]::IsNullOrWhiteSpace($snapshot.$name)) { throw "Snapshot requires $name." }
    }
    return $snapshot
}

function Update-GameRepository([string]$Repository) {
    if (Invoke-GameGit $Repository @('config', '--get', 'remote.origin.url') -AllowFailure) {
        Invoke-GameGit $Repository @('fetch', 'origin', '+refs/heads/*:refs/remotes/origin/*', '+refs/tags/*:refs/tags/*') | Out-Null
    }
}

function Remove-GameImport([string]$Repository, [string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    if (-not (Test-GamePath ([IO.Path]::GetDirectoryName($full)) $Repository) -or -not [IO.Path]::GetFileName($full).StartsWith('import-')) {
        throw "Refusing to remove a path outside the import directory: $full"
    }
    if ([IO.Directory]::Exists($full)) { Remove-Item -LiteralPath $full -Recurse -Force }
}

function Read-InstalledGameSnapshot([string]$Repository, [string]$GameDir) {
    $classData = Join-Path $Repository 'a68aff1288d95bd6a58ec6ad99813ea9c59e0abc.tpk'
    if (-not [IO.File]::Exists($classData)) {
        $response = Invoke-RestMethod 'https://api.github.com/repos/nesrak1/UABEA/git/blobs/a68aff1288d95bd6a58ec6ad99813ea9c59e0abc'
        [IO.File]::WriteAllBytes("$classData.tmp", [Convert]::FromBase64String($response.content))
        [IO.File]::Move("$classData.tmp", $classData)
    }
    $temporary = Join-Path $Repository ('import-versions-' + [Guid]::NewGuid().ToString('N'))
    [IO.Directory]::CreateDirectory($temporary) | Out-Null
    try {
        $unity = Join-Path $temporary 'unity.txt'
        $display = Join-Path $temporary 'display.txt'
        $data = Join-Path $GameDir 'Probably Stolen_Data'
        dotnet run --project "$PSScriptRoot/ReadGameVersion" -- "$data/globalgamemanagers" $classData "$data/Managed/Assembly-CSharp.dll" $unity $display | Out-Host
        if ($LASTEXITCODE -ne 0) { throw 'Game version extraction failed.' }
        return [pscustomobject]@{
            'display-version' = [IO.File]::ReadAllText($display).Trim()
            'unity-version' = [IO.File]::ReadAllText($unity).Trim()
            'decompiler-version' = '11.2.0.10050'
        }
    }
    finally { Remove-GameImport $Repository $temporary }
}

function Import-GameSnapshot(
    [string]$Repository, [string]$Libraries, $Snapshot, [switch]$Unsigned,
    [scriptblock]$Decompile = {
        param($Lib, $Src)
        Write-Host 'Decompiling Assembly-CSharp.dll...'
        dotnet tool execute ilspycmd@11.2.0.10050 -- -p --disable-updatecheck -r $Lib -o $Src "$Lib/Assembly-CSharp.dll"
        if ($LASTEXITCODE -ne 0) { throw 'Decompilation failed.' }
    }
) {
    $labels = @($Snapshot.'display-version', $Snapshot.'unity-version' | Select-Object -Unique)
    foreach ($label in $labels) { Invoke-GameGit $Repository @('check-ref-format', "refs/tags/$label") | Out-Null }
    $branch = 'refs/heads/' + $Snapshot.'unity-version'
    $local = Invoke-GameGit $Repository @('rev-parse', '--verify', $branch) -AllowFailure
    $remote = Invoke-GameGit $Repository @('rev-parse', '--verify', ('refs/remotes/origin/' + $Snapshot.'unity-version')) -AllowFailure
    $parent = if ($local) { $local } else { $remote }
    if ($local -and $remote -and $local -ne $remote) {
        if ($null -ne (Invoke-GameGit $Repository @('merge-base', '--is-ancestor', $local, $remote) -AllowFailure)) { $parent = $remote }
        elseif ($null -eq (Invoke-GameGit $Repository @('merge-base', '--is-ancestor', $remote, $local) -AllowFailure)) {
            throw "Local and remote $($Snapshot.'unity-version') histories diverged; reconcile them before importing."
        }
    }

    $stage = Join-Path $Repository ('import-' + [Guid]::NewGuid().ToString('N'))
    [IO.Directory]::CreateDirectory((Join-Path $stage 'lib')) | Out-Null
    $previousIndex = $env:GIT_INDEX_FILE
    $env:GIT_INDEX_FILE = Join-Path $stage 'index'
    $stageArgs = @('--git-dir', $Repository, '--work-tree', $stage)
    try {
        foreach ($dll in [IO.Directory]::EnumerateFiles($Libraries, '*.dll')) {
            [IO.File]::Copy($dll, (Join-Path "$stage/lib" ([IO.Path]::GetFileName($dll))))
        }
        if (-not [IO.File]::Exists("$stage/lib/Assembly-CSharp.dll")) { throw 'The installation is missing Assembly-CSharp.dll.' }
        [IO.File]::WriteAllText("$stage/.gitattributes", "lib/** -text -filter`n")
        Invoke-GameGit $stage ($stageArgs + @('read-tree', '--empty')) | Out-Null
        Invoke-GameGit $stage ($stageArgs + @('add', '--force', '--', '.gitattributes', 'lib')) | Out-Null
        $tree = Invoke-GameGit $stage ($stageArgs + @('write-tree'))
        $libTree = Invoke-GameGit $Repository @('rev-parse', "${tree}:lib")
        $existing = $null
        $candidates = @($parent) + @((Invoke-GameGit $Repository @('rev-list', '--all')) -split "`n")
        foreach ($candidate in ($candidates | Select-Object -Unique)) {
            if ($candidate -and (Invoke-GameGit $Repository @('rev-parse', '--verify', "${candidate}:lib") -AllowFailure) -eq $libTree) {
                $existing = $candidate
                break
            }
        }
        if ($existing) {
            $previous = Read-GameSnapshot (Invoke-GameGit $Repository @('show', "${existing}:snapshot.json"))
            $Snapshot.'decompiler-version' = $previous.'decompiler-version'
            Invoke-GameGit $stage ($stageArgs + @('read-tree', '--prefix=src/', "${existing}:src")) | Out-Null
            Write-Host "Reusing decompiled sources from $existing."
        }
        else {
            & $Decompile "$stage/lib" "$stage/src" | Out-Host
            if (-not [IO.Directory]::Exists("$stage/src") -or -not [IO.Directory]::GetFiles("$stage/src", '*', 'AllDirectories').Length) {
                throw 'Decompiler produced no source files.'
            }
            Invoke-GameGit $stage ($stageArgs + @('add', '--force', '--', 'src')) | Out-Null
        }
        if ($existing -and $previous.'display-version' -ceq $Snapshot.'display-version' -and $previous.'unity-version' -ceq $Snapshot.'unity-version') {
            # Reuse the metadata blob too, preserving its formatting and line endings.
            Invoke-GameGit $stage ($stageArgs + @('restore', '--source', $existing, '--staged', '--', 'snapshot.json')) | Out-Null
        }
        else {
            $json = "{`n" + ((@('display-version', 'unity-version', 'decompiler-version') | ForEach-Object {
                '  "' + $_ + '": ' + (ConvertTo-Json -InputObject $Snapshot.$_ -Compress)
            }) -join ",`n") + "`n}`n"
            [IO.File]::WriteAllText("$stage/snapshot.json", $json)
            Invoke-GameGit $stage ($stageArgs + @('add', '--force', '--', 'snapshot.json')) | Out-Null
        }
        $tree = Invoke-GameGit $stage ($stageArgs + @('write-tree'))
        if ($parent -and (Invoke-GameGit $Repository @('rev-parse', "$parent^{tree}")) -eq $tree) { $commit = $parent }
        else {
            $arguments = @('commit-tree', $tree)
            if (-not $Unsigned -and (Invoke-GameGit $Repository @('config', '--bool', '--get', 'commit.gpgsign') -AllowFailure) -eq 'true') { $arguments += '-S' }
            if ($parent) { $arguments += @('-p', $parent) }
            $arguments += @('-m', "Import $($Snapshot.'display-version') (Unity $($Snapshot.'unity-version'))")
            $commit = Invoke-GameGit $Repository $arguments
        }
        $expected = if ($local) { $local } else { '0' * 40 }
        # Retain the commit on its branch before moving the convenience tags.
        Invoke-GameGit $Repository @('update-ref', $branch, $commit, $expected) | Out-Null
        foreach ($label in $labels) { Invoke-GameGit $Repository @('update-ref', "refs/tags/$label", $commit) | Out-Null }
        return $commit
    }
    finally {
        $env:GIT_INDEX_FILE = $previousIndex
        Remove-GameImport $Repository $stage
    }
}
