#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$UnsignedBundle,
    [Parameter(Mandatory)][string]$SignedQaBundle,
    [Parameter(Mandatory)][string]$TextureProfileDirectory,
    [Parameter(Mandatory)][string]$EvidenceDirectory
)
. (Join-Path $PSScriptRoot 'Common.ps1')
$unsigned = (Resolve-Path -LiteralPath $UnsignedBundle).Path
$signed = (Resolve-Path -LiteralPath $SignedQaBundle).Path
$textureProfile = (Resolve-Path -LiteralPath $TextureProfileDirectory).Path
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
if (Test-Path -LiteralPath $evidence) { throw 'Bundle contract evidence must use a new directory.' }
[void][IO.Directory]::CreateDirectory($evidence)
$summary = [ordered]@{passed=$false;unsignedRejected=$false;tamperedPayloadRejected=$false;wrongPublisherRejected=$false;qaCannotBePublisher=$false;existingEvidencePreserved=$false;sourceBundlesUnchanged=$false;releaseAcceptance=$false}
$before = @((Get-FileHash -LiteralPath $unsigned).Hash,(Get-FileHash -LiteralPath $signed).Hash)
function Assert-Rejected([string]$Name,[string]$Bundle,[string]$Expected,[string]$Certificate) {
    $output = Join-Path $evidence $Name
    $parameters = @{BundlePath=$Bundle;TextureProfileDirectory=$textureProfile;EvidenceDirectory=$output;DebugApks=$true}
    if ($Certificate) { $parameters.ExpectedUploadCertificateSha256 = $Certificate }
    $rejected = $false
    try { & (Join-Path $PSScriptRoot 'Test-AndroidBundle.ps1') @parameters }
    catch { $rejected = $true }
    if (!$rejected) { throw "Bundle contract unexpectedly passed: $Name" }
    $result = Get-Content -LiteralPath (Join-Path $output 'result.json') -Raw | ConvertFrom-Json
    if ($result.passed -or $result.technicalValidationPassed -or $result.releaseAcceptancePassed -or $result.storeSubmissionReady -or $result.failure -notmatch $Expected) { throw "Wrong bundle rejection: $Name" }
    return $result
}
try {
    [void](Assert-Rejected 'unsigned' $unsigned 'signature is absent' $null)
    $summary.unsignedRejected = $true
    [void](Assert-Rejected 'wrong-publisher' $signed 'does not match the expected publisher' ('0' * 64))
    $summary.wrongPublisherRejected = $true
    $certificateText = [IO.File]::ReadAllText((Join-Path $evidence 'wrong-publisher/aab-certificates.pem'))
    $certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::CreateFromPem($certificateText)
    try {
        if ($certificate.Subject -notmatch '(?:^|,\s*)CN=Android Debug(?:,|$)') { throw 'Supply a clearly labelled Android Debug QA bundle, not a publisher artifact.' }
        $qaFingerprint = $certificate.GetCertHashString([Security.Cryptography.HashAlgorithmName]::SHA256)
    }
    finally { $certificate.Dispose() }
    [void](Assert-Rejected 'qa-as-publisher' $signed 'does not match the expected publisher' $qaFingerprint)
    $summary.qaCannotBePublisher = $true

    # Mutate an actual signed payload, preserving its signature files. Structure
    # validation alone can still pass; cryptographic verification must reject it.
    $tampered = Join-Path $evidence 'tampered.aab'
    Copy-Item -LiteralPath $signed -Destination $tampered
    $archive = [IO.Compression.ZipFile]::Open($tampered,[IO.Compression.ZipArchiveMode]::Update)
    try {
        $entry = $archive.Entries | Where-Object { $_.FullName -match '^[^/]+/assets/content/bootstrap\.json$' } | Select-Object -First 1
        if (!$entry) { throw 'Signed bundle has no JSON payload for the tamper contract.' }
        $path = $entry.FullName
        $reader = [IO.StreamReader]::new($entry.Open())
        try { $payload = $reader.ReadToEnd() } finally { $reader.Dispose() }
        $entry.Delete()
        $entry = $archive.CreateEntry($path)
        $writer = [IO.StreamWriter]::new($entry.Open(),[Text.UTF8Encoding]::new($false))
        try { $writer.Write($payload + "`n ") } finally { $writer.Dispose() }
        $summary.tamperedEntry = $path
    }
    finally { $archive.Dispose() }
    [void](Assert-Rejected 'tampered' $tampered 'jarsigner|signature' $null)
    $summary.tamperedPayloadRejected = $true

    $keep = Join-Path $evidence 'keep'
    [void][IO.Directory]::CreateDirectory($keep)
    $sentinel = Join-Path $keep 'sentinel.txt'; [IO.File]::WriteAllText($sentinel,'preserve this')
    $rejected = $false
    try { & (Join-Path $PSScriptRoot 'Test-AndroidBundle.ps1') -BundlePath $signed -TextureProfileDirectory $textureProfile -EvidenceDirectory $keep -DebugApks }
    catch { $rejected = $_.Exception.Message -match 'new evidence directory' }
    if (!$rejected -or [IO.File]::ReadAllText($sentinel) -ne 'preserve this' -or @(Get-ChildItem -LiteralPath $keep).Count -ne 1) { throw 'Existing bundle evidence was modified.' }
    $summary.existingEvidencePreserved = $true
    $after = @((Get-FileHash -LiteralPath $unsigned).Hash,(Get-FileHash -LiteralPath $signed).Hash)
    if ($before[0] -ne $after[0] -or $before[1] -ne $after[1]) { throw 'Bundle contract inputs changed.' }
    $summary.sourceBundlesUnchanged = $true; $summary.passed = $true
}
catch { $summary.failure = $_.Exception.Message; throw }
finally { $summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence 'result.json') -Encoding utf8 }
Write-Host "Android bundle failure contracts passed: $evidence"
