#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$WorkspaceDirectory,
    [Parameter(Mandatory)][string]$EvidenceDirectory,
    [string]$GodotPath,
    [switch]$Release,
    [switch]$Low
)
. (Join-Path $PSScriptRoot 'Common.ps1')
if (!$env:JAVA_HOME -or !($env:ANDROID_HOME -or $env:ANDROID_SDK_ROOT)) { throw 'Set the pinned JDK 17 and Android SDK paths before exporting.' }
if ($Release -and (!$env:GODOT_ANDROID_KEYSTORE_RELEASE_PATH -or !$env:GODOT_ANDROID_KEYSTORE_RELEASE_USER -or !$env:GODOT_ANDROID_KEYSTORE_RELEASE_PASSWORD -or !(Test-Path -LiteralPath $env:GODOT_ANDROID_KEYSTORE_RELEASE_PATH -PathType Leaf))) { throw 'Release export requires GODOT_ANDROID_KEYSTORE_RELEASE_PATH, GODOT_ANDROID_KEYSTORE_RELEASE_USER and GODOT_ANDROID_KEYSTORE_RELEASE_PASSWORD; supply real signing configuration through the process environment.' }
$sourceRoot = $script:RepoRoot
$workspace = [IO.Path]::GetFullPath($WorkspaceDirectory)
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $sourceRoot 'artifacts')) + [IO.Path]::DirectorySeparatorChar
foreach ($target in @($workspace,$evidence)) {
    if (!$target.StartsWith($artifactRoot,[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $target)) { throw 'Bundle workspace and evidence must be new directories under repository artifacts.' }
}
if ($workspace -eq $evidence -or $workspace.StartsWith($evidence + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or $evidence.StartsWith($workspace + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Bundle workspace and evidence must not overlap.' }
$godot = Get-GodotPath -ExplicitPath $GodotPath
Assert-GodotVersion -Path $godot
[void][IO.Directory]::CreateDirectory($evidence)
$status = [ordered]@{passed=$false;releaseRequested=$Release.IsPresent;low=$Low.IsPresent;publisherSigningAcceptance=$false;storeSubmissionReady=$false;physicalAcceptance=$false;sourceRoot=$sourceRoot;workspace=$workspace}
$oldEditor = $env:DUNGEONS_INCLUDE_EDITOR; $oldLow = $env:DUNGEONS_ANDROID_LOW; $oldTextures = $env:DUNGEONS_MOBILE_TEXTURE_DIR
Push-Location $sourceRoot
try {
    $snapshot = & (Join-Path $PSScriptRoot 'New-SourceWorkspace.ps1') -WorkspaceDirectory $workspace -ManifestPath (Join-Path $evidence 'source-files.json')
    $status.sourceFiles = $snapshot.Files
    [void][IO.Directory]::CreateDirectory((Join-Path $workspace '.toolchain'))
    [IO.File]::WriteAllText((Join-Path $workspace '.toolchain/godot-path.txt'),$godot)
    $presets = Join-Path $workspace 'game/export_presets.cfg'
    $text = [IO.File]::ReadAllText($presets)
    $selected = if ($Low) { 'Android Low' } else { 'Android' }
    $sections = [regex]::Split($text,'(?m)(?=^\[preset\.\d+\]\r?$)')
    $preset = @($sections | Where-Object { $_ -match ('(?m)^name="' + [regex]::Escape($selected) + '"\r?$') })
    if ($preset.Count -ne 1) { throw 'Expected one Android source preset.' }
    $newIndex = (@([regex]::Matches($text,'(?m)^\[preset\.(\d+)\]') | ForEach-Object {[int]$_.Groups[1].Value}) | Measure-Object -Maximum).Maximum + 1
    $oldIndex = [regex]::Match($preset[0],'^\[preset\.(\d+)\]').Groups[1].Value
    $bundleName = 'Android Bundle Validation'
    $bundlePreset = $preset[0].Replace("[preset.${oldIndex}]","[preset.${newIndex}]").Replace("[preset.${oldIndex}.options]","[preset.${newIndex}.options]").Replace("name=`"$selected`"","name=`"$bundleName`"")
    $bundlePreset = [regex]::Replace($bundlePreset,'(?m)^gradle_build/export_format=.*\r?\n','')
    $bundlePreset = $bundlePreset.Replace('gradle_build/use_gradle_build=true',"gradle_build/use_gradle_build=true`ngradle_build/export_format=1")
    [IO.File]::WriteAllText($presets,$text + "`n" + $bundlePreset)
    Set-Location -LiteralPath $workspace
    . (Join-Path $workspace 'tools/Common.ps1')
    $env:DUNGEONS_INCLUDE_EDITOR = 'true'
    Invoke-Checked dotnet @('restore','Dungeons.sln','--locked-mode') -LogPath (Join-Path $evidence 'restore.log') -Quiet
    Invoke-Checked dotnet @('build','game/Dungeons.Game.csproj','-c','Debug','--no-restore','-warnaserror') -LogPath (Join-Path $evidence 'build.log') -Quiet
    Initialize-GodotEditor -Path $godot
    [void](Invoke-GodotImport -Path $godot -LogPath (Join-Path $evidence 'import.log'))
    $textures = Join-Path $evidence 'native-textures'
    & (Join-Path $workspace 'tools/Build-MobileTextureVariants.ps1') -GodotPath $godot -ProjectDirectory $textures
    & (Join-Path $workspace 'tools/Install-AndroidTemplate.ps1')
    & (Join-Path $workspace 'tools/Install-AndroidHooks.ps1')
    $env:DUNGEONS_INCLUDE_EDITOR = 'false'; $env:DUNGEONS_ANDROID_LOW = if ($Low) { '1' } else { '0' }; $env:DUNGEONS_MOBILE_TEXTURE_DIR = $textures
    $output = Join-Path $evidence 'block_dungeons.aab'
    $mode = if ($Release) { '--export-release' } else { '--export-debug' }
    Invoke-Checked $godot @('--headless','--path','game',$mode,$bundleName,$output) -TimeoutSeconds 900 -LogPath (Join-Path $evidence 'export.log') -RejectEngineErrors -Quiet
    if (!(Test-Path -LiteralPath $output) -or [IO.File]::ReadAllText((Join-Path $evidence 'export.log')) -notmatch 'MOBILE_TEXTURE_EXPORT=PASS codec=ETC2 count=') { throw 'The bundle or native texture export boundary is missing.' }
    $status.bundle = $output; $status.bundleSha256 = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash
    $status.textureProfileDirectory = $textures; $status.passed = $true
}
catch { $status.failure = $_.Exception.Message; throw }
finally {
    $env:DUNGEONS_INCLUDE_EDITOR = $oldEditor; $env:DUNGEONS_ANDROID_LOW = $oldLow; $env:DUNGEONS_MOBILE_TEXTURE_DIR = $oldTextures
    $status | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence 'result.json') -Encoding utf8
    Pop-Location
}
Write-Host "Isolated Android bundle exported: $($status.bundle). Signing/validation/store/device acceptance remain separate."
