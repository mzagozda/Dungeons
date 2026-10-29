#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$EvidenceDirectory,
    [string]$BaselineDirectory,
    [string]$LevelPath
)
. (Join-Path $PSScriptRoot 'Common.ps1')
if (!$BaselineDirectory) { $BaselineDirectory = Join-Path $script:RepoRoot 'assets/baselines/replays/mossy-hollow-v1' }
if (!$LevelPath) { $LevelPath = Join-Path $script:RepoRoot 'game/levels/mossy_hollow/level.json' }
$baseline = [IO.Path]::GetFullPath($BaselineDirectory); $evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
if ((Test-Path $evidence) -or $evidence.StartsWith($baseline + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or $evidence -eq $baseline) { throw 'Replay golden evidence must be new and outside the baseline.' }
[void][IO.Directory]::CreateDirectory($evidence)
$summary = [ordered]@{passed=$false;reviewedBytesPassed=$false;simulationVerified=$false;physicalAcceptance=$false}
Push-Location $script:RepoRoot
try {
    Invoke-Checked python @('tools/replay_baseline.py',$baseline,'--output',(Join-Path $evidence 'identity.json')) -LogPath (Join-Path $evidence 'identity.log') -Quiet
    $identity = Get-Content (Join-Path $evidence 'identity.json') -Raw | ConvertFrom-Json
    $summary.reviewedBytesPassed = $true
    foreach ($case in $identity.cases) {
        $log = Join-Path $evidence ($case.name + '.log')
        Invoke-Checked dotnet @('run','--project','src/Dungeons.Tools','-c','Release','--no-build','--','replay',$LevelPath,(Join-Path $baseline $case.name),'--content','game/content') -TimeoutSeconds 120 -LogPath $log -Quiet
        if (![IO.File]::ReadAllText($log).Contains("Replay verified. tick=0 expected=$($case.finalHash) actual=$($case.finalHash)")) { throw 'Core did not verify the reviewed final identity.' }
    }
    # Verify immutable inputs again after the actual current Core consumes them.
    Invoke-Checked python @('tools/replay_baseline.py',$baseline,'--output',(Join-Path $evidence 'identity-after.json')) -LogPath (Join-Path $evidence 'identity-after.log') -Quiet
    $summary.simulationVerified = $true; $summary.passed = $true
}
catch { $summary.failure = $_.Exception.Message; throw }
finally { $summary | ConvertTo-Json | Set-Content (Join-Path $evidence 'result.json'); Pop-Location }
Write-Host "Reviewed replay bytes and all three actual Core playbacks pass: $evidence"
