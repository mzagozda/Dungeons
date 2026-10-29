[CmdletBinding()]
param([string]$AdbPath = 'adb', [Parameter(Mandatory)][string]$Serial, [switch]$Low)
. (Join-Path $PSScriptRoot 'Common.ps1')
$package = 'org.blockdungeons.game'
$apk = Join-Path $script:RepoRoot $(if ($Low) { 'artifacts/android/block_dungeons_low.apk' } else { 'artifacts/android/block_dungeons.apk' })
if (!(Test-Path -LiteralPath $apk)) { throw 'Export the selected fresh Android APK first.' }
$power = (& $AdbPath -s $Serial shell dumpsys power | Out-String)
if ($LASTEXITCODE -ne 0 -or $power -notmatch 'mWakefulness=Awake') { throw 'Wake and unlock the connected physical Android device before M7 rendering.' }
$physical = (& $AdbPath -s $Serial shell getprop ro.kernel.qemu | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $physical -eq '1' -or $Serial.StartsWith('emulator-')) { throw 'This checker requires a physical Android device.' }
$evidence = Join-Path $script:RepoRoot ('artifacts/m7/android-' + $(if ($Low) { 'low-' } else { 'medium-' }) + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($evidence)
Invoke-Checked $AdbPath @('-s', $Serial, 'install', '-r', $apk)
$installed = @(& $AdbPath -s $Serial shell pm path $package | Where-Object { $_ -match '^package:' })
if ($LASTEXITCODE -ne 0 -or $installed.Count -ne 1) { throw 'Expected one installed APK.' }
$installedHash = ((& $AdbPath -s $Serial shell sha256sum $installed[0].Trim().Substring(8) | Out-String).Trim() -split '\s+')[0]
$hash = (Get-FileHash -LiteralPath $apk -Algorithm SHA256).Hash
if ($LASTEXITCODE -ne 0 -or $hash -ine $installedHash) { throw 'Installed APK does not match the selected build.' }
$activity = @(& $AdbPath -s $Serial shell cmd package resolve-activity --brief $package | Where-Object { $_ -match '/' })
if ($LASTEXITCODE -ne 0 -or $activity.Count -ne 1) { throw 'Could not resolve Android activity.' }
Invoke-Checked $AdbPath @('-s', $Serial, 'shell', 'am', 'force-stop', $package)
$stem = 'm7-' + [guid]::NewGuid().ToString('N')
$arguments = "res://scenes/m7_preview.tscn,--,--m7-mode=device,--m7-screenshot=user://$stem.png"
Invoke-Checked $AdbPath @('-s', $Serial, 'shell', 'am', 'start', '-W', '-f', '0x10008000', '-n', $activity[0].Trim(), '--esa', 'command_line_params', $arguments)
$devicePid = (& $AdbPath -s $Serial shell pidof $package | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $devicePid -notmatch '^\d+$') { throw 'No fresh Android process was found.' }
$ready = $false
for ($attempt = 0; $attempt -lt 40; $attempt++) {
    & $AdbPath -s $Serial shell run-as $package test -f "files/$stem.json"
    if ($LASTEXITCODE -eq 0) { $ready = $true; break }
    Start-Sleep -Seconds 1
}
$log = Join-Path $evidence 'runtime.log'
Invoke-Checked $AdbPath @('-s', $Serial, 'logcat', '-d', "--pid=$devicePid") -LogPath $log -Quiet
if (!$ready) { throw "M7 did not produce a fresh report. See $log" }
$text = [IO.File]::ReadAllText($log)
if ($text -notmatch 'M7_PREVIEW=PASS' -or $text -match 'godot\s*:.*(?:ERROR:|SCRIPT ERROR:)') { throw "Android runtime check failed; see $log" }
foreach ($extension in @('png','json','dreplay')) {
    $start = [Diagnostics.ProcessStartInfo]::new(); $start.FileName = (Get-Command $AdbPath).Source
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true; $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
    foreach ($argument in @('-s', $Serial, 'exec-out', 'run-as', $package, 'cat', "files/$stem.$extension")) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new(); $process.StartInfo = $start
    $output = [IO.File]::Create((Join-Path $evidence "scene.$extension"))
    try {
        [void]$process.Start(); $copy = $process.StandardOutput.BaseStream.CopyToAsync($output)
        if (!$copy.Wait(30000) -or !$process.WaitForExit(5000)) { $process.Kill(); throw 'ADB file transfer timed out.' }
        if ($process.ExitCode -ne 0) { throw 'ADB file transfer failed.' }
    }
    finally { $output.Dispose(); $process.Dispose() }
}
$report = Get-Content -LiteralPath (Join-Path $evidence 'scene.json') -Raw | ConvertFrom-Json
if (!$report.replayVerified -or !$report.debugGestureVerified -or $report.androidSdk -lt 26 -or !$report.nativeFilePicker) { throw 'Packaged native/replay/debug-gesture capability checks failed.' }
if ($report.audioVoicesPrepared -ne 152 -or !$report.music -or !$report.audioDriver -or $report.audioDriver -eq 'Dummy') { throw 'Packaged audio bank/music did not initialize with the device audio driver.' }
if ($Low -and ($report.renderer -ne 'gl_compatibility' -or $report.quality -ne 'Low' -or $report.lights -gt 4)) { throw 'Low APK did not boot Compatibility with its quality limits.' }
if (!$Low -and ($report.renderer -ne 'mobile' -or $report.quality -ne 'Medium')) { throw 'Default APK did not boot Mobile/Medium on the available device.' }
[ordered]@{ capturedUtc=[DateTime]::UtcNow.ToString('o'); serial=$Serial; apkSha256=$hash; installedSha256=$installedHash; low=$Low.IsPresent; syntheticTouchReplayPassed=$true; nativeMetrics=$report; physicalFingerOsBatteryThermalGpuAcceptancePending=$true; fullAcceptance=$false } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence 'result.json') -Encoding UTF8
Write-Host "Packaged Android M7 render/synthetic gesture checks passed. Physical input acceptance remains pending. Evidence: $evidence"
