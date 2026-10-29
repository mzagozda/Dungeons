#requires -Version 7.0
[CmdletBinding()]
param(
    [string]$GodotPath,
    [ValidateSet('mobile','gl_compatibility')][string]$Renderer = 'mobile',
    [string]$EvidenceDirectory
)
. (Join-Path $PSScriptRoot 'Common.ps1')
$godot = Get-GodotPath -ExplicitPath $GodotPath
if (!$EvidenceDirectory) { $EvidenceDirectory = Join-Path $script:RepoRoot ('artifacts/soak/scene-' + [guid]::NewGuid().ToString('N')) }
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
if (Test-Path -LiteralPath $evidence) { throw 'Soak scene diagnostic evidence must use a new directory.' }
[void][IO.Directory]::CreateDirectory($evidence)
$summary = [ordered]@{passed=$false;acceleratedDiagnostic=$true;soakAcceptance=$false;renderer=$Renderer;physicalMobileAcceptance=$false}
Push-Location $script:RepoRoot
try {
    $run = Join-Path $evidence 'run'; $replays = Join-Path $script:RepoRoot 'artifacts/m6/candidates'
    Invoke-Checked $godot @('--audio-driver','Dummy','--rendering-method',$Renderer,'--path','game','--resolution','1280x720','--','--qa-mode=soak',"--soak-output=$run","--soak-replays=$replays",'--soak-seconds=10','--soak-fast') -TimeoutSeconds 180 -LogPath (Join-Path $evidence 'engine.log') -RejectEngineErrors -Quiet
    $log = [IO.File]::ReadAllText((Join-Path $evidence 'engine.log'))
    $result = Get-Content -LiteralPath (Join-Path $run 'result.json') -Raw | ConvertFrom-Json
    $identity = Get-Content -LiteralPath (Join-Path $run 'identity.json') -Raw | ConvertFrom-Json
    $attempt = Get-Content -LiteralPath (Join-Path $run 'attempt-0001.json') -Raw | ConvertFrom-Json
    if (!$result.passed -or !$result.diagnostic -or $result.soakAcceptance -or $result.progress.FullClears -lt 1 -or $result.progress.Ambushes -lt 1 -or $result.progress.Respawns -lt 1 -or $result.progress.NormalAttempts -lt 1 -or $result.progress.RenderedFrames -lt 1 -or !$attempt.verified) { throw 'Incomplete or incorrectly labelled native soak diagnostic.' }
    if ($identity.display -eq 'headless' -or $identity.renderer -ne $Renderer -or $log -notmatch '(?m)^SOAK_DIAGNOSTIC=PASS ' -or $log -match $script:NativeLeakPattern) { throw 'Soak diagnostic lacks the requested native renderer or reports a shutdown leak.' }
    $summary.progress = $result.progress; $summary.normalReplayVerified = $true; $summary.passed = $true
}
catch { $summary.failure = $_.Exception.Message; throw }
finally {
    $summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence 'result.json') -Encoding utf8
    Pop-Location
    Write-Host "Native soak scene diagnostic: $evidence (passed: $($summary.passed); two-hour acceptance: false)"
}
