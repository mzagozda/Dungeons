#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$WorkspaceDirectory,
    [Parameter(Mandatory)][string]$EvidenceDirectory,
    [string]$SigningDirectory,
    [switch]$Low
)
. (Join-Path $PSScriptRoot 'Common.ps1')
if (!$IsWindows) { throw 'This local signing profile uses Windows CurrentUser DPAPI.' }
if (!$SigningDirectory) { $SigningDirectory = Join-Path $env:LOCALAPPDATA 'Taskscape/BlockDungeons/Signing' }
$signingRoot = (Resolve-Path -LiteralPath $SigningDirectory).Path
$profile = Get-Content -LiteralPath (Join-Path $signingRoot 'signing.json') -Raw | ConvertFrom-Json
$public = Get-Content -LiteralPath (Join-Path $script:RepoRoot 'platform/android/play-console.json') -Raw | ConvertFrom-Json
foreach ($path in @($profile.keyStorePath,$profile.protectedPasswordPath)) {
    $resolved = (Resolve-Path -LiteralPath $path).Path
    if (!$resolved.StartsWith($signingRoot + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or (Get-Item -LiteralPath $resolved).Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) { throw 'Private signing inputs must be regular files inside the protected signing directory.' }
}
if (!(Get-Acl -LiteralPath $signingRoot).AreAccessRulesProtected -or $profile.uploadCertificateSha256 -ne $public.uploadCertificateSha256) { throw 'Protected local signing profile differs from the expected public upload identity.' }
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $script:RepoRoot 'artifacts')) + [IO.Path]::DirectorySeparatorChar
if (!$evidence.StartsWith($artifactRoot,[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $evidence)) { throw 'Publisher evidence must be new and under repository artifacts.' }
[void][IO.Directory]::CreateDirectory($evidence)
$summary = [ordered]@{passed=$false;releaseBundleBuilt=$false;publisherUploadSignatureVerified=$false;localApkSetVerified=$false;playUploadRegistrationVerified=$false;playDistributionApksVerified=$false;storeSubmissionReady=$false;physicalAcceptance=$false}
$names = @('GODOT_ANDROID_KEYSTORE_RELEASE_PATH','GODOT_ANDROID_KEYSTORE_RELEASE_USER','GODOT_ANDROID_KEYSTORE_RELEASE_PASSWORD','JAVA_HOME','ANDROID_HOME')
$previous = @{}
foreach ($name in $names) { $previous[$name] = [Environment]::GetEnvironmentVariable($name,'Process') }
$passwordFile = Join-Path $signingRoot ('validation-password-' + [guid]::NewGuid().ToString('N') + '.txt')
$secure = $null; $password = $null
try {
    $secure = ConvertTo-SecureString ([IO.File]::ReadAllText($profile.protectedPasswordPath).Trim())
    $password = [PSCredential]::new('upload',$secure).GetNetworkCredential().Password
    $env:GODOT_ANDROID_KEYSTORE_RELEASE_PATH = $profile.keyStorePath
    $env:GODOT_ANDROID_KEYSTORE_RELEASE_USER = $profile.keyAlias
    $env:GODOT_ANDROID_KEYSTORE_RELEASE_PASSWORD = $password
    if (!$env:JAVA_HOME) { $env:JAVA_HOME = Join-Path $script:RepoRoot '.toolchain/jdk17/jdk-17.0.20.1+1' }
    if (!$env:ANDROID_HOME) { $env:ANDROID_HOME = Join-Path ${env:ProgramFiles(x86)} 'Android/android-sdk' }
    & (Join-Path $PSScriptRoot 'Export-AndroidBundle.ps1') -WorkspaceDirectory $WorkspaceDirectory -EvidenceDirectory (Join-Path $evidence 'export') -Release -Low:$Low
    $export = Get-Content -LiteralPath (Join-Path $evidence 'export/result.json') -Raw | ConvertFrom-Json
    if (!$export.passed) { throw 'Release bundle export failed.' }
    $summary.releaseBundleBuilt = $true; $summary.bundle = $export.bundle; $summary.bundleSha256 = $export.bundleSha256
    # Validator tools consume a short-lived password file, never command-line plaintext.
    $stream = [IO.File]::Open($passwordFile,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write)
    try { $bytes = [Text.UTF8Encoding]::new($false).GetBytes($password); $stream.Write($bytes); [Array]::Clear($bytes) } finally { $stream.Dispose() }
    & (Join-Path $PSScriptRoot 'Test-AndroidBundle.ps1') -BundlePath $export.bundle -TextureProfileDirectory $export.textureProfileDirectory -EvidenceDirectory (Join-Path $evidence 'validation') -ApkSigningKeyStore $profile.keyStorePath -ApkSigningKeyAlias $profile.keyAlias -StorePasswordFile $passwordFile -KeyPasswordFile $passwordFile -ExpectedUploadCertificateSha256 $public.uploadCertificateSha256 -ExpectedApkCertificateSha256 $public.uploadCertificateSha256 -Low:$Low
    $validation = Get-Content -LiteralPath (Join-Path $evidence 'validation/result.json') -Raw | ConvertFrom-Json
    if (!$validation.passed) { throw 'Publisher signature or local APK-set validation failed.' }
    $summary.publisherUploadSignatureVerified = $true; $summary.localApkSetVerified = $true
    $summary.uploadCertificateSha256 = $public.uploadCertificateSha256
    # These APKs use our upload key for offline validation. Google-delivered APKs
    # use the separate Play-managed certificate and require actual Play evidence.
    $summary.localApkCertificateSha256 = $public.uploadCertificateSha256
    $summary.playAppSigningCertificateSha256 = $public.playAppSigningCertificateSha256
    $summary.passed = $true
}
catch { $summary.failure = $_.Exception.Message; throw }
finally {
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name,$previous[$name],'Process') }
    if (Test-Path -LiteralPath $passwordFile) { Remove-Item -LiteralPath $passwordFile }
    # Godot/Gradle output is retained for diagnosis, with any accidental literal
    # password occurrence removed before evidence is delivered or displayed.
    $redacted = 0
    if ($password) {
        foreach ($root in @($evidence,[IO.Path]::GetFullPath($WorkspaceDirectory))) {
            if (!(Test-Path -LiteralPath $root)) { continue }
            foreach ($file in Get-ChildItem -LiteralPath $root -File -Recurse | Where-Object { $_.Extension -in @('.log','.txt','.cfg','.gradle','.properties','.json','.xml') -and $_.Length -lt 16MB }) {
                $text = [IO.File]::ReadAllText($file.FullName)
                if ($text.Contains($password)) { [IO.File]::WriteAllText($file.FullName,$text.Replace($password,'[REDACTED SIGNING PASSWORD]'),[Text.UTF8Encoding]::new($false)); $redacted++ }
            }
        }
    }
    $summary.temporaryPasswordRemoved = !(Test-Path -LiteralPath $passwordFile)
    $summary.redactedFiles = $redacted
    if ($secure) { $secure.Dispose() }; $password = $null
    $summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence 'result.json') -Encoding utf8
}
Write-Host "Signed release bundle and local APK validation: $evidence (passed: $($summary.passed)); no Play upload or rollout."
