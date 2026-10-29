[CmdletBinding()]
param([string]$GodotPath, [switch]$Rendered, [ValidateRange(5, 120)][int]$CaptureSeconds = 15)
. (Join-Path $PSScriptRoot 'Common.ps1')
$godot = Get-GodotPath -ExplicitPath $GodotPath
Assert-GodotVersion $godot
$evidence = Join-Path $script:RepoRoot ('artifacts/m3/gate-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($evidence)
Push-Location $script:RepoRoot
Start-Transcript -Path (Join-Path $evidence 'checks.log') | Out-Null
try {
    & (Join-Path $PSScriptRoot 'Test-Core.ps1')
    foreach ($scriptName in @('check_schemas.py', 'generate_diagnostics.py', 'atlas_pack.py', 'check_clips.py', 'check_imports.py', 'check_editor_icons.py', 'make_audio.py', 'make_ui.py')) {
        $arguments = @("tools/$scriptName")
        if ($scriptName -in @('generate_diagnostics.py', 'atlas_pack.py', 'make_audio.py', 'make_ui.py')) { $arguments += '--check' }
        Invoke-Checked 'python' $arguments
    }
    Invoke-Checked python @('-m','unittest','discover','-s','tools','-p','test_texture_imports.py')
    Invoke-Checked 'dotnet' @('restore', 'game/Dungeons.Game.csproj', '--locked-mode')
    Invoke-Checked 'dotnet' @('build', 'game/Dungeons.Game.csproj', '-c', 'Debug', '--no-restore', '-warnaserror')
    # Preserve raw diagnostics. The adopted host must import without errors.
    $importLog = Join-Path $evidence 'import.log'
    Initialize-GodotEditor -Path $godot
    $knownCount = Invoke-GodotImport -Path $godot -LogPath $importLog
    $env:GODOT_BIN = $godot
    Invoke-Checked 'dotnet' @('test', 'game/Dungeons.Game.csproj', '-c', 'Debug', '--no-build', '--settings', 'tools/godot.runsettings', '--logger', 'trx', '--results-directory', (Join-Path $evidence 'gdunit'))
    & (Join-Path $PSScriptRoot 'Assert-TestRun.ps1') -ResultsDirectory (Join-Path $evidence 'gdunit') -MinimumTests 93
    if ($Rendered) {
        $appLog = Join-Path $evidence 'app.log'
        Invoke-Checked $godot @('--audio-driver', 'Dummy', '--path', 'game', '--', '--level', 'core.mossy_hollow', '--seed', '1', "--screenshot-test=$(Join-Path $evidence 'app.png')") -TimeoutSeconds 45 *> $appLog
        $appText = [IO.File]::ReadAllText($appLog)
        if ($appText -notmatch 'APP_SCREENSHOT=PASS' -or $appText -match '(?m)^(ERROR:|SCRIPT ERROR:)') { throw "App scene check failed: $appLog" }
        foreach ($viewpoint in @(@{name='bridge'; position='16.5,34.5'}, @{name='stairs'; position='16.5,12.5'}, @{name='ruin'; position='16.5,6.5'})) {
            $png = Join-Path $evidence ($viewpoint.name + '.png')
            $log = Join-Path $evidence ($viewpoint.name + '.log')
            Invoke-Checked $godot @('--audio-driver', 'Dummy', '--path', 'game', 'res://scenes/m3_preview.tscn', '--', "--m3-screenshot=$png", "--m3-location=$($viewpoint.position)", "--perf-capture=$CaptureSeconds", '--perf-uncapped') -TimeoutSeconds ($CaptureSeconds + 30) *> $log
            $text = [IO.File]::ReadAllText($log)
            if ($text -notmatch 'M3_PREVIEW=PASS' -or $text -match '(?m)^(ERROR:|SCRIPT ERROR:)') { throw "Rendered M3 check failed: $log" }
            Invoke-Checked 'python' @('tools/summarize_perf.py', ($png + '.csv'), '--output', (Join-Path $evidence ($viewpoint.name + '-perf.json')))
        }
    }
    [ordered]@{ capturedUtc=[DateTime]::UtcNow.ToString('o'); rendered=$Rendered.IsPresent; localGatePassed=$Rendered.IsPresent; knownHeadlessPreviewDiagnostics=$knownCount; deviceEvidenceTrackedSeparately=$true; referenceDevicesPending=$true; fullPlatformAcceptance=$false } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'result.json') -Encoding UTF8
    Write-Host "M3 local checks passed; visual/reference-device acceptance remains separate. Evidence: $evidence"
}
finally { Stop-Transcript | Out-Null; Pop-Location }
