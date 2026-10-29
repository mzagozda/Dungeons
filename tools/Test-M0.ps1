[CmdletBinding()]
param([string]$GodotPath, [switch]$Rendered)
. (Join-Path $PSScriptRoot 'Common.ps1')
$godot = Get-GodotPath -ExplicitPath $GodotPath
Assert-GodotVersion -Path $godot
$evidenceDir = Join-Path $script:RepoRoot 'artifacts/m0'
[void][IO.Directory]::CreateDirectory($evidenceDir)
Push-Location $script:RepoRoot
Start-Transcript -Path (Join-Path $evidenceDir 'checks-transcript.log') -Force | Out-Null
try {
    Invoke-Checked -FilePath 'dotnet' -Arguments @('restore', 'Dungeons.sln', '--locked-mode')
    Invoke-Checked -FilePath 'dotnet' -Arguments @('build', 'Dungeons.sln', '--configuration', 'Debug', '--no-restore', '-warnaserror')
    # Optimized timing/exact allocation acceptance is mandatory in Test-Core.ps1.
    Invoke-Checked -FilePath 'dotnet' -Arguments @('test', 'src/Dungeons.Core.Tests', '--configuration', 'Debug', '--no-build', '--filter', 'Category!=Performance', '--logger', 'trx', '--results-directory', $evidenceDir)
    Initialize-GodotEditor -Path $godot
    [void](Invoke-GodotImport -Path $godot -LogPath (Join-Path $evidenceDir 'import.log'))
    $probeArguments = @('--path', (Join-Path $script:RepoRoot 'game'))
    if (!$Rendered) { $probeArguments += '--headless' }
    $probeArguments += @('--', '--m0-smoke-test')
    if ($Rendered) { $probeArguments += "--m0-screenshot=$(Join-Path $evidenceDir 'editor-probe.png')" }
    Invoke-Probe -Executable $godot -Arguments $probeArguments -LogPath (Join-Path $evidenceDir 'editor-probe.log')
}
finally { Stop-Transcript | Out-Null; Pop-Location }
