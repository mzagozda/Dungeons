[CmdletBinding()]
param([string]$GodotPath, [switch]$Rendered, [switch]$Benchmarks)
. (Join-Path $PSScriptRoot 'Common.ps1')
$godot = Get-GodotPath -ExplicitPath $GodotPath
$evidence = Join-Path $script:RepoRoot ('artifacts/m5/gate-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($evidence)
Push-Location $script:RepoRoot
try {
    & (Join-Path $PSScriptRoot 'Test-M4.ps1') -GodotPath $godot -Rendered:$Rendered
    if ($Rendered) {
        $png = Join-Path $evidence 'combat.png'
        $log = Join-Path $evidence 'combat.log'
        Invoke-Checked $godot @('--audio-driver','Dummy','--path','game','res://scenes/m5_preview.tscn','--',"--m5-screenshot=$png") -TimeoutSeconds 45 -LogPath $log -RejectEngineErrors -Quiet
        if ([IO.File]::ReadAllText($log) -notmatch 'M5_PREVIEW=PASS' -or !(Test-Path -LiteralPath $png)) { throw 'Missing M5 combat capture.' }
        $env:GODOT_BIN = $godot
        foreach ($backend in @('native','compatibility')) {
            Invoke-Checked dotnet @('test','game/Dungeons.Game.csproj','-c','Debug','--no-build','--filter','FullyQualifiedName~AmbientPresentationTests|FullyQualifiedName~PickupPresentationTests','--settings',"tools/godot-$backend.runsettings",'--logger','trx','--results-directory',(Join-Path $evidence $backend))
            & (Join-Path $PSScriptRoot 'Assert-TestRun.ps1') -ResultsDirectory (Join-Path $evidence $backend) -MinimumTests 4
        }
    }
    if ($Benchmarks) {
        Push-Location (Join-Path $script:RepoRoot 'src/Dungeons.Benchmarks')
        try { Invoke-Checked 'dotnet' @('run','--project','Dungeons.Benchmarks.csproj','-c','Release','--','--filter','*CombatBenchmarks*','--job','short','--exporters','json','--artifacts',(Join-Path $evidence 'benchmarks')) }
        finally { Pop-Location }
    }
    [ordered]@{ capturedUtc=[DateTime]::UtcNow.ToString('o'); rendered=$Rendered.IsPresent; benchmarks=$Benchmarks.IsPresent; runtimeChecksPassed=$true; fullClearGoldenPending=$true; remainingPresentationPending=$true; requiredPlatformChecksPending=$true; milestoneAccepted=$false } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'result.json') -Encoding UTF8
    Write-Host "M5 runtime checks passed; golden/presentation/platform acceptance remains pending. Evidence: $evidence"
}
finally { Pop-Location }
