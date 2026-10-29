[CmdletBinding()]
param(
    [ValidateSet('Windows', 'Android', 'iOS')][string]$Platform = 'Windows',
    [string]$GodotPath,
    [ValidateRange(30, 1800)][int]$ExportTimeoutSeconds = 600,
    [switch]$Release,
    [switch]$Low,
    [switch]$SmokeTest,
    [switch]$Rendered
)
. (Join-Path $PSScriptRoot 'Common.ps1')
if ($Low -and $Platform -ne 'Android') { throw '-Low selects the Android Compatibility preset.' }
if ($SmokeTest -and $Platform -ne 'Windows') { throw '-SmokeTest is supported for Windows; use device QA for mobile.' }
if ($Rendered -and !$SmokeTest) { throw '-Rendered requires -SmokeTest.' }
$godot = Get-GodotPath -ExplicitPath $GodotPath
Assert-GodotVersion -Path $godot
switch ($Platform) {
    'Windows' { $preset = 'Windows Desktop'; $relativeOutput = 'artifacts/windows/BlockDungeons.exe' }
    'Android' { $preset = if ($Low) { 'Android Low' } else { 'Android' }; $relativeOutput = if ($Low) { 'artifacts/android/block_dungeons_low.apk' } else { 'artifacts/android/block_dungeons.apk' } }
    'iOS' { $preset = 'iOS'; $relativeOutput = 'artifacts/ios/block_dungeons.zip' }
}
$output = Join-Path $script:RepoRoot $relativeOutput
[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($output))
$mode = if ($Release) { '--export-release' } else { '--export-debug' }
$previousOutput = $null
$exportSucceeded = $false
$previousLowEnvironment = $env:DUNGEONS_ANDROID_LOW
$previousEditorEnvironment = $env:DUNGEONS_INCLUDE_EDITOR
$previousTextureEnvironment = $env:DUNGEONS_MOBILE_TEXTURE_DIR
$presetSections = [regex]::Split([IO.File]::ReadAllText((Join-Path $script:RepoRoot 'game/export_presets.cfg')), '(?m)(?=^\[preset\.\d+\]\r?$)')
$presetSection = @($presetSections | Where-Object { $_ -match ('(?m)^name="' + [regex]::Escape($preset) + '"\r?$') })
if ($presetSection.Count -ne 1) { throw "Expected one export preset named $preset." }
$featureMatch = [regex]::Match($presetSection[0], '(?m)^custom_features="([^"]*)"')
$includeEditor = @($featureMatch.Groups[1].Value.Split(',') | ForEach-Object Trim) -contains 'editor'
Push-Location $script:RepoRoot
Start-Transcript -Path (Join-Path ([IO.Path]::GetDirectoryName($output)) 'export-transcript.log') -Force | Out-Null
try {
    $env:DUNGEONS_INCLUDE_EDITOR = $includeEditor.ToString().ToLowerInvariant()
    Initialize-GodotEditor -Path $godot
    $importLog = [IO.Path]::ChangeExtension($output, '.import.log')
    $knownPreviewDiagnostics = Invoke-GodotImport -Path $godot -LogPath $importLog
    if (!$knownPreviewDiagnostics) { Write-Host "Godot import passed without engine errors: $importLog" }
    if ($Platform -in @('Android','iOS')) {
        $textureProject = Join-Path $script:RepoRoot ('artifacts/mobile-textures/export-' + [guid]::NewGuid().ToString('N'))
        & (Join-Path $PSScriptRoot 'Build-MobileTextureVariants.ps1') -GodotPath $godot -ProjectDirectory $textureProject
        $env:DUNGEONS_MOBILE_TEXTURE_DIR = $textureProject
    }
    if ($Platform -eq 'Android') {
        $env:DUNGEONS_ANDROID_LOW = if ($Low) { '1' } else { '0' }
        & (Join-Path $PSScriptRoot 'Install-AndroidTemplate.ps1')
        & (Join-Path $PSScriptRoot 'Install-AndroidHooks.ps1')
    }
    $exportArguments = @('--headless', '--path', 'game', $mode, $preset, $output)
    if (Test-Path -LiteralPath $output) {
        $previousOutput = $output + '.previous-' + [guid]::NewGuid().ToString('N')
        Move-Item -LiteralPath $output -Destination $previousOutput
    }
    Invoke-Checked -FilePath $godot -Arguments $exportArguments -TimeoutSeconds $ExportTimeoutSeconds -RejectEngineErrors -LogPath (Join-Path ([IO.Path]::GetDirectoryName($output)) 'export-native.log')
    if (!(Test-Path -LiteralPath $output)) { throw "Export did not produce $output" }
    if ($Platform -in @('Android','iOS') -and [IO.File]::ReadAllText((Join-Path ([IO.Path]::GetDirectoryName($output)) 'export-native.log')) -notmatch 'MOBILE_TEXTURE_EXPORT=PASS codec=ETC2 count=') { throw 'Mobile export did not pass its native texture boundary.' }
    if ($Platform -eq 'Android') {
        & (Join-Path $PSScriptRoot 'Test-ApkManifest.ps1') -ApkPath $output -Low:$Low
        & (Join-Path $PSScriptRoot 'Test-EditorExport.ps1') -ApkPath $output -IncludeEditor:$includeEditor
        $resourcePack = Join-Path $textureProject 'package-resources.zip'
        Invoke-Checked python @('tools/audit_mobile_texture_package.py',$output,$textureProject,'--report',(Join-Path $textureProject 'package-audit.json'),'--asset-pack',$resourcePack)
        Invoke-Checked $godot @('--headless','--path',$textureProject,'--script',(Join-Path $PSScriptRoot 'audit_packaged_mobile_textures.gd'),'--',$resourcePack,(Join-Path $textureProject 'manifest.json'),(Join-Path $textureProject 'package-native-audit.json')) -TimeoutSeconds 90 -LogPath (Join-Path $textureProject 'package-native-audit.log') -RejectEngineErrors -Quiet
        [ordered]@{profileDirectory=$textureProject; manifest=(Join-Path $textureProject 'manifest.json'); apkSha256=(Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash; packageAudit=(Join-Path $textureProject 'package-audit.json'); nativeAudit=(Join-Path $textureProject 'package-native-audit.json'); physicalAcceptance=$false} | ConvertTo-Json | Set-Content -LiteralPath ($output + '.textures.json') -Encoding UTF8
    }
    if ($Platform -eq 'Windows') {
        & (Join-Path $PSScriptRoot 'Test-EditorExport.ps1') -DesktopAssemblyPath (Join-Path ([IO.Path]::GetDirectoryName($output)) 'data_Dungeons.Game_windows_x86_64/Dungeons.Game.dll')
    }
    $exportSucceeded = $true
    if ($previousOutput) { Remove-Item -LiteralPath $previousOutput }
    if ($SmokeTest) {
        # Automated export validation must work without an attached audio endpoint.
        # Real output/listening is a separate manual platform acceptance check.
        $probeArguments = @('--audio-driver','Dummy')
        if (!$Rendered) { $probeArguments += '--headless' }
        $probeArguments += @('--', '--m0-smoke-test')
        if ($Rendered) { $probeArguments += "--m0-screenshot=$([IO.Path]::ChangeExtension($output, '.png'))" }
        Invoke-Probe -Executable $output -Arguments $probeArguments -LogPath ([IO.Path]::ChangeExtension($output, '.log')) -Packaged
        $editorLog = [IO.Path]::ChangeExtension($output, '.editor-assets.log')
        Invoke-Checked $output @('--headless','--','--qa-mode=palette') -LogPath $editorLog -TimeoutSeconds 45 -RejectEngineErrors -Quiet
        if ([IO.File]::ReadAllText($editorLog) -notmatch 'PACKAGED_EDITOR_PALETTE=PASS count=') { throw 'Exported desktop palette was not verified.' }
        $uiLog = [IO.Path]::ChangeExtension($output, '.ui-assets.log')
        Invoke-Checked $output @('--headless','--','--qa-mode=ui') -LogPath $uiLog -TimeoutSeconds 45 -RejectEngineErrors -Quiet
        if ([IO.File]::ReadAllText($uiLog) -notmatch 'PACKAGED_UI_THEME=PASS') { throw 'Exported desktop pixel theme was not verified.' }
    }
}
finally {
    $env:DUNGEONS_ANDROID_LOW = $previousLowEnvironment
    $env:DUNGEONS_INCLUDE_EDITOR = $previousEditorEnvironment
    $env:DUNGEONS_MOBILE_TEXTURE_DIR = $previousTextureEnvironment
    if (!$exportSucceeded -and $previousOutput) {
        if (Test-Path -LiteralPath $output) { Move-Item -LiteralPath $output -Destination ($output + '.failed-' + [guid]::NewGuid().ToString('N')) }
        Write-Warning "Export failed. Prior artifact retained at $previousOutput; it does not satisfy this export."
    }
    Stop-Transcript | Out-Null; Pop-Location
}
