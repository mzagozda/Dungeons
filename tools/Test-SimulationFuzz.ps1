#requires -Version 7.0
[CmdletBinding()]
param(
    [ValidateRange(2,128)][int]$Cases = 32,
    [ValidateRange(1,100000)][int]$Ticks = 10000,
    [UInt64]$Seed = 23443019308292698,
    [string]$EvidenceDirectory
)
. (Join-Path $PSScriptRoot 'Common.ps1')
if (!$EvidenceDirectory) { $EvidenceDirectory = Join-Path $script:RepoRoot ('artifacts/simulation-fuzz/gate-' + [guid]::NewGuid().ToString('N')) }
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
if (Test-Path -LiteralPath $evidence) { throw 'Simulation fuzz evidence must use a new directory.' }
[void][IO.Directory]::CreateDirectory($evidence)
$summary = [ordered]@{ passed=$false; cases=$Cases; ticksPerRun=$Ticks; suiteSeed=$Seed; simulationFuzzAcceptance=$false; physicalAcceptance=$false; soakAcceptance=$false }
Push-Location $script:RepoRoot
try {
    Invoke-Checked dotnet @('restore','src/Dungeons.Tools','--locked-mode') -LogPath (Join-Path $evidence 'restore.log') -Quiet
    # An isolated published pair prevents subsequent builds replacing the fuzzed Core.
    $bin = Join-Path $evidence 'bin'
    # Keep the normal apphost enabled: disabling it also removes the original bin
    # executable, which downstream dotnet run --no-build commands still require.
    Invoke-Checked dotnet @('publish','src/Dungeons.Tools','-c','Release','--no-restore','-o',$bin,'-warnaserror') -LogPath (Join-Path $evidence 'publish.log') -Quiet
    Copy-Item -LiteralPath (Join-Path $script:RepoRoot 'game/content') -Destination (Join-Path $evidence 'content') -Recurse
    $summary.coreSha256 = (Get-FileHash -LiteralPath (Join-Path $bin 'Dungeons.Core.dll')).Hash
    $summary.toolsSha256 = (Get-FileHash -LiteralPath (Join-Path $bin 'Dungeons.Tools.dll')).Hash
    $arguments = @((Join-Path $bin 'Dungeons.Tools.dll'),'sim-fuzz','game/levels/mossy_hollow/level.json',(Join-Path $evidence 'cases'),$Cases.ToString([Globalization.CultureInfo]::InvariantCulture),$Ticks.ToString([Globalization.CultureInfo]::InvariantCulture),$Seed.ToString([Globalization.CultureInfo]::InvariantCulture))
    Invoke-Checked dotnet $arguments -TimeoutSeconds 600 -LogPath (Join-Path $evidence 'fuzz.log') -Quiet
    $result = Get-Content -LiteralPath (Join-Path $evidence 'cases/result.json') -Raw | ConvertFrom-Json
    if (!$result.passed -or $result.cases.Count -ne $Cases -or $result.actualTicksAcrossBothRuns -ne ([long]$Cases * $Ticks * 2)) { throw 'Incomplete simulation fuzz result.' }
    $summary.simulationFuzzAcceptance = $result.simulationFuzzAcceptance
    $summary.actualTicksAcrossBothRuns = $result.actualTicksAcrossBothRuns
    $summary.entityChecks = ($result.cases | Measure-Object -Property EntityChecks -Sum).Sum
    $summary.dynamicGateCases = @($result.cases | Where-Object { $_.Kind -eq 'generated-gates' }).Count
    $summary.gateClosuresPerFirstRun = ($result.cases | Measure-Object -Property GateClosures -Sum).Sum
    & (Join-Path $PSScriptRoot 'Test-SimulationFuzzCli.ps1') -ToolsBinary (Join-Path $bin 'Dungeons.Tools.dll') -EvidenceDirectory (Join-Path $evidence 'cli-contracts')
    $summary.cliContractsPassed = $true
    $summary.passed = $true
}
catch { $summary.failure = $_.Exception.Message; throw }
finally {
    $summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence 'result.json') -Encoding utf8
    Pop-Location
    Write-Host "Simulation fuzz evidence: $evidence (passed: $($summary.passed), full simulation fuzz acceptance: $($summary.simulationFuzzAcceptance))"
}
