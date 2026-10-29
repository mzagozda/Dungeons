#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[a-z][a-z0-9-]{0,63}$')][string]$Runner,
    [string]$BaselineFile,
    [string]$EvidenceDirectory
)
. (Join-Path $PSScriptRoot 'Common.ps1')
if (!$EvidenceDirectory) { $EvidenceDirectory = Join-Path $script:RepoRoot ('artifacts/nightly/benchmarks-' + [guid]::NewGuid().ToString('N')) }
if (Test-Path -LiteralPath $EvidenceDirectory) { throw 'Benchmark evidence must use a new directory.' }
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
if (!$BaselineFile) { $BaselineFile = Join-Path $script:RepoRoot "assets/baselines/performance/$Runner.json" }
$baseline = [IO.Path]::GetFullPath($BaselineFile)
if ($evidence -eq $baseline -or $baseline.StartsWith($evidence + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'The stored baseline must be outside the evidence directory.' }
[void][IO.Directory]::CreateDirectory($evidence)
$summary = [ordered]@{
    capturedUtc=[DateTime]::UtcNow.ToString('o'); runner=$Runner; coreGatePassed=$false
    fixtureContractsPassed=$false; benchmarksExecuted=$false; candidateRecorded=$false
    regressionPassed=$false; passed=$false; physicalAcceptance=$false
}
Push-Location $script:RepoRoot
try {
    Invoke-Checked dotnet @('restore','Dungeons.sln','--locked-mode') -LogPath (Join-Path $evidence 'restore.log') -Quiet
    # Includes real zero-allocation tick checks and the 1 ms queued path-service budget.
    # Measurement, allocation and instrumented coverage checks stay separate.
    Invoke-Checked pwsh @('-NoProfile','-File',(Join-Path $PSScriptRoot 'Test-Core.ps1')) -TimeoutSeconds 600 -LogPath (Join-Path $evidence 'core.log') -Quiet
    $summary.coreGatePassed = $true
    Invoke-Checked python @('-m','unittest','discover','-s','tools','-p','test_benchmark_baseline.py') -LogPath (Join-Path $evidence 'comparator-tests.log') -Quiet
    $contracts = Join-Path $evidence 'contracts.json'
    Invoke-Checked dotnet @('run','--project','src/Dungeons.Benchmarks','-c','Release','--no-build','--','--nightly-contracts') -LogPath $contracts -Quiet
    $summary.fixtureContractsPassed = $true
    # BenchmarkDotNet discovers the matching csproj recursively from its working
    # directory. Keep preserved workspaces/source archives outside that search.
    Push-Location (Join-Path $script:RepoRoot 'src/Dungeons.Benchmarks')
    try {
        Invoke-Checked dotnet @('run','--project','Dungeons.Benchmarks.csproj','-c','Release','--no-build','--','--filter','*NightlyBenchmarks*','--job','short','--launchCount','3','--warmupCount','12','--iterationCount','8','--outliers','DontRemove','--exporters','json','--artifacts',(Join-Path $evidence 'bdn')) -TimeoutSeconds 900 -LogPath (Join-Path $evidence 'benchmarks.log') -Quiet
    }
    finally { Pop-Location }
    $summary.benchmarksExecuted = $true
    $reports = Join-Path $evidence 'bdn/results'
    Invoke-Checked python @('tools/benchmark_baseline.py','record',$reports,$contracts,'--runner',$Runner,'--output',(Join-Path $evidence 'candidate.json')) -LogPath (Join-Path $evidence 'candidate.log') -Quiet
    $summary.candidateRecorded = $true
    # A missing per-runner reference fails with 3 after preserving real measurements.
    # The candidate never overwrites or automatically advances the stored baseline.
    Invoke-Checked python @('tools/benchmark_baseline.py','check',$reports,$contracts,'--runner',$Runner,'--baseline',$baseline,'--output',(Join-Path $evidence 'comparison.json')) -LogPath (Join-Path $evidence 'comparison.log') -Quiet
    $summary.regressionPassed = $true
    $summary.passed = $true
}
catch { $summary.failure = $_.Exception.Message; throw }
finally {
    $summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence 'result.json') -Encoding utf8
    Pop-Location
    Write-Host "Nightly benchmark evidence: $evidence (regression passed: $($summary.regressionPassed))"
}
