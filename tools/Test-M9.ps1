[CmdletBinding()]
param([string]$GodotPath, [switch]$Rendered, [switch]$Benchmarks)
. (Join-Path $PSScriptRoot 'Common.ps1')
$godot = Get-GodotPath -ExplicitPath $GodotPath
$evidence = Join-Path $script:RepoRoot ('artifacts/m9/gate-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($evidence)
Push-Location $script:RepoRoot
try {
    & (Join-Path $PSScriptRoot 'Test-M8.ps1') -GodotPath $godot -Rendered:$Rendered -Benchmarks:$Benchmarks
    foreach ($schema in @('triggers','editor')) {
        Invoke-Checked dotnet @('run','--project','src/Dungeons.Tools','-c','Release','--no-build','--','schema',"--$schema") -LogPath (Join-Path $evidence ($schema + '.json')) -Quiet
        [void]([IO.File]::ReadAllText((Join-Path $evidence ($schema + '.json'))) | ConvertFrom-Json)
    }
    Invoke-Checked python @('tools/check_editor_icons.py','--catalog',(Join-Path $evidence 'editor.json'),'--contact-sheet',(Join-Path $evidence 'palette.png'))
    & (Join-Path $PSScriptRoot 'Test-EditorPalette.ps1') -EvidenceDirectory (Join-Path $evidence 'palette-cli')
    if ($Rendered) {
        $env:GODOT_BIN = $godot
        Invoke-Checked dotnet @('test','game/Dungeons.Game.csproj','-c','Debug','--no-build','--filter','FullyQualifiedName~EditorPreviewTests|FullyQualifiedName~LevelReloadTests','--settings','tools/godot-native.runsettings','--logger','trx','--results-directory',(Join-Path $evidence 'native-editor'))
        & (Join-Path $PSScriptRoot 'Assert-TestRun.ps1') -ResultsDirectory (Join-Path $evidence 'native-editor') -MinimumTests 9
        Invoke-Checked dotnet @('test','game/Dungeons.Game.csproj','-c','Debug','--no-build','--filter','FullyQualifiedName~EditorPreviewTests|FullyQualifiedName~LevelReloadTests','--settings','tools/godot-compatibility.runsettings','--logger','trx','--results-directory',(Join-Path $evidence 'compatibility-editor'))
        & (Join-Path $PSScriptRoot 'Assert-TestRun.ps1') -ResultsDirectory (Join-Path $evidence 'compatibility-editor') -MinimumTests 9
        foreach ($renderer in @('mobile','gl_compatibility')) {
            $png = Join-Path $evidence ($renderer + '.png'); $log = Join-Path $evidence ($renderer + '.log')
            Invoke-Checked $godot @('--audio-driver','Dummy','--rendering-method',$renderer,'--path','game','--resolution','1280x720','res://scenes/m3_preview.tscn','--','--editor-preview',"--m3-screenshot=$png",'--m3-location=16.5,34.5','--perf-capture=5','--perf-uncapped') -TimeoutSeconds 60 -LogPath $log -RejectEngineErrors -Quiet
            if ([IO.File]::ReadAllText($log) -notmatch 'M3_PREVIEW=PASS.*preview=True' -or !(Test-Path -LiteralPath $png)) { throw "Missing session-free preview acceptance: $renderer" }
            Invoke-Checked python @('tools/summarize_perf.py',($png + '.csv'),'--output',(Join-Path $evidence ($renderer + '-perf.json')))
        }
        & (Join-Path $PSScriptRoot 'Test-Thumbnail.ps1') -EvidenceDirectory (Join-Path $evidence 'thumbnails')
    }
    [ordered]@{
        capturedUtc=[DateTime]::UtcNow.ToString('o'); rendered=$Rendered.IsPresent; implementedAuthoringApiChecksPassed=$true
        sessionFreePreviewChecksPassed=$true; nativePreviewChecksPassed=$Rendered.IsPresent
        thumbnailChecksPassed=$Rendered.IsPresent
        nativeReloadChecksPassed=$Rendered.IsPresent
        authoredPaletteChecksPassed=$true
        remaining=@('earlier platform and milestone acceptance')
        fullMilestoneAcceptance=$false
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $evidence 'result.json') -Encoding UTF8
    Write-Host "M9 local editor-readiness checks passed; required earlier milestone/platform acceptance remains pending. Evidence: $evidence"
}
finally { Pop-Location }
