#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BundlePath,
    [Parameter(Mandatory)][string]$UniversalValidationDirectory,
    [Parameter(Mandatory)][string]$TextureProfileDirectory,
    [Parameter(Mandatory)][string]$EvidenceDirectory,
    [switch]$Low,
    [string]$GodotPath
)
. (Join-Path $PSScriptRoot 'Common.ps1')
$bundle = (Resolve-Path -LiteralPath $BundlePath).Path
$validated = (Resolve-Path -LiteralPath $UniversalValidationDirectory).Path
$profile = (Resolve-Path -LiteralPath $TextureProfileDirectory).Path
$reference = Get-Content (Join-Path $validated 'result.json') -Raw | ConvertFrom-Json
$signer = Get-Content (Join-Path $validated 'apk-release/result.json') -Raw | ConvertFrom-Json
$bundleSha = (Get-FileHash $bundle).Hash
if (!$reference.passed -or !$reference.technicalValidationPassed -or !$reference.debugApkValidation -or $reference.bundleSha256 -ne $bundleSha -or !$signer.verifiedSignature -or @($signer.certificateSha256).Count -ne 1) { throw 'A successful matching QA universal-APK validation is required first.' }
$universal = Join-Path $validated 'universal.apk'
if ((Get-FileHash $universal).Hash -ine $reference.generatedApkSha256) { throw 'Validated universal APK changed.' }
$certificate = $signer.certificateSha256[0]
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $script:RepoRoot 'artifacts')) + [IO.Path]::DirectorySeparatorChar
if (!$evidence.StartsWith($artifactRoot,[StringComparison]::OrdinalIgnoreCase) -or (Test-Path $evidence)) { throw 'Split evidence must use a new directory under repository artifacts.' }
$androidSdk = if ($env:ANDROID_HOME) { $env:ANDROID_HOME } else { $env:ANDROID_SDK_ROOT }
if (!$env:JAVA_HOME -or !$androidSdk) { throw 'Set JAVA_HOME and ANDROID_HOME/ANDROID_SDK_ROOT for offline split validation.' }
$java = Join-Path $env:JAVA_HOME $(if ($IsWindows) { 'bin/java.exe' } else { 'bin/java' })
$sdkTools = Get-ChildItem (Join-Path $androidSdk 'build-tools') -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$aapt = Join-Path $sdkTools.FullName $(if ($IsWindows) { 'aapt.exe' } else { 'aapt' })
$zipalign = Join-Path $sdkTools.FullName $(if ($IsWindows) { 'zipalign.exe' } else { 'zipalign' })
$apksigner = Join-Path $sdkTools.FullName 'lib/apksigner.jar'
$pin = Get-Content (Join-Path $PSScriptRoot 'bundletool.json') -Raw | ConvertFrom-Json
$jar = Join-Path $script:RepoRoot ('.toolchain/bundletool/' + $pin.file)
if ((Get-FileHash $jar).Hash -ine $pin.sha256) { throw 'Pinned bundletool hash differs.' }
$godot = Get-GodotPath -ExplicitPath $GodotPath
[void][IO.Directory]::CreateDirectory($evidence)
$password = Join-Path $evidence 'temporary-qa.password'
$summary = [ordered]@{passed=$false;offlineSelectionOnly=$true;physicalAcceptance=$false;releaseAcceptance=$false;storeSubmissionReady=$false;bundleSha256=$bundleSha;low=$Low.IsPresent;profiles=@();negativeProfiles=@()}
$resourceCache = @{}
Push-Location $script:RepoRoot
try {
    [IO.File]::WriteAllText($password,'android',[Text.UTF8Encoding]::new($false))
    $set = Join-Path $evidence 'default.apks'
    Invoke-Checked $java @('-jar',$jar,'build-apks',"--bundle=$bundle","--output=$set",('--ks=' + (Join-Path $script:RepoRoot '.toolchain/bundletool/dungeons-validation.keystore')),'--ks-key-alias=androiddebugkey',"--ks-pass=file:$password","--key-pass=file:$password") -TimeoutSeconds 300 -LogPath (Join-Path $evidence 'build-apks.log') -Quiet
    foreach ($sdk in @(26,34,36)) {
        $case = Join-Path $evidence "synthetic-api-$sdk"; [void][IO.Directory]::CreateDirectory($case)
        $spec = [ordered]@{supportedAbis=@('arm64-v8a');supportedLocales=@('en','pl');screenDensity=480;sdkVersion=$sdk}
        $specFile = Join-Path $case 'device-spec.json'; $spec | ConvertTo-Json | Set-Content $specFile
        $selected = Join-Path $case 'apks'
        Invoke-Checked $java @('-jar',$jar,'extract-apks',"--apks=$set","--output-dir=$selected","--device-spec=$specFile") -TimeoutSeconds 120 -LogPath (Join-Path $case 'extract.log') -Quiet
        $files = @(foreach ($apk in Get-ChildItem $selected -Filter '*.apk' -File) {
            $badging = Join-Path $case ($apk.BaseName + '.badging.txt'); $signature = Join-Path $case ($apk.BaseName + '.signature.txt')
            Invoke-Checked $aapt @('dump','badging',$apk.FullName) -LogPath $badging -Quiet
            Invoke-Checked $java @('-jar',$apksigner,'verify','--verbose','--print-certs',$apk.FullName) -LogPath $signature -Quiet
            Invoke-Checked $zipalign @('-c','-P','16','-v','4',$apk.FullName) -LogPath (Join-Path $case ($apk.BaseName + '.zipalign.txt')) -Quiet
            [ordered]@{apk=$apk.FullName;badging=$badging;signature=$signature;zipalign16Passed=$true}
        })
        $plan = Join-Path $case 'plan.json'; [ordered]@{deviceSpec=$spec;files=$files} | ConvertTo-Json -Depth 6 | Set-Content $plan
        $checked = Join-Path $case 'payload.json'
        Invoke-Checked python @('tools/audit_android_splits.py',$plan,'--reference-apk',$universal,'--reference-badging',(Join-Path $validated 'apk-release/badging.txt'),'--certificate',$certificate,'--output',$checked) -LogPath (Join-Path $case 'payload.log') -Quiet
        $result = Get-Content $checked -Raw | ConvertFrom-Json
        if (!$result.passed -or $result.nativeLibraryCount -ne 11) { throw 'Selected split set lacks the complete eleven-library payload.' }
        & (Join-Path $PSScriptRoot 'Test-ApkManifest.ps1') -ApkPath $result.baseApk -Low:$Low
        $manifestText = [IO.File]::ReadAllText([IO.Path]::ChangeExtension($result.baseApk,'.manifest.txt'))
        $renderer = if ($Low) { 'gl_compatibility' } else { 'mobile' }
        if ($manifestText -notmatch ('(?s)org\.godotengine\.rendering\.method[^\r\n]*\r?\n\s+A: android:value[^\r\n]*"' + $renderer + '"')) { throw 'Selected base APK renderer differs from the requested profile.' }
        & (Join-Path $PSScriptRoot 'Test-EditorExport.ps1') -ApkPath $result.assetApk
        $assetSha = (Get-FileHash $result.assetApk).Hash
        if (!$resourceCache.ContainsKey($assetSha)) {
            $resources = Join-Path $case 'package-resources.zip'
            Invoke-Checked python @('tools/audit_mobile_texture_package.py',$result.assetApk,$profile,'--report',(Join-Path $case 'textures.json'),'--asset-pack',$resources) -LogPath (Join-Path $case 'textures.log') -Quiet
            Invoke-Checked $godot @('--headless','--path',$profile,'--script',(Join-Path $PSScriptRoot 'audit_packaged_mobile_textures.gd'),'--',$resources,(Join-Path $profile 'manifest.json'),(Join-Path $case 'native-textures.json')) -TimeoutSeconds 90 -LogPath (Join-Path $case 'native-textures.log') -RejectEngineErrors -Quiet
            $resourceCache[$assetSha] = $case
        }
        $summary.profiles += [ordered]@{sdk=$sdk;synthetic=$true;apks=$result.files.Count;assetEntries=$result.assetEntries;nativeLibraries=$result.nativeLibraryCount;aggregateBytes=$result.aggregateBytes;payloadMatchesUniversal=$result.matchesUniversalPayloadBytes;signaturesMatched=$true;zipalign16Passed=$true;assetApkSha256=$assetSha;nativeTextureAuditEvidence=$resourceCache[$assetSha];passed=$true}
    }
    foreach ($negative in @(@{name='below-minimum-api25';sdk=25;abi='arm64-v8a'},@{name='unsupported-x86-64';sdk=36;abi='x86_64'})) {
        $spec = Join-Path $evidence ($negative.name + '.json')
        [ordered]@{supportedAbis=@($negative.abi);supportedLocales=@('en','pl');screenDensity=480;sdkVersion=$negative.sdk} | ConvertTo-Json | Set-Content $spec
        $output = Join-Path $evidence $negative.name; $log = Join-Path $evidence ($negative.name + '.log'); $rejected = $false
        try { Invoke-Checked $java @('-jar',$jar,'extract-apks',"--apks=$set","--output-dir=$output","--device-spec=$spec") -LogPath $log -Quiet }
        catch { $rejected = $true }
        if (!$rejected -or [IO.File]::ReadAllText($log) -notmatch 'IncompatibleDeviceException' -or @(Get-ChildItem $output -Filter '*.apk' -Recurse -ErrorAction SilentlyContinue).Count -ne 0) { throw 'An incompatible synthetic profile was not cleanly rejected.' }
        $summary.negativeProfiles += [ordered]@{name=$negative.name;rejected=$true}
    }
    if ((Get-FileHash $bundle).Hash -ne $bundleSha -or (Get-FileHash $universal).Hash -ine $reference.generatedApkSha256) { throw 'Input bundle/reference changed during split validation.' }
    $summary.inputUnchanged = $true; $summary.passed = $true
}
catch { $summary.failure = $_.Exception.Message; throw }
finally {
    if (Test-Path $password) { Remove-Item -LiteralPath $password }
    $summary | ConvertTo-Json -Depth 7 | Set-Content (Join-Path $evidence 'result.json')
    Pop-Location
}
Write-Host "Offline synthetic Android split matrix passed; physical/publisher acceptance remains pending: $evidence"
