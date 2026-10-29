#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$IdentityPath,
    [string]$OutputDirectory = 'artifacts/release-ci/android',
    [string]$EvidenceDirectory = 'artifacts/release-ci/android-checks',
    [string]$WorkspaceDirectory = 'artifacts/release-ci/android-workspace'
)
. (Join-Path $PSScriptRoot 'ReleaseArtifacts.ps1')
$identity = Read-ReleaseIdentity $IdentityPath
foreach ($name in @('ANDROID_UPLOAD_KEYSTORE_BASE64','ANDROID_UPLOAD_KEY_ALIAS','ANDROID_UPLOAD_KEYSTORE_PASSWORD')) {
    if (![Environment]::GetEnvironmentVariable($name,'Process')) { throw "Configure the required GitHub Actions secret $name. No debug signing fallback is allowed." }
}
$public = Get-Content -LiteralPath (Join-Path $script:RepoRoot 'platform/android/play-console.json') -Raw | ConvertFrom-Json
$metadata = Get-Content -LiteralPath (Join-Path $script:RepoRoot 'platform/store/en-US.json') -Raw | ConvertFrom-Json
if ($metadata.release.versionName -ne $identity.versionName -or $metadata.release.androidVersionCode -ne $identity.androidVersionCode -or $identity.androidVersionCode -le $public.uploadedVersionCode) { throw 'Run prepare_ci_release.py in this disposable checkout before the Android release build.' }
$output = New-ReleaseDirectory $OutputDirectory
$evidence = New-ReleaseDirectory $EvidenceDirectory
$WorkspaceDirectory = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($WorkspaceDirectory)
if ($output -eq $evidence -or $output.StartsWith($evidence + [IO.Path]::DirectorySeparatorChar) -or $evidence.StartsWith($output + [IO.Path]::DirectorySeparatorChar)) { throw 'Output and evidence directories must not overlap.' }
$tempRoot = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [IO.Path]::GetTempPath() }
$privateDirectory = Join-Path $tempRoot ('block-dungeons-signing-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($privateDirectory)
$oldSigning = @{}
$names = @('GODOT_ANDROID_KEYSTORE_RELEASE_PATH','GODOT_ANDROID_KEYSTORE_RELEASE_USER','GODOT_ANDROID_KEYSTORE_RELEASE_PASSWORD')
foreach ($name in $names) { $oldSigning[$name] = [Environment]::GetEnvironmentVariable($name,'Process') }
$password = $env:ANDROID_UPLOAD_KEYSTORE_PASSWORD
$summary = [ordered]@{passed=$false;identity=$identity;uploadCertificateSha256=$public.uploadCertificateSha256;apkUsesUploadKey=$true;playDistributionApksVerified=$false;storeSubmissionReady=$false;physicalAcceptance=$false;privateFilesRemoved=$false}
try {
    if ($IsWindows) {
        $acl = Get-Acl -LiteralPath $privateDirectory
        $acl.SetAccessRuleProtection($true,$false)
        $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User
        $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid,'FullControl','ContainerInherit,ObjectInherit','None','Allow'))
        Set-Acl -LiteralPath $privateDirectory -AclObject $acl
    } else {
        Invoke-Checked chmod @('700',$privateDirectory) -Quiet
    }
    $keyStore = Join-Path $privateDirectory 'upload.p12'
    $passwordFile = Join-Path $privateDirectory 'password.txt'
    $bytes = [Convert]::FromBase64String($env:ANDROID_UPLOAD_KEYSTORE_BASE64)
    try { [IO.File]::WriteAllBytes($keyStore,$bytes) } finally { [Array]::Clear($bytes) }
    [IO.File]::WriteAllText($passwordFile,$password,[Text.UTF8Encoding]::new($false))
    if (!$IsWindows) { Invoke-Checked chmod @('600',$keyStore,$passwordFile) -Quiet }
    $env:GODOT_ANDROID_KEYSTORE_RELEASE_PATH = $keyStore
    $env:GODOT_ANDROID_KEYSTORE_RELEASE_USER = $env:ANDROID_UPLOAD_KEY_ALIAS
    $env:GODOT_ANDROID_KEYSTORE_RELEASE_PASSWORD = $password
    Remove-Item Env:ANDROID_UPLOAD_KEYSTORE_BASE64,Env:ANDROID_UPLOAD_KEY_ALIAS,Env:ANDROID_UPLOAD_KEYSTORE_PASSWORD
    # Refuse a wrong/expired key before paying for a full export. Certificate bytes are public.
    $certificates = [Security.Cryptography.X509Certificates.X509CertificateLoader]::LoadPkcs12Collection([IO.File]::ReadAllBytes($keyStore),$password,[Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
    try {
        $signers = @($certificates | Where-Object HasPrivateKey)
        if ($signers.Count -ne 1 -or $signers[0].GetCertHashString([Security.Cryptography.HashAlgorithmName]::SHA256) -ine $public.uploadCertificateSha256 -or $signers[0].NotAfter.ToUniversalTime() -le [DateTime]::UtcNow) { throw 'Keystore does not contain the registered, valid Taskscape upload key.' }
    } finally { foreach ($certificate in $certificates) { $certificate.Dispose() } }
    & (Join-Path $PSScriptRoot 'Install-Bundletool.ps1')
    & (Join-Path $PSScriptRoot 'Export-AndroidBundle.ps1') -WorkspaceDirectory $WorkspaceDirectory -EvidenceDirectory (Join-Path $evidence 'export') -Release
    $export = Get-Content -LiteralPath (Join-Path $evidence 'export/result.json') -Raw | ConvertFrom-Json
    if (!$export.passed -or !$export.releaseRequested) { throw 'Expected a successful release-template AAB export.' }
    & (Join-Path $PSScriptRoot 'Test-AndroidBundle.ps1') -BundlePath $export.bundle -TextureProfileDirectory $export.textureProfileDirectory -EvidenceDirectory (Join-Path $evidence 'validation') -ApkSigningKeyStore $keyStore -ApkSigningKeyAlias $env:GODOT_ANDROID_KEYSTORE_RELEASE_USER -StorePasswordFile $passwordFile -KeyPasswordFile $passwordFile -ExpectedUploadCertificateSha256 $public.uploadCertificateSha256 -ExpectedApkCertificateSha256 $public.uploadCertificateSha256
    $validation = Get-Content -LiteralPath (Join-Path $evidence 'validation/result.json') -Raw | ConvertFrom-Json
    if (!$validation.passed -or !$validation.releaseAcceptancePassed) { throw 'Android release signature or package validation failed.' }
    [xml]$manifest = [IO.File]::ReadAllText((Join-Path $evidence 'validation/manifest.xml'))
    $namespace = 'http://schemas.android.com/apk/res/android'
    if ($manifest.manifest.package -ne $public.applicationId -or $manifest.manifest.GetAttribute('versionCode',$namespace) -ne [string]$identity.androidVersionCode -or $manifest.manifest.GetAttribute('versionName',$namespace) -ne $identity.versionName) { throw 'Actual AAB manifest differs from the requested release identity.' }
    Copy-Item -LiteralPath $export.bundle -Destination (Join-Path $output ($identity.stem + '-android-arm64.aab'))
    Copy-Item -LiteralPath (Join-Path $evidence 'validation/universal.apk') -Destination (Join-Path $output ($identity.stem + '-android-arm64.apk'))
    $summary.passed = $true
} finally {
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name,$oldSigning[$name],'Process') }
    Remove-Item Env:ANDROID_UPLOAD_KEYSTORE_BASE64,Env:ANDROID_UPLOAD_KEY_ALIAS,Env:ANDROID_UPLOAD_KEYSTORE_PASSWORD -ErrorAction SilentlyContinue
    # Both generated paths are inside a fresh, explicitly resolved private directory.
    foreach ($file in @('upload.p12','password.txt')) {
        $path = Join-Path $privateDirectory $file
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path }
    }
    Remove-Item -LiteralPath $privateDirectory
    $summary.privateFilesRemoved = !(Test-Path -LiteralPath $privateDirectory)
    # Preserve diagnostic logs without accidentally retaining an exporter-echoed password.
    foreach ($root in @($evidence,[IO.Path]::GetFullPath($WorkspaceDirectory))) {
        if (!(Test-Path -LiteralPath $root)) { continue }
        foreach ($file in Get-ChildItem -LiteralPath $root -File -Recurse | Where-Object { $_.Extension -in @('.log','.txt','.cfg','.gradle','.properties','.json','.xml') -and $_.Length -lt 16MB }) {
            $content = [IO.File]::ReadAllText($file.FullName)
            if ($content.Contains($password)) { [IO.File]::WriteAllText($file.FullName,$content.Replace($password,'[REDACTED SIGNING PASSWORD]'),[Text.UTF8Encoding]::new($false)) }
        }
    }
    $password = $null
    $summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence 'result.json') -Encoding utf8
}
$summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'build-info.json') -Encoding utf8
Write-ReleaseChecksums $output
Write-Host "Release Android APK/AAB: $output; no Google Play upload or rollout."
