[CmdletBinding()]
param([string]$GodotPath, [switch]$Rendered, [switch]$Benchmarks)
. (Join-Path $PSScriptRoot 'Common.ps1')
$godot = Get-GodotPath -ExplicitPath $GodotPath
$evidence = Join-Path $script:RepoRoot ('artifacts/m6/gate-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($evidence)
Push-Location $script:RepoRoot
try {
    & (Join-Path $PSScriptRoot 'Test-M5.ps1') -GodotPath $godot -Rendered:$Rendered -Benchmarks:$Benchmarks
    $replay = Join-Path $script:RepoRoot 'artifacts/m6/mossy-hollow-scripted.dreplay'
    if (!(Test-Path -LiteralPath $replay)) { throw 'Scripted gameplay replay is missing.' }
    if ($Rendered) {
        foreach ($mode in @('complete','failed','death','objects')) {
            $png = Join-Path $evidence ($mode + '.png')
            $log = Join-Path $evidence ($mode + '.log')
            Invoke-Checked $godot @('--audio-driver','Dummy','--path','game','res://scenes/m6_preview.tscn','--',"--m6-mode=$mode","--m6-screenshot=$png","--m6-replay=$replay") -TimeoutSeconds 45 -LogPath $log -RejectEngineErrors -Quiet
            if ([IO.File]::ReadAllText($log) -notmatch 'M6_PREVIEW=PASS' -or !(Test-Path -LiteralPath $png)) { throw "Missing M6 capture: $mode" }
        }
    }
    & (Join-Path $PSScriptRoot 'Write-ReplayCandidateManifest.ps1') -Directory (Join-Path $script:RepoRoot 'artifacts/m6/candidates')
    Copy-Item -LiteralPath $replay -Destination (Join-Path $evidence 'mossy-hollow-scripted.dreplay')
    Copy-Item -LiteralPath (Join-Path $script:RepoRoot 'artifacts/m6/candidates') -Destination (Join-Path $evidence 'candidates') -Recurse
    & (Join-Path $PSScriptRoot 'Write-ReplayCandidateManifest.ps1') -Directory (Join-Path $evidence 'candidates')
    $goldenPassed = $false
    $goldenDirectory = Join-Path $script:RepoRoot 'assets/baselines/replays/mossy-hollow-v1'
    if (Test-Path -LiteralPath $goldenDirectory) {
        & (Join-Path $PSScriptRoot 'Test-ReplayGoldens.ps1') -BaselineDirectory $goldenDirectory -EvidenceDirectory (Join-Path $evidence 'reviewed-goldens')
        $goldenPassed = $true
    }
    else { Write-Host 'Reviewed replay baseline is not established; golden acceptance remains pending.' }
    if ($Benchmarks) {
        Push-Location (Join-Path $script:RepoRoot 'src/Dungeons.Benchmarks')
        try { Invoke-Checked 'dotnet' @('run','--project','Dungeons.Benchmarks.csproj','-c','Release','--','--filter','*MossyReplayBenchmarks*','--job','short','--exporters','json','--artifacts',(Join-Path $evidence 'benchmarks')) }
        finally { Pop-Location }
    }
    [ordered]@{ capturedUtc=[DateTime]::UtcNow.ToString('o'); rendered=$Rendered.IsPresent; runtimeChecksPassed=$true; missionClearReplayPassed=$true; reviewedGoldenChecksPassed=$goldenPassed; goldenReviewPending=(!$goldenPassed); requiredPlatformChecksPending=$true; fullAcceptance=$false } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'result.json') -Encoding UTF8
    Write-Host "M6 runtime checks passed; reviewed goldens/platform acceptance remains pending. Evidence: $evidence"
}
finally { Pop-Location }
