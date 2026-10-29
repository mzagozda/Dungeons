#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$WorkspaceDirectory,
    [Parameter(Mandatory)][string]$EvidenceDirectory,
    [switch]$Low,
    [string]$GodotPath
)
. (Join-Path $PSScriptRoot 'Common.ps1')
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $script:RepoRoot 'artifacts')) + [IO.Path]::DirectorySeparatorChar
if (!$evidence.StartsWith($artifactRoot,[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $evidence)) { throw 'Bundle pipeline evidence must be a new directory under repository artifacts.' }
if (!$env:JAVA_HOME) { throw 'Set JAVA_HOME to the pinned JDK 17.' }
[void][IO.Directory]::CreateDirectory($evidence)
$summary = [ordered]@{passed=$false;low=$Low.IsPresent;publisherAcceptance=$false;storeSubmissionReady=$false;physicalAcceptance=$false}
$password = Join-Path $evidence 'temporary-qa.password'
$windows = [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT
$keytool = Join-Path $env:JAVA_HOME $(if ($windows) { 'bin/keytool.exe' } else { 'bin/keytool' })
$jarsigner = Join-Path $env:JAVA_HOME $(if ($windows) { 'bin/jarsigner.exe' } else { 'bin/jarsigner' })
try {
    & (Join-Path $PSScriptRoot 'Install-Bundletool.ps1')
    $exportDirectory = Join-Path $evidence 'export'
    & (Join-Path $PSScriptRoot 'Export-AndroidBundle.ps1') -WorkspaceDirectory $WorkspaceDirectory -EvidenceDirectory $exportDirectory -GodotPath $GodotPath -Low:$Low
    $export = Get-Content -LiteralPath (Join-Path $exportDirectory 'result.json') -Raw | ConvertFrom-Json
    if (!$export.passed -or $export.releaseRequested) { throw 'The pipeline requires a successful debug bundle export.' }
    $unsigned = $export.bundle
    $signed = Join-Path $evidence 'block_dungeons_qa_signed.aab'
    $key = Join-Path $script:RepoRoot '.toolchain/bundletool/dungeons-validation.keystore'
    [IO.File]::WriteAllText($password,'android',[Text.UTF8Encoding]::new($false))
    if (!(Test-Path -LiteralPath $key)) {
        Invoke-Checked $keytool @('-genkeypair','-keystore',$key,'-alias','androiddebugkey','-keyalg','RSA','-keysize','2048','-validity','365','-dname','CN=Android Debug,O=Block Dungeons QA,C=PL','-storepass:file',$password,'-keypass:file',$password,'-noprompt') -LogPath (Join-Path $evidence 'qa-key.log') -Quiet
    }
    Invoke-Checked $jarsigner @('-keystore',$key,'-storepass:file',$password,'-keypass:file',$password,'-signedjar',$signed,$unsigned,'androiddebugkey') -LogPath (Join-Path $evidence 'qa-signing.log') -Quiet
    $validation = Join-Path $evidence 'validation'
    & (Join-Path $PSScriptRoot 'Test-AndroidBundle.ps1') -BundlePath $signed -TextureProfileDirectory $export.textureProfileDirectory -EvidenceDirectory $validation -DebugApks -Low:$Low -GodotPath $GodotPath
    & (Join-Path $PSScriptRoot 'Test-AndroidBundleContracts.ps1') -UnsignedBundle $unsigned -SignedQaBundle $signed -TextureProfileDirectory $export.textureProfileDirectory -EvidenceDirectory (Join-Path $evidence 'contracts')
    $checked = Get-Content -LiteralPath (Join-Path $validation 'result.json') -Raw | ConvertFrom-Json
    if (!$checked.passed -or !$checked.technicalValidationPassed -or !$checked.zipalign16Passed -or $checked.releaseAcceptancePassed -or $checked.storeSubmissionReady -or $checked.physicalAcceptance) { throw 'QA bundle validation must pass technically while leaving delivery acceptance false.' }
    & (Join-Path $PSScriptRoot 'Test-AndroidSplits.ps1') -BundlePath $signed -UniversalValidationDirectory $validation -TextureProfileDirectory $export.textureProfileDirectory -EvidenceDirectory (Join-Path $evidence 'splits') -Low:$Low -GodotPath $GodotPath
    $splits = Get-Content (Join-Path $evidence 'splits/result.json') -Raw | ConvertFrom-Json
    if (!$splits.passed -or @($splits.profiles).Count -ne 3 -or @($splits.negativeProfiles).Count -ne 2 -or $splits.physicalAcceptance -or $splits.releaseAcceptance) { throw 'Offline split matrix is incomplete or claims physical/publisher acceptance.' }
    $summary.offlineSplitMatrixPassed = $true
    $summary.bundleSha256 = $checked.bundleSha256; $summary.generatedApkSha256 = $checked.generatedApkSha256
    $summary.passed = $true
}
catch { $summary.failure = $_.Exception.Message; throw }
finally {
    if (Test-Path -LiteralPath $password) { Remove-Item -LiteralPath $password }
    $summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence 'result.json') -Encoding utf8
}
Write-Host "Android bundle QA pipeline passed (Low=$($Low.IsPresent)): $evidence"
