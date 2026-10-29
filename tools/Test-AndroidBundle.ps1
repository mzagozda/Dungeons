#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BundlePath,
    [Parameter(Mandatory)][string]$TextureProfileDirectory,
    [Parameter(Mandatory)][string]$EvidenceDirectory,
    [switch]$DebugApks,
    [string]$ApkSigningKeyStore,
    [string]$ApkSigningKeyAlias,
    [string]$StorePasswordFile,
    [string]$KeyPasswordFile,
    [ValidatePattern('^[a-fA-F0-9]{64}$')][string]$ExpectedUploadCertificateSha256,
    [ValidatePattern('^[a-fA-F0-9]{64}$')][string]$ExpectedApkCertificateSha256,
    [switch]$Low,
    [string]$GodotPath
)
. (Join-Path $PSScriptRoot 'Common.ps1')
$bundle = (Resolve-Path -LiteralPath $BundlePath).Path
$textureProfile = (Resolve-Path -LiteralPath $TextureProfileDirectory).Path
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
if ([IO.Path]::GetExtension($bundle) -ne '.aab' -or (Test-Path -LiteralPath $evidence)) { throw 'An AAB and a new evidence directory are required.' }
if ($DebugApks -and ($ApkSigningKeyStore -or $ApkSigningKeyAlias -or $StorePasswordFile -or $KeyPasswordFile -or $ExpectedApkCertificateSha256)) { throw 'Explicit debug APK validation cannot be combined with publisher APK signing parameters.' }
if (!$DebugApks -and (!$ApkSigningKeyStore -or !$ApkSigningKeyAlias -or !$StorePasswordFile -or !$KeyPasswordFile)) { throw 'Supply APK signing key/alias/password files, or explicitly select -DebugApks for technical validation.' }
if (!$env:JAVA_HOME) { throw 'Set JAVA_HOME to the pinned JDK 17.' }
$java = Join-Path $env:JAVA_HOME $(if ($IsWindows) { 'bin/java.exe' } else { 'bin/java' })
$keytool = Join-Path $env:JAVA_HOME $(if ($IsWindows) { 'bin/keytool.exe' } else { 'bin/keytool' })
$jarsigner = Join-Path $env:JAVA_HOME $(if ($IsWindows) { 'bin/jarsigner.exe' } else { 'bin/jarsigner' })
$pin = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'bundletool.json') -Raw | ConvertFrom-Json
$jar = Join-Path $script:RepoRoot ('.toolchain/bundletool/' + $pin.file)
if (!(Test-Path -LiteralPath $jar) -or (Get-FileHash -LiteralPath $jar -Algorithm SHA256).Hash -ine $pin.sha256) { throw 'Run Install-Bundletool.ps1; the pinned official jar is required.' }
$godot = Get-GodotPath -ExplicitPath $GodotPath
[void][IO.Directory]::CreateDirectory($evidence)
$summary = [ordered]@{passed=$false;technicalValidationPassed=$false;releaseAcceptancePassed=$false;storeSubmissionReady=$false;physicalAcceptance=$false;debugApkValidation=$DebugApks.IsPresent;bundleSha256=(Get-FileHash -LiteralPath $bundle -Algorithm SHA256).Hash;bundletool=$pin.version}
$temporaryPassword = $null
Push-Location $script:RepoRoot
try {
    Invoke-Checked $java @('-jar',$jar,'validate',"--bundle=$bundle") -LogPath (Join-Path $evidence 'validate.log') -Quiet
    Invoke-Checked $jarsigner @('-J-Duser.language=en','-verify',$bundle) -LogPath (Join-Path $evidence 'aab-signature.log') -Quiet
    $verification = [IO.File]::ReadAllText((Join-Path $evidence 'aab-signature.log'))
    if ($verification -notmatch '(?m)^jar verified\.' -or $verification -match 'contains unsigned entries|treated as unsigned') { throw 'AAB signature is absent, invalid or leaves unsigned payload entries.' }
    Invoke-Checked $keytool @('-J-Duser.language=en','-printcert','-rfc','-jarfile',$bundle) -LogPath (Join-Path $evidence 'aab-certificates.pem') -Quiet
    $pem = [IO.File]::ReadAllText((Join-Path $evidence 'aab-certificates.pem'))
    $cert = [Security.Cryptography.X509Certificates.X509Certificate2]::CreateFromPem($pem)
    try {
        $fingerprint = $cert.GetCertHashString([Security.Cryptography.HashAlgorithmName]::SHA256).ToLowerInvariant()
        $summary.uploadCertificateSha256 = $fingerprint
        $summary.uploadCertificateSubject = $cert.Subject
        $summary.uploadCertificateValidNow = $cert.NotBefore.ToUniversalTime() -le [DateTime]::UtcNow -and $cert.NotAfter.ToUniversalTime() -gt [DateTime]::UtcNow
        $summary.uploadPublisherMatched = $ExpectedUploadCertificateSha256 -and $fingerprint -eq $ExpectedUploadCertificateSha256.ToLowerInvariant() -and $cert.Subject -notmatch '(?:^|,\s*)CN=Android Debug(?:,|$)' -and $summary.uploadCertificateValidNow
    }
    finally { $cert.Dispose() }
    if (!$summary.uploadCertificateValidNow) { throw 'AAB signing certificate is not currently valid.' }
    if ($ExpectedUploadCertificateSha256 -and !$summary.uploadPublisherMatched) { throw 'AAB upload certificate does not match the expected publisher identity.' }
    $archive = [IO.Compression.ZipFile]::OpenRead($bundle)
    try { if (@($archive.Entries | Where-Object FullName -Match '^META-INF/[^/]+\.(RSA|DSA|EC)$').Count -ne 1) { throw 'Expected exactly one AAB signer.' } }
    finally { $archive.Dispose() }
    Invoke-Checked $java @('-jar',$jar,'dump','manifest',"--bundle=$bundle",'--module=base') -LogPath (Join-Path $evidence 'manifest.xml') -Quiet
    if ($DebugApks) {
        $ApkSigningKeyStore = Join-Path $script:RepoRoot '.toolchain/bundletool/dungeons-validation.keystore'
        $ApkSigningKeyAlias = 'androiddebugkey'
        $temporaryPassword = Join-Path $evidence 'temporary-debug-key.password'
        [IO.File]::WriteAllText($temporaryPassword,'android',[Text.UTF8Encoding]::new($false))
        $StorePasswordFile = $temporaryPassword; $KeyPasswordFile = $temporaryPassword
        if (!(Test-Path -LiteralPath $ApkSigningKeyStore)) {
            Invoke-Checked $keytool @('-genkeypair','-keystore',$ApkSigningKeyStore,'-alias',$ApkSigningKeyAlias,'-keyalg','RSA','-keysize','2048','-validity','365','-dname','CN=Android Debug,O=Block Dungeons QA,C=PL','-storepass:file',$StorePasswordFile,'-keypass:file',$KeyPasswordFile,'-noprompt') -LogPath (Join-Path $evidence 'debug-key.log') -Quiet
        }
    }
    $ApkSigningKeyStore = (Resolve-Path -LiteralPath $ApkSigningKeyStore).Path
    $StorePasswordFile = (Resolve-Path -LiteralPath $StorePasswordFile).Path
    $KeyPasswordFile = (Resolve-Path -LiteralPath $KeyPasswordFile).Path
    $apkSet = Join-Path $evidence 'universal.apks'; $apk = Join-Path $evidence 'universal.apk'
    Invoke-Checked $java @('-jar',$jar,'build-apks',"--bundle=$bundle","--output=$apkSet",'--mode=universal',"--ks=$ApkSigningKeyStore","--ks-key-alias=$ApkSigningKeyAlias","--ks-pass=file:$StorePasswordFile","--key-pass=file:$KeyPasswordFile") -TimeoutSeconds 300 -LogPath (Join-Path $evidence 'build-apks.log') -Quiet
    $archive = [IO.Compression.ZipFile]::OpenRead($apkSet)
    try {
        $entry = $archive.GetEntry('universal.apk')
        if (!$entry -or $entry.Length -gt 256MB) { throw 'Missing or oversized generated universal APK.' }
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,$apk,$false)
    }
    finally { $archive.Dispose() }
    & (Join-Path $PSScriptRoot 'Test-ApkManifest.ps1') -ApkPath $apk -Low:$Low
    & (Join-Path $PSScriptRoot 'Test-EditorExport.ps1') -ApkPath $apk
    $resources = Join-Path $evidence 'package-resources.zip'
    Invoke-Checked python @((Join-Path $PSScriptRoot 'audit_mobile_texture_package.py'),$apk,$textureProfile,'--report',(Join-Path $evidence 'textures.json'),'--asset-pack',$resources) -LogPath (Join-Path $evidence 'textures.log') -Quiet
    Invoke-Checked $godot @('--headless','--path',$textureProfile,'--script',(Join-Path $PSScriptRoot 'audit_packaged_mobile_textures.gd'),'--',$resources,(Join-Path $textureProfile 'manifest.json'),(Join-Path $evidence 'native-textures.json')) -TimeoutSeconds 90 -LogPath (Join-Path $evidence 'native-textures.log') -RejectEngineErrors -Quiet
    $releaseDirectory = Join-Path $evidence 'apk-release'
    $releaseParameters = @{ApkPath=$apk;EvidenceDirectory=$releaseDirectory}
    if ($ExpectedApkCertificateSha256) { $releaseParameters.ReleaseCertificateSha256 = $ExpectedApkCertificateSha256 }
    try { & (Join-Path $PSScriptRoot 'Test-AndroidRelease.ps1') @releaseParameters }
    catch { if (!(Test-Path -LiteralPath (Join-Path $releaseDirectory 'result.json'))) { throw } }
    $release = Get-Content -LiteralPath (Join-Path $releaseDirectory 'result.json') -Raw | ConvertFrom-Json
    if (!$release.checks.minSdk26 -or !$release.checks.currentTargetSdk -or !$release.checks.native16Kb -or !$release.checks.sizeBudget) { throw 'Generated APK fails SDK/native/size prerequisites.' }
    if (!$release.verifiedSignature) { throw 'Generated APK signature verification did not pass.' }
    $sdk = if ($env:ANDROID_HOME) { $env:ANDROID_HOME } else { $env:ANDROID_SDK_ROOT }
    $zipalignName = if ($IsWindows) { 'zipalign.exe' } else { 'zipalign' }
    $zipalign = Get-ChildItem -LiteralPath (Join-Path $sdk 'build-tools') -Directory | Sort-Object { [version]$_.Name } -Descending | ForEach-Object { Join-Path $_.FullName $zipalignName } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (!$zipalign) { throw 'Android build-tools zipalign is required.' }
    Invoke-Checked $zipalign @('-c','-P','16','-v','4',$apk) -LogPath (Join-Path $evidence 'zipalign16.log') -Quiet
    $summary.zipalign16Passed = $true
    if ($ExpectedApkCertificateSha256 -and !$release.checks.releaseSignature) { throw 'Generated APK does not match the expected publisher signing identity.' }
    $summary.generatedApkSha256 = $release.apkSha256
    $summary.technicalValidationPassed = $true
    $summary.releaseAcceptancePassed = !$DebugApks -and $summary.uploadPublisherMatched -and $release.passed
    $summary.passed = $DebugApks.IsPresent -or $summary.releaseAcceptancePassed
}
catch { $summary.failure = $_.Exception.Message; throw }
finally {
    if ($temporaryPassword) { Remove-Item -LiteralPath $temporaryPassword -ErrorAction SilentlyContinue }
    $summary | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath (Join-Path $evidence 'result.json') -Encoding utf8
    Pop-Location
}
if (!$summary.passed) { throw 'Technical validation passed; explicit publisher identities/release checks remain pending. See result.json.' }
Write-Host "Android bundle validation passed (debug APK validation=$($DebugApks.IsPresent)); store and physical acceptance remain separate."
