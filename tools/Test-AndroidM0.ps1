[CmdletBinding()]
param([string]$AdbPath = 'adb', [string]$Serial, [switch]$SkipInstall)
. (Join-Path $PSScriptRoot 'Common.ps1')
$package = 'org.blockdungeons.game'
$apk = Join-Path $script:RepoRoot 'artifacts/android/block_dungeons.apk'
if (!(Test-Path -LiteralPath $apk)) { throw 'Export the debug Android APK with tools/Export-M0.ps1 -Platform Android first.' }
$devices = @(& $AdbPath devices -l)
if ($LASTEXITCODE -ne 0) { throw 'ADB failed.' }
$authorized = @($devices | Where-Object { $_ -match '^\S+\s+device\s' } | ForEach-Object { ($_ -split '\s+')[0] })
if (!$Serial) {
    if ($authorized.Count -ne 1) { throw "Expected one authorized device; found $($authorized.Count). Connect/authorize a device, or select -Serial." }
    $Serial = $authorized[0]
}
if ($authorized -notcontains $Serial) { throw "Device $Serial is not connected and authorized." }
$powerState = (& $AdbPath -s $Serial shell dumpsys power | Out-String)
if ($powerState -match 'mWakefulness=(?:Asleep|Dozing)') { throw 'Wake and unlock the physical phone before the rendered Vulkan/device check. ADB authorization alone does not create a visible Android window.' }
$emulatorFlag = (& $AdbPath -s $Serial shell getprop ro.kernel.qemu | Out-String).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Could not query the device type.' }
if ($emulatorFlag -eq '1' -or $Serial.StartsWith('emulator-', [StringComparison]::Ordinal)) {
    throw 'M0 requires a physical Android device; an emulator cannot satisfy the gate.'
}
if (!$SkipInstall) {
    Invoke-Checked -FilePath $AdbPath -Arguments @('-s', $Serial, 'install', '-r', $apk)
}
# Manual installation must be of this exact APK, not an older probe left on the phone.
$installedPaths = @(& $AdbPath -s $Serial shell pm path $package | Where-Object { $_ -match '^package:' })
if ($LASTEXITCODE -ne 0 -or $installedPaths.Count -ne 1) { throw 'The single-APK probe is not installed. Complete installation on the phone first.' }
$installedPath = $installedPaths[0].Trim().Substring('package:'.Length)
$installedHashLine = (& $AdbPath -s $Serial shell sha256sum $installedPath | Out-String).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Could not verify the installed APK hash.' }
$installedHash = ($installedHashLine -split '\s+')[0]
$expectedHash = (Get-FileHash -LiteralPath $apk -Algorithm SHA256).Hash
if ($installedHash -ine $expectedHash) { throw 'The installed APK does not match artifacts/android/block_dungeons.apk.' }
Invoke-Checked -FilePath $AdbPath -Arguments @('-s', $Serial, 'shell', 'am', 'force-stop', $package)
# Remove only this probe's previous report so stale success cannot satisfy a new device run.
Invoke-Checked -FilePath $AdbPath -Arguments @('-s', $Serial, 'shell', 'run-as', $package, 'rm', '-f', 'files/m0-report.json')
$activity = @(& $AdbPath -s $Serial shell cmd package resolve-activity --brief $package | Where-Object { $_ -match '/' })
if ($LASTEXITCODE -ne 0 -or $activity.Count -ne 1) { throw 'Could not resolve the APK launch activity.' }
Invoke-Checked -FilePath $AdbPath -Arguments @('-s', $Serial, 'shell', 'am', 'start', '-W', '-f', '0x10008000', '-n', $activity[0].Trim(), '--esa', 'command_line_params', '--,--m0-smoke-test')
$reportText = ''
for ($attempt = 0; $attempt -lt 15; $attempt++) {
    # A cold start has not written the report yet. Avoid cat's expected stderr:
    # Windows PowerShell turns it into a terminating NativeCommandError under Stop.
    & $AdbPath -s $Serial shell run-as $package test -f files/m0-report.json
    if ($LASTEXITCODE -eq 0) {
        $reportText = (& $AdbPath -s $Serial shell run-as $package cat files/m0-report.json | Out-String)
        if ($LASTEXITCODE -eq 0 -and $reportText.Contains('"milestone"')) { break }
    }
    Start-Sleep -Seconds 1
}
if (!$reportText.Contains('"milestone"')) { throw 'Device did not produce a probe report within 15 seconds. Inspect adb logcat.' }
$report = $reportText | ConvertFrom-Json
$evidenceDir = Join-Path $script:RepoRoot 'artifacts/m0'
[void][IO.Directory]::CreateDirectory($evidenceDir)
$evidence = [ordered]@{
    serial = $Serial
    capturedUtc = [DateTime]::UtcNow.ToString('o')
    apkSha256 = $expectedHash
    installedApkSha256 = $installedHash
    manuallyInstalled = $SkipInstall.IsPresent
    report = $report
}
$evidence | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidenceDir 'android-device.json') -Encoding UTF8
if (!$report.passed -or !$report.packaged -or $report.platform -ne 'Android' -or $report.display -eq 'headless') {
    throw 'Android probe failed; see artifacts/m0/android-device.json.'
}
if (!($report.PSObject.Properties.Name -contains 'm2ContentReplayPassed') -or !$report.m2ContentReplayPassed) { throw 'Packaged M2 content/replay probe did not pass.' }
Write-Host 'Physical Android install, launch, JSON/translation/Core link and cube rotation probe passed. Visually verify the spinning cube and label on the device.'
