[CmdletBinding()]
param()
. (Join-Path $PSScriptRoot 'Common.ps1')
$evidenceDir = Join-Path $script:RepoRoot 'artifacts/core'
[void][IO.Directory]::CreateDirectory($evidenceDir)
Push-Location $script:RepoRoot
Start-Transcript -Path (Join-Path $evidenceDir 'checks-transcript.log') -Force | Out-Null
try {
    # Build both Core targets; the host-independent gate needs no Godot executable.
    foreach ($project in @('Dungeons.Core', 'Dungeons.Core.Tests', 'Dungeons.Tools', 'Dungeons.Benchmarks')) {
        Invoke-Checked -FilePath 'dotnet' -Arguments @('build', "src/$project/$project.csproj", '--configuration', 'Release', '-warnaserror')
    }
    # Keep exact allocation/timing checks in their own optimized process. Under
    # .NET 10, prior functional-suite garbage collections can perturb the thread
    # allocation counter even across an allocation-free SpinWait-only interval.
    # Both partitions remain mandatory; no assertion or byte budget is relaxed.
    Invoke-Checked -FilePath 'dotnet' -Arguments @('test', 'src/Dungeons.Core.Tests', '--configuration', 'Release', '--no-build', '--filter', 'Category!=Performance', '--logger', 'trx', '--results-directory', (Join-Path $evidenceDir 'functional'))
    Invoke-Checked -FilePath 'dotnet' -Arguments @('test', 'src/Dungeons.Core.Tests', '--configuration', 'Release', '--no-build', '--filter', 'Category=Performance', '--logger', 'trx', '--results-directory', (Join-Path $evidenceDir 'performance'))
    # Coverage changes execution speed and adds a BCL facade reference. Run it separately;
    # Instrumentation-sensitive timing/exact-allocation probes are filtered here,
    # having passed against the normal optimized binary above.
    $coverageDir = Join-Path $evidenceDir ([Guid]::NewGuid().ToString('N'))
    Invoke-Checked -FilePath 'dotnet' -Arguments @('test', 'src/Dungeons.Core.Tests', '--configuration', 'Release', '--no-build', '--collect:XPlat Code Coverage', '--settings', 'tools/coverage.runsettings', '--filter', 'Category!=Performance', '--logger', 'trx', '--results-directory', $coverageDir)
    $reports = @(Get-ChildItem -LiteralPath $coverageDir -Recurse -Filter 'coverage.cobertura.xml')
    if ($reports.Count -eq 0) { throw 'Coverage report is missing.' }
    # VSTest's TRX logger may copy the attachment into its In/ directory as well.
    $reportHashes = @($reports | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } | Select-Object -Unique)
    if ($reportHashes.Count -ne 1) { throw 'Coverage reports disagree; refusing an ambiguous result.' }
    [xml]$coverage = Get-Content -LiteralPath $reports[0].FullName -Raw
    $packages = @($coverage.coverage.packages.package | Where-Object { $_.name -eq 'Dungeons.Core' })
    if ($packages.Count -ne 1) { throw 'Core coverage package is missing or ambiguous.' }
    $rate = [double]::Parse($packages[0].'line-rate', [Globalization.CultureInfo]::InvariantCulture)
    $summary = [ordered]@{ utc = [DateTime]::UtcNow.ToString('o'); configuration = 'Release'; coreLineCoverage = $rate; minimum = 0.85; report = $reports[0].FullName; success = ($rate -ge 0.85) }
    $summary | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidenceDir 'coverage-summary.json') -Encoding UTF8
    Write-Host ('Core line coverage: {0:P2} (required 85%)' -f $rate)
    if ($rate -lt 0.85) { throw 'Core line coverage is below specification §20.9.' }
}
finally { Stop-Transcript | Out-Null; Pop-Location }
