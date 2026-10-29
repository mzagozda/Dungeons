#requires -Version 7.0
[CmdletBinding()]
param(
    [string]$GodotPath,
    [Parameter(Mandatory)][ValidatePattern('^[a-z][a-z0-9-]{0,63}$')][string]$Profile,
    [ValidateSet('mobile','gl_compatibility')][string]$Renderer = 'gl_compatibility',
    [string]$BaselineDirectory,
    [string]$EvidenceDirectory
)
. (Join-Path $PSScriptRoot 'Common.ps1')
$godot = Get-GodotPath -ExplicitPath $GodotPath
Assert-GodotVersion $godot
if (!$EvidenceDirectory) { $EvidenceDirectory = Join-Path $script:RepoRoot ('artifacts/nightly/rendered-' + [guid]::NewGuid().ToString('N')) }
if (Test-Path -LiteralPath $EvidenceDirectory) { throw 'Nightly evidence must use a new directory.' }
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
if (!$BaselineDirectory) { $BaselineDirectory = Join-Path $script:RepoRoot "assets/baselines/visual/$Profile" }
$baseline = [IO.Path]::GetFullPath($BaselineDirectory)
if ($evidence.StartsWith($baseline + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or $evidence -eq $baseline) { throw 'Evidence must be outside the reviewed baseline.' }
[void][IO.Directory]::CreateDirectory($evidence)
$summary = [ordered]@{
    capturedUtc=[DateTime]::UtcNow.ToString('o'); profile=$Profile; renderer=$Renderer
    renderedIntegrationPassed=$false; soakDiagnosticPassed=$false; namedScenariosPassed=$false; candidatePrepared=$false
    reviewedBaselinePassed=$false; passed=$false; physicalAcceptance=$false
}
$previousGodot = $env:GODOT_BIN
Push-Location $script:RepoRoot
try {
    # Regenerate the development completion replay with the current source identity.
    # This is not promotion of a reviewed gameplay golden.
    Invoke-Checked dotnet @('restore','Dungeons.sln','--locked-mode') -LogPath (Join-Path $evidence 'restore.log') -Quiet
    Invoke-Checked dotnet @('build','src/Dungeons.Core.Tests','-c','Release','--no-restore','-warnaserror') -LogPath (Join-Path $evidence 'core-build.log') -Quiet
    Invoke-Checked dotnet @('test','src/Dungeons.Core.Tests','-c','Release','--no-build','--filter','FullyQualifiedName~ScriptedPlaythroughTests','--logger','trx','--results-directory',(Join-Path $evidence 'replay')) -LogPath (Join-Path $evidence 'replay.log') -Quiet
    Invoke-Checked dotnet @('build','game/Dungeons.Game.csproj','-c','Debug','--no-restore','-warnaserror') -LogPath (Join-Path $evidence 'game-build.log') -Quiet
    Initialize-GodotEditor -Path $godot
    $summary.knownHeadlessPreviewDiagnostics = Invoke-GodotImport -Path $godot -LogPath (Join-Path $evidence 'import.log')
    $env:GODOT_BIN = $godot
    $backend = if ($Renderer -eq 'mobile') { 'native' } else { 'compatibility' }
    # All scene tests run with an actual display/renderer, including the native-only branches.
    Invoke-Checked dotnet @('test','game/Dungeons.Game.csproj','-c','Debug','--no-build','--settings',"tools/godot-$backend.runsettings",'--logger','trx','--results-directory',(Join-Path $evidence 'gdunit')) -TimeoutSeconds 600 -LogPath (Join-Path $evidence 'gdunit.log') -Quiet
    & (Join-Path $PSScriptRoot 'Assert-TestRun.ps1') -ResultsDirectory (Join-Path $evidence 'gdunit') -MinimumTests 93
    $summary.renderedIntegrationPassed = $true
    & (Join-Path $PSScriptRoot 'Test-SoakScene.ps1') -GodotPath $godot -Renderer $Renderer -EvidenceDirectory (Join-Path $evidence 'soak-diagnostic')
    $summary.soakDiagnosticPassed = $true
    & (Join-Path $PSScriptRoot 'Test-ScreenshotScenarios.ps1') -GodotPath $godot -Renderer $Renderer -Repeat -EvidenceDirectory (Join-Path $evidence 'scenarios')
    $summary.namedScenariosPassed = $true
    Invoke-Checked python @('tools/visual_baseline.py','prepare',(Join-Path $evidence 'scenarios'),(Join-Path $evidence 'candidate'),'--profile',$Profile) -LogPath (Join-Path $evidence 'candidate.log') -Quiet
    $summary.candidatePrepared = $true
    Invoke-Checked python @('tools/make_visual_contact_sheet.py',(Join-Path $evidence 'scenarios'),(Join-Path $evidence 'contact.png')) -LogPath (Join-Path $evidence 'contact.log') -Quiet
    # Missing or pending review returns 3, which deliberately fails this gate.
    Invoke-Checked python @('tools/visual_baseline.py','check',$baseline,(Join-Path $evidence 'scenarios'),'--profile',$Profile,'--output',(Join-Path $evidence 'comparison')) -LogPath (Join-Path $evidence 'comparison.log') -Quiet
    $summary.reviewedBaselinePassed = $true
    $summary.passed = $true
}
catch {
    $summary.failure = $_.Exception.Message
    throw
}
finally {
    $summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence 'result.json') -Encoding utf8
    $env:GODOT_BIN = $previousGodot
    Pop-Location
    Write-Host "Nightly rendered evidence: $evidence (reviewed baseline passed: $($summary.reviewedBaselinePassed))"
}
