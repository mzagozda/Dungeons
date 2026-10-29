[CmdletBinding()]
param([Parameter(Mandatory)][string]$ApkPath, [switch]$Low)
. (Join-Path $PSScriptRoot 'Common.ps1')
$sdk = $env:ANDROID_HOME
if (!$sdk) { $sdk = $env:ANDROID_SDK_ROOT }
if (!$sdk) { throw 'Set ANDROID_HOME to verify the packaged Android manifest.' }
$aaptName = if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) { 'aapt.exe' } else { 'aapt' }
$aapt = Get-ChildItem -LiteralPath (Join-Path $sdk 'build-tools') -Directory | Sort-Object Name -Descending | ForEach-Object { Join-Path $_.FullName $aaptName } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (!$aapt) { throw 'Android build-tools aapt is required for packaged manifest verification.' }
$log = [IO.Path]::ChangeExtension($ApkPath, '.manifest.txt')
Invoke-Checked $aapt @('dump', 'xmltree', $ApkPath, 'AndroidManifest.xml') -LogPath $log -Quiet
$text = [IO.File]::ReadAllText($log)
foreach ($required in @('android.permission.VIBRATE','android.intent.action.VIEW','application/x-block-dungeons-level','application/octet-stream','pathPattern')) {
    if (!$text.Contains($required)) { throw "Packaged manifest is missing $required; see $log" }
}
if ($text -notmatch 'android:screenOrientation[^\r\n]*0xb\b') { throw "Packaged orientation is not userLandscape; see $log" }
$activityBlock = [regex]::Match($text, '(?ms)^\s+E: activity \([^\r\n]+\)\r?\n(?:(?!^\s+E: activity(?: |-)\().)*?android:name[^\r\n]*com\.godot\.game\.GodotApp[^\r\n]*\r?\n(?:(?!^\s+E: activity(?: |-)\().)*?(?=^\s+E: activity(?: |-)\(|\z)')
if (!$activityBlock.Success -or $activityBlock.Value -notmatch 'android:exported[^\r\n]*0xffffffff\b' -or !$activityBlock.Value.Contains('android.intent.action.VIEW')) { throw "Packaged import activity is not externally accessible; see $log" }
if ($Low) {
    if ($text -notmatch '(?s)android.hardware.vulkan.version[^\r\n]*\r?\n\s+A: android:required[^\r\n]*0x0\b' -or !$text.Contains('gl_compatibility')) { throw "Low APK still requires Vulkan or lacks Compatibility metadata; see $log" }
}
Write-Host "Packaged Android manifest passed (Low=$($Low.IsPresent))."
