[CmdletBinding()]
param([Parameter(Mandatory)][string]$ApkPath, [Parameter(Mandatory)][string]$EvidenceDirectory,
      [ValidatePattern('^[0-9a-fA-F]{64}$')][string]$ReleaseCertificateSha256)
. (Join-Path $PSScriptRoot 'Common.ps1')
$apk = (Resolve-Path -LiteralPath $ApkPath).Path
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
if (Test-Path -LiteralPath $evidence) { throw 'Release audit evidence must use a new directory.' }
$sdk = $env:ANDROID_HOME
if (!$sdk) { $sdk = $env:ANDROID_SDK_ROOT }
if (!$sdk) { throw 'Set ANDROID_HOME to inspect the APK.' }
$windows = [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT
$aaptName = if ($windows) { 'aapt.exe' } else { 'aapt' }
$signerName = if ($windows) { 'apksigner.bat' } else { 'apksigner' }
$buildTools = Get-ChildItem -LiteralPath (Join-Path $sdk 'build-tools') -Directory | Sort-Object { [version]$_.Name } -Descending | Where-Object { (Test-Path -LiteralPath (Join-Path $_.FullName $aaptName)) -and (Test-Path -LiteralPath (Join-Path $_.FullName $signerName)) } | Select-Object -First 1
if (!$buildTools) { throw 'Android build-tools aapt and apksigner are required.' }
[void][IO.Directory]::CreateDirectory($evidence)
$badging = Join-Path $evidence 'badging.txt'; $signature = Join-Path $evidence 'signature.txt'
Invoke-Checked (Join-Path $buildTools.FullName $aaptName) @('dump','badging',$apk) -LogPath $badging -Quiet
# apksigner is a batch launcher on Windows; invoke its actual Java entry point
# instead of constructing a cmd.exe command around the APK path.
$java = if ($env:JAVA_HOME) { Join-Path $env:JAVA_HOME $(if ($windows) { 'bin/java.exe' } else { 'bin/java' }) } else { (Get-Command java -ErrorAction Stop).Source }
$jar = Join-Path $buildTools.FullName 'lib/apksigner.jar'
Invoke-Checked $java @('-jar',$jar,'verify','--verbose','--print-certs',$apk) -LogPath $signature -Quiet
$arguments = @((Join-Path $PSScriptRoot 'audit_android_release.py'),$apk,'--badging',$badging,'--signature',$signature,'--output',(Join-Path $evidence 'result.json'))
if ($ReleaseCertificateSha256) { $arguments += @('--release-cert-sha256',$ReleaseCertificateSha256) }
Invoke-Checked python $arguments
