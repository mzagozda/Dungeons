#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ExportEvidenceDirectory,
    [Parameter(Mandatory)][string]$EvidenceDirectory,
    [Parameter(Mandatory)][ValidatePattern('^[A-Z0-9]{10}$')][string]$AppleTeamId,
    [Parameter(Mandatory)][ValidatePattern('^[A-Z0-9]{10}$')][string]$ApiKeyId,
    [Parameter(Mandatory)][guid]$ApiIssuerId,
    [Parameter(Mandatory)][string]$ApiKeyFile
)
. (Join-Path $PSScriptRoot 'Common.ps1')
if (!$IsMacOS) { throw 'Automatic iOS signing requires macOS/Xcode and real Apple credentials.' }
$priorDirectory = (Resolve-Path -LiteralPath $ExportEvidenceDirectory).Path
$priorFile = Join-Path $priorDirectory 'result.json'
$prior = Get-Content $priorFile -Raw | ConvertFrom-Json
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $script:RepoRoot 'artifacts')) + [IO.Path]::DirectorySeparatorChar
if (!$evidence.StartsWith($artifactRoot,[StringComparison]::Ordinal) -or (Test-Path $evidence) -or $evidence.StartsWith($priorDirectory + [IO.Path]::DirectorySeparatorChar,[StringComparison]::Ordinal)) { throw 'Signing evidence must be new and separate under repository artifacts.' }
$key = (Resolve-Path -LiteralPath $ApiKeyFile).Path
if ($key.StartsWith($script:RepoRoot + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetExtension($key) -ne '.p8') { throw 'Keep the real .p8 API key outside the repository and artifact/upload directories.' }
if ((Get-Item $key).Length -gt 16KB -or [IO.File]::ReadAllText($key) -notmatch '(?s)^-----BEGIN PRIVATE KEY-----\s+.+?\s+-----END PRIVATE KEY-----\s*$') { throw 'Expected an actual PEM API key file.' }
if (!$prior.passed -or !$prior.projectExportPassed -or !$prior.unsignedArchiveBuilt -or !$prior.archivePrerequisitesPassed -or !$prior.releaseTemplate -or $prior.appleTeamId -ne $AppleTeamId) { throw 'Actual matching release-template unsigned export/archive prerequisites must pass before Apple provisioning is contacted.' }
$project = (Resolve-Path -LiteralPath $prior.projectPath).Path
if (!$project.StartsWith($priorDirectory + [IO.Path]::DirectorySeparatorChar,[StringComparison]::Ordinal)) { throw 'Exported project path escapes its recorded evidence directory.' }
if ((Get-FileHash (Join-Path $project 'project.pbxproj')).Hash -ne $prior.projectPbxprojSha256) { throw 'Exported Xcode project changed.' }
$unsignedAudit = Get-Content (Join-Path $priorDirectory 'archive-prerequisites.json') -Raw | ConvertFrom-Json
if (!$unsignedAudit.passed) { throw 'Unsigned archive prerequisite report does not pass.' }
$bundleId = $unsignedAudit.bundleIdentifier
[void][IO.Directory]::CreateDirectory($evidence)
$summary = [ordered]@{passed=$false;automaticSigningRequested=$true;appleTeamId=$AppleTeamId;signingAcceptance=$false;archivePrerequisitesPassed=$false;exportedIpaVerified=$false;physicalAcceptance=$false;storeSubmissionReady=$false;uploaded=$false}
Push-Location $script:RepoRoot
try {
    $options = Join-Path $evidence 'ExportOptions.plist'
    Invoke-Checked python @('tools/prepare_ios_signing.py',$priorFile,'--team',$AppleTeamId,'--key-id',$ApiKeyId,'--issuer',$ApiIssuerId.ToString(),'--output',$options) -LogPath (Join-Path $evidence 'signing-plan.log') -Quiet
    $authentication = @('-allowProvisioningUpdates','-authenticationKeyPath',$key,'-authenticationKeyID',$ApiKeyId,'-authenticationKeyIssuerID',$ApiIssuerId.ToString())
    $archive = Join-Path $evidence 'BlockDungeons.xcarchive'
    # This opt-in operation authorizes Xcode's automatic Apple provisioning. It
    # never requests device registration or an App Store/TestFlight upload.
    Invoke-Checked xcodebuild (@('-project',$project,'-scheme','BlockDungeons','-configuration','Release','-sdk','iphoneos','-destination','generic/platform=iOS','-archivePath',$archive,'CODE_SIGNING_ALLOWED=YES','CODE_SIGN_STYLE=Automatic',"DEVELOPMENT_TEAM=$AppleTeamId",'archive') + $authentication) -TimeoutSeconds 1800 -LogPath (Join-Path $evidence 'archive.log') -Quiet
    Invoke-Checked python @('tools/audit_ios_archive.py',$archive,'--bundle-id',$bundleId,'--output',(Join-Path $evidence 'archive-prerequisites.json')) -LogPath (Join-Path $evidence 'archive-prerequisites.log') -Quiet
    $summary.archivePrerequisitesPassed = $true
    $export = Join-Path $evidence 'export'
    Invoke-Checked xcodebuild (@('-exportArchive','-archivePath',$archive,'-exportOptionsPlist',$options,'-exportPath',$export) + $authentication) -TimeoutSeconds 1800 -LogPath (Join-Path $evidence 'export.log') -Quiet
    $ipas = @(Get-ChildItem $export -File -Filter '*.ipa')
    if ($ipas.Count -ne 1) { throw 'Expected exactly one exported IPA.' }
    $unpacked = Join-Path $evidence 'unpacked'
    [IO.Compression.ZipFile]::ExtractToDirectory($ipas[0].FullName,$unpacked,$false)
    $apps = @(Get-ChildItem (Join-Path $unpacked 'Payload') -Directory -Filter '*.app')
    if ($apps.Count -ne 1) { throw 'Exported IPA lacks its single application.' }
    Invoke-Checked python @('tools/audit_ios_archive.py',$apps[0].FullName,'--application','--bundle-id',$bundleId,'--output',(Join-Path $evidence 'exported-app-prerequisites.json')) -LogPath (Join-Path $evidence 'exported-app-prerequisites.log') -Quiet
    Invoke-Checked codesign @('--verify','--deep','--strict',$apps[0].FullName) -LogPath (Join-Path $evidence 'codesign-verify.log') -Quiet
    Invoke-Checked codesign @('-dvv',$apps[0].FullName) -LogPath (Join-Path $evidence 'codesign-identity.log') -Quiet
    $signed = [IO.File]::ReadAllText((Join-Path $evidence 'codesign-identity.log'))
    if ($signed -notmatch ('(?m)^TeamIdentifier=' + [regex]::Escape($AppleTeamId) + '\s*$') -or $signed -notmatch '(?m)^Authority=(Apple Distribution|iPhone Distribution):') { throw 'Exported application lacks the expected Apple distribution team/identity.' }
    # Inspect the exported app's actual plist using plutil without dumping its
    # provisioning profile or credentials into the uploaded public reports.
    Invoke-Checked plutil @('-extract','CFBundleIdentifier','raw','-o','-',(Join-Path $apps[0].FullName 'Info.plist')) -LogPath (Join-Path $evidence 'bundle-id.txt') -Quiet
    if ([IO.File]::ReadAllText((Join-Path $evidence 'bundle-id.txt')).Trim() -ne $bundleId) { throw 'Exported IPA bundle identity differs.' }
    $summary.signingAcceptance = $true; $summary.exportedIpaVerified = $true; $summary.ipa = $ipas[0].FullName
    $summary.ipaSha256 = (Get-FileHash $ipas[0].FullName).Hash; $summary.passed = $true
}
catch { $summary.failure = $_.Exception.Message; throw }
finally { $summary | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $evidence 'result.json'); Pop-Location }
Write-Host "Automatic signed IPA export passed; physical/store submission remains separate: $evidence"
