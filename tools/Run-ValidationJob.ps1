#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$RequestFile)
$ErrorActionPreference = 'Stop'
$request = Get-Content -LiteralPath $RequestFile -Raw | ConvertFrom-Json
$jobRoot = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($RequestFile))
. (Join-Path $request.repoRoot 'tools/Common.ps1')
$status = [ordered]@{job=$request.kind;passed=$false;fullMilestoneAcceptance=$false;processId=$PID;startedUtc=[DateTime]::UtcNow.ToString('o')}
[ordered]@{processId=$PID;startUtc=(Get-Process -Id $PID).StartTime.ToUniversalTime().ToString('o');sessionId=(Get-Process -Id $PID).SessionId;requestFile=[IO.Path]::GetFullPath($RequestFile)} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $jobRoot 'running.json') -Encoding utf8
$transcript = $false
try {
    Start-Transcript -Path (Join-Path $jobRoot 'console.log') -NoClobber | Out-Null; $transcript = $true
    Set-Location -LiteralPath $request.repoRoot
    switch ($request.kind) {
        'DesktopSoak' {
            $parameters = @{Executable=$request.executable;ReplayDirectory=$request.replays;Seconds=[int]$request.seconds;Renderer=$request.renderer;Fast=[bool]$request.fast;EvidenceDirectory=$request.evidence}
            & (Join-Path $script:RepoRoot 'tools/Test-DesktopSoak.ps1') @parameters
        }
        'M9Rendered' { & (Join-Path $script:RepoRoot 'tools/Test-M9.ps1') -Rendered }
        'Benchmarks' {
            $parameters = @{Runner=$request.benchmarkRunner;EvidenceDirectory=$request.evidence}
            if ($request.PSObject.Properties.Name -contains 'benchmarkBaselineFile' -and $request.benchmarkBaselineFile) { $parameters.BaselineFile = $request.benchmarkBaselineFile }
            & (Join-Path $script:RepoRoot 'tools/Test-NightlyBenchmarks.ps1') @parameters
        }
        default { throw 'Unknown detached validation job kind.' }
    }
    $status.passed = $true
}
catch { $status.failure = $_.Exception.Message; Write-Host ('VALIDATION_JOB=FAIL ' + $_.Exception.Message) }
finally {
    $status.finishedUtc = [DateTime]::UtcNow.ToString('o')
    $status | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $jobRoot 'result.json') -Encoding utf8
    if ($transcript) { Stop-Transcript | Out-Null }
}
if (!$status.passed) { exit 1 }
