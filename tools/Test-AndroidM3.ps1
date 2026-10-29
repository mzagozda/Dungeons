[CmdletBinding()]
param([string]$AdbPath = 'adb', [string]$Serial, [ValidateRange(5, 120)][int]$CaptureSeconds = 15)
. (Join-Path $PSScriptRoot 'Common.ps1')
& (Join-Path $PSScriptRoot 'Test-AndroidM0.ps1') -AdbPath $AdbPath -Serial $Serial
if (!$Serial) { $Serial = (Get-Content (Join-Path $script:RepoRoot 'artifacts/m0/android-device.json') -Raw | ConvertFrom-Json).serial }
$package = 'org.blockdungeons.game'
$evidence = Join-Path $script:RepoRoot ('artifacts/m3/android-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($evidence)
$activity = @(& $AdbPath -s $Serial shell cmd package resolve-activity --brief $package | Where-Object { $_ -match '/' })
if ($LASTEXITCODE -ne 0 -or $activity.Count -ne 1) { throw 'Cannot resolve Android activity.' }
$file = 'm3-render-' + [guid]::NewGuid().ToString('N') + '.png'
Invoke-Checked $AdbPath @('-s', $Serial, 'shell', 'am', 'force-stop', $package)
# GodotActivity's documented source accepts EXTRA_COMMAND_LINE_PARAMS as String[].
# No comma-valued arguments here: Android am uses commas to separate array items.
$parameters = "res://scenes/m3_preview.tscn,--,--m3-screenshot=user://$file,--perf-capture=$CaptureSeconds,--perf-uncapped"
Invoke-Checked $AdbPath @('-s', $Serial, 'shell', 'am', 'start', '-W', '-f', '0x10008000', '-n', $activity[0].Trim(), '--esa', 'command_line_params', $parameters)
$runPid = (& $AdbPath -s $Serial shell pidof $package | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $runPid -notmatch '^\d+$') { throw 'Rendered app did not expose one running process.' }
$ready = $false
for ($attempt = 0; $attempt -lt $CaptureSeconds + 30; $attempt++) {
    & $AdbPath -s $Serial shell run-as $package test -f "files/$file"
    if ($LASTEXITCODE -eq 0) { $ready = $true; break }
    Start-Sleep -Seconds 1
}
if (!$ready) { throw 'Rendered Android scene did not write its fresh capture. Inspect package logcat.' }
$runtimeLog = Join-Path $evidence 'runtime.log'
Invoke-Checked $AdbPath @('-s', $Serial, 'logcat', '-d', "--pid=$runPid") -LogPath $runtimeLog -Quiet
$runtime = [IO.File]::ReadAllText($runtimeLog)
if ($runtime -notmatch 'M3_PREVIEW=PASS' -or $runtime -match 'godot\s*:.*(?:ERROR:|SCRIPT ERROR:)') { throw "Android renderer reported an error or no success marker; see $runtimeLog" }
$png = Join-Path $evidence 'scene.png'
$start = New-Object Diagnostics.ProcessStartInfo
$start.FileName = (Get-Command $AdbPath).Source
$start.Arguments = "-s $Serial exec-out run-as $package cat files/$file"
$start.UseShellExecute = $false; $start.CreateNoWindow = $true; $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
$process = New-Object Diagnostics.Process
$process.StartInfo = $start
$destination = [IO.File]::Create($png)
try {
    [void]$process.Start()
    $copy = $process.StandardOutput.BaseStream.CopyToAsync($destination)
    if (!$copy.Wait(30000) -or !$process.WaitForExit(5000)) { $process.Kill(); throw 'ADB PNG transfer timed out.' }
    if ($process.ExitCode -ne 0) { throw 'ADB PNG transfer failed.' }
}
finally { $destination.Dispose(); $process.Dispose() }
Invoke-Checked $AdbPath @('-s', $Serial, 'exec-out', 'run-as', $package, 'cat', "files/$file.csv") -LogPath ($png + '.csv') -Quiet
$perf = Join-Path $evidence 'perf.json'
$budgetFailure = $null
try { Invoke-Checked 'python' @('tools/summarize_perf.py', ($png + '.csv'), '--mobile', '--output', $perf) }
catch { $budgetFailure = $_ }
$apk = Join-Path $script:RepoRoot 'artifacts/android/block_dungeons.apk'
[ordered]@{ capturedUtc=[DateTime]::UtcNow.ToString('o'); serial=$Serial; apkSha256=(Get-FileHash -LiteralPath $apk -Algorithm SHA256).Hash; screenshot=$png; performance=$perf; budgetsPassed=($null -eq $budgetFailure); fullReferenceDeviceAcceptance=$false } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'result.json') -Encoding UTF8
if ($budgetFailure) { throw "Android capture completed but a performance budget failed; see $perf" }
Write-Host "Physical Android rendered capture and available-device budgets passed. Evidence: $evidence"
