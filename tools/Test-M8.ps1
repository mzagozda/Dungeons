[CmdletBinding()]
param([string]$GodotPath, [switch]$Rendered, [switch]$Benchmarks)
. (Join-Path $PSScriptRoot 'Common.ps1')
$godot = Get-GodotPath -ExplicitPath $GodotPath
$evidence = Join-Path $script:RepoRoot ('artifacts/m8/gate-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($evidence)
Push-Location $script:RepoRoot
try {
    & (Join-Path $PSScriptRoot 'Test-M7.ps1') -GodotPath $godot -Rendered:$Rendered -Benchmarks:$Benchmarks
    Invoke-Checked python @('-m','unittest','discover','-s','tools','-p','test_compare_images.py')
    Invoke-Checked python @('-m','unittest','discover','-s','tools','-p','test_visual_baseline.py')
    Invoke-Checked python @('-m','unittest','discover','-s','tools','-p','test_benchmark_baseline.py')
    Invoke-Checked python @('-m','unittest','discover','-s','tools','-p','test_android_release.py')
    Invoke-Checked python @('-m','unittest','discover','-s','tools','-p','test_android_splits.py')
    Invoke-Checked python @('tools/store_metadata.py','check')
    Invoke-Checked python @('-m','unittest','discover','-s','tools','-p','test_store_metadata.py')
    Invoke-Checked python @('-m','unittest','discover','-s','tools','-p','test_ios_preset.py')
    Invoke-Checked python @('-m','unittest','discover','-s','tools','-p','test_ios_archive.py')
    Invoke-Checked python @('-m','unittest','discover','-s','tools','-p','test_ios_signing.py')
    Invoke-Checked python @('-m','unittest','discover','-s','tools','-p','test_replay_baseline.py')
    & (Join-Path $PSScriptRoot 'Test-AotCompatibility.ps1') -EvidenceDirectory (Join-Path $evidence 'aot-compatibility')
    & (Join-Path $PSScriptRoot 'Test-SimulationFuzz.ps1') -EvidenceDirectory (Join-Path $evidence 'simulation-fuzz')
    & (Join-Path $PSScriptRoot 'Test-LaunchCommands.ps1') -GodotPath $godot -EvidenceDirectory (Join-Path $evidence 'launch') -Rendered:$Rendered
    if ($Rendered) {
        & (Join-Path $PSScriptRoot 'Test-ScreenshotScenarios.ps1') -GodotPath $godot -EvidenceDirectory (Join-Path $evidence 'scenarios') -Repeat
        foreach ($capture in @('desktop-video','phone-video','phone-audio','phone-controls','phone-layout','phone-bindings','phone-accessibility','phone-hud')) {
            $mode = $capture.Substring($capture.IndexOf('-') + 1)
            $focus = if ($mode -in @('layout','bindings')) { $mode } else { $null }
            if ($focus) { $mode = 'controls' }
            $png = Join-Path $evidence ($capture + '.png'); $log = Join-Path $evidence ($capture + '.log')
            $arguments = @('--audio-driver','Dummy','--path','game','--resolution','1760x800','res://scenes/m8_settings_preview.tscn','--',"--settings-screenshot=$png","--settings-mode=$mode")
            if ($capture.StartsWith('phone-')) { $arguments += @('--settings-phone','--settings-ui=1.5','--settings-text=1.5') }
            if ($focus) { $arguments += "--settings-focus=$focus" }
            Invoke-Checked $godot $arguments -TimeoutSeconds 45 -LogPath $log -RejectEngineErrors -Quiet
            if ([IO.File]::ReadAllText($log) -notmatch 'M8_SETTINGS_PREVIEW=PASS' -or !(Test-Path -LiteralPath $png)) { throw "Missing settings capture: $capture" }
        }
        $env:GODOT_BIN = $godot
        foreach ($backend in @('native','compatibility')) {
            Invoke-Checked dotnet @('test','game/Dungeons.Game.csproj','-c','Debug','--no-build','--filter','FullyQualifiedName~BootFlowTests|FullyQualifiedName~UiThemeTests|FullyQualifiedName~LevelBrowserTests|FullyQualifiedName~HudFeedbackTests|FullyQualifiedName~MissionHudTests|FullyQualifiedName~LaunchCommandTests|FullyQualifiedName~ScreenshotFixtureTests|FullyQualifiedName~MobileInputTests|FullyQualifiedName~MissionFlowTests','--settings',"tools/godot-$backend.runsettings",'--logger','trx','--results-directory',(Join-Path $evidence ($backend + '-ui')))
            & (Join-Path $PSScriptRoot 'Assert-TestRun.ps1') -ResultsDirectory (Join-Path $evidence ($backend + '-ui')) -MinimumTests 45
            $soakRenderer = if ($backend -eq 'native') { 'mobile' } else { 'gl_compatibility' }
            & (Join-Path $PSScriptRoot 'Test-SoakScene.ps1') -GodotPath $godot -Renderer $soakRenderer -EvidenceDirectory (Join-Path $evidence ($backend + '-soak-diagnostic'))
        }
    }
    [ordered]@{ capturedUtc=[DateTime]::UtcNow.ToString('o'); rendered=$Rendered.IsPresent; localSettingsChecksPassed=$true; localAudioChecksPassed=$true; soakDiagnosticChecksPassed=$Rendered.IsPresent; twoHourSoakAcceptance=$false; physicalSettingsAndInputAcceptancePending=$true; audioListeningAndDeviceAcceptancePending=$true; remainingPolishAndHardeningPending=$true; fullMilestoneAcceptance=$false } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'result.json') -Encoding UTF8
    Write-Host "M8 local settings/audio checks passed; remaining polish/hardening and platform acceptance are pending. Evidence: $evidence"
}
finally { Pop-Location }
