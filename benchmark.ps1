param(
    [string]$Output = 'artifacts/solver-benchmark',
    [int]$Rounds = 3,
    [int]$Tiny = 60,
    [int]$Satchel = 12,
    [int]$Buffer = 12,
    [int]$Crowded = 12,
    [int]$Seed = 1729,
    [int]$Seconds = 10,
    [switch]$FuzzOnly
)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    # Do not overlap even the build with an active render, test, or other CPU-heavy work.
    $quiet = 0
    while ($quiet -lt 2) {
        $before = @{}
        Get-Process | ForEach-Object { if ($null -ne $_.CPU) { $before[$_.Id] = $_.CPU } }
        $timer = [Diagnostics.Stopwatch]::StartNew()
        Start-Sleep -Seconds 1
        $usage = @(Get-Process | Where-Object { $_.Id -ne $PID -and $_.Id -ne 0 -and $before.ContainsKey($_.Id) } |
            ForEach-Object { [pscustomobject]@{ Name = $_.ProcessName; Cores = [Math]::Max(0, $_.CPU - $before[$_.Id]) / $timer.Elapsed.TotalSeconds } })
        $heavy = @($usage | Where-Object { $_.Cores -ge $(if ($_.Name -in @('Code', 'Paseo')) { 2 } else { 0.5 }) })
        if ($heavy.Count -eq 0 -and ($usage | Measure-Object Cores -Sum).Sum -lt [Math]::Max(2, [Environment]::ProcessorCount * 0.1)) { $quiet++; continue }
        Write-Host ('Waiting before build: ' + (($heavy | ForEach-Object { '{0} ({1:F1} cores)' -f $_.Name, $_.Cores }) -join ', '))
        $quiet = 0
        Start-Sleep -Seconds 5
    }
    dotnet build src/Tuck.PackSolver.Benchmarks -c Release --nologo -m:1 /nodeReuse:false
    if ($LASTEXITCODE -ne 0) { throw 'Benchmark build failed.' }
    $runArgs = @('src/Tuck.PackSolver.Benchmarks/bin/Release/net10.0/Tuck.PackSolver.Benchmarks.dll',
        '--output', $Output, '--rounds', $Rounds, '--tiny', $Tiny, '--satchel', $Satchel, '--buffer', $Buffer, '--crowded', $Crowded, '--seed', $Seed, '--seconds', $Seconds)
    if ($FuzzOnly) { $runArgs += '--fuzz-only' }
    dotnet @runArgs
    if ($LASTEXITCODE -ne 0) { throw 'Benchmark failed; inspect failure.txt in the output directory.' }
}
finally { Pop-Location }
