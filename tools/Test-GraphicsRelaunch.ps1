[CmdletBinding()]
param([string]$GodotPath, [string]$EvidenceDirectory)
. (Join-Path $PSScriptRoot 'Common.ps1')
$godot = Get-GodotPath -ExplicitPath $GodotPath
if (!$EvidenceDirectory) { $EvidenceDirectory = Join-Path $script:RepoRoot ('artifacts/m7/relaunch-' + [guid]::NewGuid().ToString('N')) }
$EvidenceDirectory = [IO.Path]::GetFullPath($EvidenceDirectory)
[void][IO.Directory]::CreateDirectory($EvidenceDirectory)
$report = Join-Path $EvidenceDirectory ('relaunch-' + [guid]::NewGuid().ToString('N') + '.json')
$log = [IO.Path]::ChangeExtension($report, '.log')
Push-Location $script:RepoRoot
try {
    Invoke-Checked $godot @('--audio-driver', 'Dummy', '--path', 'game', '--rendering-method', 'mobile', 'res://scenes/graphics_relaunch_preview.tscn', '--', "--graphics-relaunch-report=$report", '--preserved-argument=kept') -TimeoutSeconds 30 -LogPath $log -RejectEngineErrors -Quiet
    for ($attempt = 0; $attempt -lt 30 -and !(Test-Path -LiteralPath $report); $attempt++) { Start-Sleep -Milliseconds 200 }
    if (!(Test-Path -LiteralPath $report)) { throw "Renderer child did not report success. See $log" }
    $result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
    if (!$result.passed -or $result.renderer -ne 'gl_compatibility' -or $result.audioDriver -ne 'Dummy' -or !$result.retryGuard -or !$result.userArgumentsPreserved -or $result.parentProcessId -eq $result.processId) { throw 'Renderer relaunch assertions failed.' }
    $child = $null
    try { $child = [Diagnostics.Process]::GetProcessById($result.processId); if (!$child.WaitForExit(5000)) { throw 'Renderer child did not exit after writing its report.' } }
    catch [ArgumentException] { } # Child already exited.
    finally { if ($child) { $child.Dispose() } }
    $childLog = [IO.Path]::ChangeExtension($report, '.child.log'); Copy-Item -LiteralPath $result.logPath -Destination $childLog
    $childText = [IO.File]::ReadAllText($childLog)
    if ($childText -notmatch 'GRAPHICS_RELAUNCH_PREVIEW=PASS' -or !$childText.Contains([IO.Path]::GetFileName($report)) -or $childText -match '(?m)^(ERROR:|SCRIPT ERROR:)') { throw "Renderer child log failed strict checks: $childLog" }
    Write-Host "Native renderer relaunch passed: $report"
}
finally { Pop-Location }
