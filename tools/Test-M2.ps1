[CmdletBinding()]
param([ValidateRange(1, 3600)][int]$FuzzSeconds = 600)
. (Join-Path $PSScriptRoot 'Common.ps1')
& (Join-Path $PSScriptRoot 'Test-Core.ps1')
$m2Evidence = Join-Path $script:RepoRoot ('artifacts/m2/gate-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($m2Evidence)
Push-Location $script:RepoRoot
Start-Transcript -Path (Join-Path $m2Evidence 'transcript.log') -Force | Out-Null
try {
    $m2Cli = Join-Path $script:RepoRoot 'src/Dungeons.Tools/bin/Release/net10.0/Dungeons.Tools.dll'
    Invoke-Checked -FilePath 'python' -Arguments @('tools/check_schemas.py')
    Invoke-Checked -FilePath 'python' -Arguments @('tools/generate_diagnostics.py', '--check')
    Invoke-Checked -FilePath 'dotnet' -Arguments @($m2Cli, 'validate', 'game/levels')
    Invoke-Checked -FilePath 'dotnet' -Arguments @($m2Cli, 'validate', 'src/Dungeons.Core.Tests/TestLevels/test_spawner_arena/level.json')
    $m2Package = Join-Path $m2Evidence 'mossy.dlvl'
    $m2Unpacked = Join-Path $m2Evidence 'unpacked'
    $m2Migrated = Join-Path $m2Evidence 'migrated.json'
    $m2Replay = Join-Path $m2Evidence 'idle.dreplay'
    Invoke-Checked -FilePath 'dotnet' -Arguments @($m2Cli, 'pack', 'game/levels/mossy_hollow', $m2Package)
    Invoke-Checked -FilePath 'dotnet' -Arguments @($m2Cli, 'unpack', $m2Package, $m2Unpacked)
    if ((Get-FileHash -LiteralPath 'game/levels/mossy_hollow/level.json').Hash -ne (Get-FileHash -LiteralPath (Join-Path $m2Unpacked 'level.json')).Hash) { throw 'Package roundtrip changed level bytes.' }
    Invoke-Checked -FilePath 'dotnet' -Arguments @($m2Cli, 'migrate', $m2Package, $m2Migrated)
    Invoke-Checked -FilePath 'dotnet' -Arguments @($m2Cli, 'validate', $m2Migrated)
    Invoke-Checked -FilePath 'dotnet' -Arguments @($m2Cli, 'preview', $m2Package)
    Invoke-Checked -FilePath 'dotnet' -Arguments @($m2Cli, 'replay', '--record-idle', 'game/levels/mossy_hollow/level.json', '120', $m2Replay)
    Invoke-Checked -FilePath 'dotnet' -Arguments @($m2Cli, 'replay', 'game/levels/mossy_hollow/level.json', $m2Replay)
    # Publish into the evidence directory so later builds cannot replace the fuzzed binary.
    $m2FuzzBin = Join-Path $m2Evidence 'fuzz-bin'
    Invoke-Checked -FilePath 'dotnet' -Arguments @('publish', 'src/Dungeons.Tools', '-c', 'Release', '-o', $m2FuzzBin, '-p:UseAppHost=false', '-warnaserror')
    Invoke-Checked -FilePath 'dotnet' -Arguments @((Join-Path $m2FuzzBin 'Dungeons.Tools.dll'), 'fuzz', 'game/levels/mossy_hollow/level.json', $FuzzSeconds.ToString([Globalization.CultureInfo]::InvariantCulture)) -TimeoutSeconds ($FuzzSeconds + 60)
    [ordered]@{ utc = [DateTime]::UtcNow.ToString('o'); success = $true; fullM2Gate = ($FuzzSeconds -ge 600); fuzzSeconds = $FuzzSeconds; coreBinarySha256 = (Get-FileHash -LiteralPath (Join-Path $m2FuzzBin 'Dungeons.Core.dll')).Hash } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $m2Evidence 'result.json') -Encoding UTF8
    Write-Host "M2 checks passed (full gate: $($FuzzSeconds -ge 600)). Evidence: $m2Evidence"
}
finally { Stop-Transcript | Out-Null; Pop-Location }
