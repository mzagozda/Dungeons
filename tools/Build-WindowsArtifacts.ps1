#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$IdentityPath,
    [string]$ExportDirectory = 'artifacts/windows',
    [string]$OutputDirectory = 'artifacts/release-ci/windows',
    [string]$EvidenceDirectory = 'artifacts/release-ci/windows-checks',
    [string]$CompilerPath,
    [string]$TimestampUrl = 'http://timestamp.digicert.com'
)
. (Join-Path $PSScriptRoot 'ReleaseArtifacts.ps1')
if (!$IsWindows) { throw 'Windows installer packaging requires Windows.' }
$identity = Read-ReleaseIdentity $IdentityPath
$metadata = Get-Content -LiteralPath (Join-Path $script:RepoRoot 'platform/store/en-US.json') -Raw | ConvertFrom-Json
if ($metadata.release.versionName -ne $identity.versionName) { throw 'Release identity and publisher version differ.' }
if (!$CompilerPath) { $CompilerPath = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe' }
if (!(Test-Path -LiteralPath $CompilerPath -PathType Leaf)) { throw 'Inno Setup 6.3+ is required; supply -CompilerPath.' }
$export = (Resolve-Path -LiteralPath $ExportDirectory).Path
foreach ($relative in @('BlockDungeons.exe','data_Dungeons.Game_windows_x86_64/Dungeons.Game.dll','data_Dungeons.Game_windows_x86_64/coreclr.dll')) {
    if (!(Test-Path -LiteralPath (Join-Path $export $relative) -PathType Leaf)) { throw "Incomplete Windows export: $relative" }
}
foreach ($item in @(Get-Item -LiteralPath (Join-Path $export 'data_Dungeons.Game_windows_x86_64')) + @(Get-ChildItem -LiteralPath (Join-Path $export 'data_Dungeons.Game_windows_x86_64') -Force -Recurse)) {
    if ($item.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) { throw 'Linked Windows runtime inputs are forbidden.' }
}
$actualVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $export 'BlockDungeons.exe')).ProductVersion
if ($actualVersion -ne $identity.versionName) { throw 'Export executable version differs from release identity; make a fresh release export.' }
$hasPfx = !!$env:WINDOWS_SIGNING_PFX_BASE64
if ($hasPfx -ne (!!$env:WINDOWS_SIGNING_PFX_PASSWORD)) { throw 'Set both WINDOWS_SIGNING_PFX_BASE64 and WINDOWS_SIGNING_PFX_PASSWORD, or neither for unsigned Windows builds.' }
if ($TimestampUrl -notmatch '^https?://[a-zA-Z0-9./:_-]+$') { throw 'Invalid Authenticode timestamp URL.' }
$output = New-ReleaseDirectory $OutputDirectory
$evidence = New-ReleaseDirectory $EvidenceDirectory
if ($output.StartsWith($evidence + [IO.Path]::DirectorySeparatorChar) -or $evidence.StartsWith($output + [IO.Path]::DirectorySeparatorChar) -or $output -eq $evidence) { throw 'Output and evidence directories must not overlap.' }
$payload = Join-Path $evidence 'payload'
[void][IO.Directory]::CreateDirectory($payload)
$certificate = $null
$importedCertificate = $false
$summary = [ordered]@{passed=$false;identity=$identity;authenticodeSigned=$false;physicalAcceptance=$false}
try {
    Copy-Item -LiteralPath (Join-Path $export 'BlockDungeons.exe') -Destination $payload
    Copy-Item -LiteralPath (Join-Path $export 'data_Dungeons.Game_windows_x86_64') -Destination $payload -Recurse
    foreach ($file in Get-ChildItem -LiteralPath $payload -Force -Recurse) {
        if ($file.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) { throw 'Linked Windows payload files are forbidden.' }
        if ($file.Extension -in @('.pdb','.log','.trx')) { Remove-Item -LiteralPath $file.FullName }
    }
    Copy-Item -LiteralPath (Join-Path $script:RepoRoot 'THIRD_PARTY_NOTICES.md') -Destination $payload
    $identity | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $payload 'build-info.json') -Encoding utf8
    $arguments = @('/Qp',"/DPayloadDir=$payload","/DOutputDir=$output","/DArtifactStem=$($identity.stem)","/DAppVersion=$($identity.versionName)")
    foreach ($pair in @{AppPublisher=$metadata.publisher.name;AppSupportUrl=$metadata.publisher.supportUrl;AppCopyright=$metadata.publisher.copyright}.GetEnumerator()) {
        if ($pair.Value -match '["\r\n{}]') { throw 'Installer metadata contains unsupported characters.' }
        $arguments += "/D$($pair.Key)=$($pair.Value)"
    }
    if ($hasPfx) {
        $sdkBin = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin'
        $signtool = Get-ChildItem -LiteralPath $sdkBin -Directory | Where-Object Name -Match '^10\.\d+\.\d+\.\d+$' | Sort-Object { [version]$_.Name } -Descending | ForEach-Object { Join-Path $_.FullName 'x64/signtool.exe' } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
        if (!$signtool) { throw 'Windows SDK signtool.exe is required for Authenticode signing.' }
        $bytes = [Convert]::FromBase64String($env:WINDOWS_SIGNING_PFX_BASE64)
        try {
            $inspection = [Security.Cryptography.X509Certificates.X509CertificateLoader]::LoadPkcs12($bytes,$env:WINDOWS_SIGNING_PFX_PASSWORD,[Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
            try {
                if (!$inspection.HasPrivateKey -or $inspection.NotBefore.ToUniversalTime() -gt [DateTime]::UtcNow -or $inspection.NotAfter.ToUniversalTime() -le [DateTime]::UtcNow) { throw 'Windows signing certificate needs a currently valid private key.' }
                if (Test-Path -LiteralPath ('Cert:\CurrentUser\My\' + $inspection.Thumbprint)) { throw 'Use a clean signing runner; refusing to replace an existing certificate/private key.' }
            } finally { $inspection.Dispose() }
            # UserKeySet without PersistKeySet also deletes the transient key if store import fails.
            $certificate = [Security.Cryptography.X509Certificates.X509CertificateLoader]::LoadPkcs12($bytes,$env:WINDOWS_SIGNING_PFX_PASSWORD,[Security.Cryptography.X509Certificates.X509KeyStorageFlags]::UserKeySet)
        } finally { [Array]::Clear($bytes) }
        if (!$certificate.HasPrivateKey -or $certificate.NotAfter.ToUniversalTime() -le [DateTime]::UtcNow) { throw 'Windows signing certificate needs a valid private key.' }
        $store = [Security.Cryptography.X509Certificates.X509Store]::new('My','CurrentUser')
        try { $store.Open('ReadWrite'); $store.Add($certificate); $importedCertificate = $true } finally { $store.Close() }
        Remove-Item Env:WINDOWS_SIGNING_PFX_BASE64,Env:WINDOWS_SIGNING_PFX_PASSWORD
        Invoke-Checked $signtool @('sign','/sha1',$certificate.Thumbprint,'/fd','SHA256','/tr',$TimestampUrl,'/td','SHA256',(Join-Path $payload 'BlockDungeons.exe')) -LogPath (Join-Path $evidence 'sign-game.log') -Quiet
        # $q is Inno's quoting token, not a shell substitution; no password reaches argv.
        $signCommand = '$q' + $signtool + '$q sign /sha1 ' + $certificate.Thumbprint + ' /fd SHA256 /tr ' + $TimestampUrl + ' /td SHA256 $f'
        $arguments += @('/DSignedBuild',('/DUninstallerDir=' + (Join-Path $evidence 'signed-uninstallers')),('/STaskscapeRelease=' + $signCommand))
    }
    Remove-Item Env:WINDOWS_SIGNING_PFX_BASE64,Env:WINDOWS_SIGNING_PFX_PASSWORD -ErrorAction SilentlyContinue
    $arguments += (Join-Path $script:RepoRoot 'platform/windows/BlockDungeons.iss')
    & $CompilerPath @arguments *> (Join-Path $evidence 'inno-compile.log')
    if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed; see inno-compile.log.' }
    $installer = Join-Path $output ($identity.stem + '-windows-x64-setup.exe')
    if (!(Test-Path -LiteralPath $installer)) { throw 'Inno Setup did not produce the expected installer.' }
    if ($certificate) {
        foreach ($file in @($installer,(Join-Path $payload 'BlockDungeons.exe'))) {
            Invoke-Checked $signtool @('verify','/pa','/all',$file) -LogPath (Join-Path $evidence ((Split-Path $file -Leaf) + '.signature.log')) -Quiet
            $signature = Get-AuthenticodeSignature -LiteralPath $file
            if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne $certificate.Thumbprint) { throw 'Windows artifact signature differs from the selected identity.' }
        }
        $summary.authenticodeSigned = $true
        $summary.signerThumbprint = $certificate.Thumbprint
    }
    $portable = Join-Path $output ($identity.stem + '-windows-x64-portable.zip')
    [IO.Compression.ZipFile]::CreateFromDirectory($payload,$portable,[IO.Compression.CompressionLevel]::Optimal,$false)
    $summary.installer = $installer; $summary.portable = $portable; $summary.passed = $true
    $summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'build-info.json') -Encoding utf8
    Write-ReleaseChecksums $output
} finally {
    Remove-Item Env:WINDOWS_SIGNING_PFX_BASE64,Env:WINDOWS_SIGNING_PFX_PASSWORD -ErrorAction SilentlyContinue
    if ($certificate) {
        if ($importedCertificate) { Remove-Item -Path ('Cert:\CurrentUser\My\' + $certificate.Thumbprint) -DeleteKey }
        $certificate.Dispose()
    }
    $summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence 'result.json') -Encoding utf8
}
Write-Host "Windows installer and portable ZIP: $output (Authenticode signed: $($summary.authenticodeSigned))"
