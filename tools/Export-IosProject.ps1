#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[A-Z0-9]{10}$')][string]$AppleTeamId,
    [Parameter(Mandatory)][string]$WorkspaceDirectory,
    [Parameter(Mandatory)][string]$EvidenceDirectory,
    [string]$GodotPath,
    [switch]$Release,
    [switch]$BuildUnsignedArchive
)
. (Join-Path $PSScriptRoot 'Common.ps1')
if (!$IsMacOS) { throw 'iOS C# export requires macOS with Xcode; desktop checks cannot replace it.' }
$workspace = [IO.Path]::GetFullPath($WorkspaceDirectory)
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $script:RepoRoot 'artifacts')) + [IO.Path]::DirectorySeparatorChar
foreach ($target in @($workspace,$evidence)) {
    if (!$target.StartsWith($artifactRoot,[StringComparison]::Ordinal) -or (Test-Path -LiteralPath $target)) { throw 'iOS workspace and evidence must be new directories under repository artifacts.' }
}
if ($workspace -eq $evidence -or $workspace.StartsWith($evidence + [IO.Path]::DirectorySeparatorChar,[StringComparison]::Ordinal) -or $evidence.StartsWith($workspace + [IO.Path]::DirectorySeparatorChar,[StringComparison]::Ordinal)) { throw 'iOS workspace and evidence must not overlap.' }
$godot = Get-GodotPath -ExplicitPath $GodotPath
Assert-GodotVersion -Path $godot
[void][IO.Directory]::CreateDirectory($evidence)
$summary = [ordered]@{passed=$false;projectExportPassed=$false;unsignedArchiveBuilt=$false;archivePrerequisitesPassed=$false;signingAcceptance=$false;physicalAcceptance=$false;storeSubmissionReady=$false;releaseTemplate=$Release.IsPresent;appleTeamId=$AppleTeamId}
$previousEditor = $env:DUNGEONS_INCLUDE_EDITOR; $previousTextures = $env:DUNGEONS_MOBILE_TEXTURE_DIR
Push-Location $script:RepoRoot
try {
    Invoke-Checked xcodebuild @('-version') -LogPath (Join-Path $evidence 'xcode-version.log') -Quiet
    Invoke-Checked xcrun @('--sdk','iphoneos','--show-sdk-version') -LogPath (Join-Path $evidence 'ios-sdk.log') -Quiet
    if ([IO.File]::ReadAllText((Join-Path $evidence 'xcode-version.log')) -notmatch '(?m)^Xcode (\d+)' -or [int]$Matches[1] -lt 26) { throw 'Current store build preflight requires Xcode 26 or later.' }
    if ([IO.File]::ReadAllText((Join-Path $evidence 'ios-sdk.log')) -notmatch '^(\d+)\.' -or [int]$Matches[1] -lt 26) { throw 'Current store build preflight requires the iOS 26 SDK or later.' }
    $snapshot = & (Join-Path $PSScriptRoot 'New-SourceWorkspace.ps1') -WorkspaceDirectory $workspace -ManifestPath (Join-Path $evidence 'source-files.json')
    $summary.sourceFiles = $snapshot.Files
    [void][IO.Directory]::CreateDirectory((Join-Path $workspace '.toolchain'))
    [IO.File]::WriteAllText((Join-Path $workspace '.toolchain/godot-path.txt'),$godot)
    $presets = Join-Path $workspace 'game/export_presets.cfg'
    $preparedPresets = Join-Path $evidence 'prepared-export-presets.cfg'
    Invoke-Checked python @((Join-Path $PSScriptRoot 'prepare_ios_preset.py'),$presets,$preparedPresets,'--team',$AppleTeamId) -LogPath (Join-Path $evidence 'preset-preparation.log') -Quiet
    $iosSections = @([regex]::Split([IO.File]::ReadAllText($preparedPresets),'(?m)(?=^\[preset\.\d+\]\r?$)') | Where-Object { $_ -match '(?m)^name="iOS"\r?$' })
    if ($iosSections.Count -ne 1) { throw 'Expected one iOS export section.' }
    $bundleMatch = [regex]::Match($iosSections[0],'(?m)^application/bundle_identifier="([^"]+)"\r?$')
    if (!$bundleMatch.Success) { throw 'The iOS preset lacks its bundle identifier.' }
    $bundleIdentifier = $bundleMatch.Groups[1].Value
    Copy-Item -LiteralPath $preparedPresets -Destination $presets
    Set-Location -LiteralPath $workspace
    . (Join-Path $workspace 'tools/Common.ps1')
    $env:DUNGEONS_INCLUDE_EDITOR = 'true'
    Invoke-Checked dotnet @('restore','Dungeons.sln','--locked-mode') -LogPath (Join-Path $evidence 'restore.log') -Quiet
    Invoke-Checked dotnet @('build','game/Dungeons.Game.csproj','-c','Debug','--no-restore','-warnaserror') -LogPath (Join-Path $evidence 'build.log') -Quiet
    Initialize-GodotEditor -Path $godot
    [void](Invoke-GodotImport -Path $godot -LogPath (Join-Path $evidence 'import.log'))
    $textures = Join-Path $evidence 'native-textures'
    & (Join-Path $workspace 'tools/Build-MobileTextureVariants.ps1') -GodotPath $godot -ProjectDirectory $textures
    $env:DUNGEONS_INCLUDE_EDITOR = 'false'; $env:DUNGEONS_MOBILE_TEXTURE_DIR = $textures
    $exportDirectory = Join-Path $evidence 'project'; [void][IO.Directory]::CreateDirectory($exportDirectory)
    # The project-only exporter creates a directory tree, not this .ipa file.
    $requestedOutput = Join-Path $exportDirectory 'BlockDungeons.ipa'
    $mode = if ($Release) { '--export-release' } else { '--export-debug' }
    Invoke-Checked $godot @('--headless','--path','game',$mode,'iOS',$requestedOutput) -TimeoutSeconds 1200 -LogPath (Join-Path $evidence 'export.log') -RejectEngineErrors -Quiet
    $project = Join-Path $exportDirectory 'BlockDungeons.xcodeproj'
    if (!(Test-Path -LiteralPath (Join-Path $project 'project.pbxproj')) -or [IO.File]::ReadAllText((Join-Path $evidence 'export.log')) -notmatch 'MOBILE_TEXTURE_EXPORT=PASS codec=ETC2 count=') { throw 'Expected Xcode project or ETC2 export boundary is missing.' }
    Invoke-Checked xcodebuild @('-list','-json','-project',$project) -LogPath (Join-Path $evidence 'project-list.json') -Quiet
    $listed = Get-Content -LiteralPath (Join-Path $evidence 'project-list.json') -Raw | ConvertFrom-Json
    if ($listed.project.schemes -notcontains 'BlockDungeons') { throw 'The exported project lacks its expected shared scheme.' }
    $summary.projectExportPassed = $true; $summary.projectPath = $project
    $summary.projectPbxprojSha256 = (Get-FileHash (Join-Path $project 'project.pbxproj')).Hash
    if ($BuildUnsignedArchive) {
        $configuration = if ($Release) { 'Release' } else { 'Debug' }
        $archive = Join-Path $evidence 'BlockDungeons.xcarchive'
        Invoke-Checked xcodebuild @('-project',$project,'-scheme','BlockDungeons','-configuration',$configuration,'-sdk','iphoneos','-destination','generic/platform=iOS','-archivePath',$archive,'CODE_SIGNING_ALLOWED=NO','archive') -TimeoutSeconds 1800 -LogPath (Join-Path $evidence 'archive.log') -Quiet
        if (!(Test-Path -LiteralPath (Join-Path $archive 'Info.plist'))) { throw 'Xcode did not produce the requested unsigned archive.' }
        $summary.unsignedArchiveBuilt = $true; $summary.archivePath = $archive
        Invoke-Checked python @((Join-Path $workspace 'tools/audit_ios_archive.py'),$archive,'--bundle-id',$bundleIdentifier,'--output',(Join-Path $evidence 'archive-prerequisites.json')) -TimeoutSeconds 300 -LogPath (Join-Path $evidence 'archive-prerequisites.log') -Quiet
        $summary.archivePrerequisitesPassed = $true
    }
    $summary.passed = $true
}
catch { $summary.failure = $_.Exception.Message; throw }
finally {
    $env:DUNGEONS_INCLUDE_EDITOR = $previousEditor; $env:DUNGEONS_MOBILE_TEXTURE_DIR = $previousTextures
    $summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence 'result.json') -Encoding utf8
    Pop-Location
}
Write-Host "iOS project export passed (archive prerequisites checked: $($summary.archivePrerequisitesPassed)); signing, device and store acceptance remain separate: $evidence"
