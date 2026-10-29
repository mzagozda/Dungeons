[CmdletBinding()]
param(
    [Parameter(Mandatory,ParameterSetName='Android')][string]$ApkPath,
    [Parameter(Mandatory,ParameterSetName='Desktop')][string]$DesktopAssemblyPath,
    [switch]$IncludeEditor
)
. (Join-Path $PSScriptRoot 'Common.ps1')

function Read-AssemblySurface {
    param([IO.Stream]$InputStream)
    $copy = [IO.MemoryStream]::new()
    try {
        $InputStream.CopyTo($copy); $copy.Position = 0
        $pe = [System.Reflection.PortableExecutable.PEReader]::new($copy)
        try {
            $reader = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
            $types = @{}
            foreach ($handle in $reader.TypeDefinitions) {
                $type = $reader.GetTypeDefinition($handle)
                $name = $reader.GetString($type.Namespace) + '.' + $reader.GetString($type.Name)
                $methods = @($type.GetMethods() | ForEach-Object { $reader.GetString($reader.GetMethodDefinition($_).Name) })
                $types[$name] = $methods
            }
            return $types
        }
        finally { $pe.Dispose() }
    }
    finally { $copy.Dispose() }
}

if ($PSCmdlet.ParameterSetName -eq 'Desktop') {
    $assembly = (Resolve-Path -LiteralPath $DesktopAssemblyPath).Path
    $stream = [IO.File]::OpenRead($assembly)
    try { $desktop = Read-AssemblySurface $stream } finally { $stream.Dispose() }
    foreach ($name in @('Dungeons.Game.Editor.PreviewCaptureHost','Dungeons.Game.Editor.LevelReloadTool','Dungeons.Game.Editor.PaletteIconCapture','Dungeons.Game.Editor.SoakHarness','Dungeons.Game.Editor.SoakPolicy')) {
        if (!$desktop.ContainsKey($name)) { throw "Desktop authoring type was stripped: $name" }
    }
    if (@($desktop.Keys | Where-Object { $_.StartsWith('Dungeons.Game.Tests.', [StringComparison]::Ordinal) }).Count) { throw 'Desktop engine tests leaked into the exported game DLL.' }
    if (!$desktop.ContainsKey('Dungeons.Game.UI.UiTheme')) { throw 'Shared runtime UI theme was stripped from desktop.' }
    [ordered]@{ capturedUtc=[DateTime]::UtcNow.ToString('o'); assembly=$assembly; sha256=(Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash;
        desktopEditorTypesPresent=$true; desktopTestTypeCount=0 } | ConvertTo-Json | Set-Content -LiteralPath ([IO.Path]::ChangeExtension($assembly, '.editor-audit.json')) -Encoding UTF8
    Write-Host "Exported desktop authoring types retained; test types excluded: $assembly"
    return
}

$apk = (Resolve-Path -LiteralPath $ApkPath).Path
$zip = [IO.Compression.ZipFile]::OpenRead($apk)
try {
    $paths = @($zip.Entries | ForEach-Object FullName)
    $builtins = [IO.File]::ReadAllText((Join-Path $script:RepoRoot 'game/levels/index.json')) | ConvertFrom-Json
    foreach ($level in $builtins) {
        $source = 'levels/' + $level + '/preview.png'
        $import = Join-Path $script:RepoRoot ('game/' + $source + '.import')
        $target = [regex]::Match([IO.File]::ReadAllText($import), '(?m)^path="res://([^"]+)"')
        if (!$target.Success -or !$zip.GetEntry('assets/' + $target.Groups[1].Value) -or !$zip.GetEntry('assets/' + $source + '.import')) { throw "Built-in catalogue preview/import missing from APK: $source" }
    }
    foreach ($source in @('assets/fonts/Silkscreen-Regular.ttf','assets/fonts/block_symbols.fnt','assets/ui/panel.png','assets/ui/button_hover.png','assets/ui/button_pressed.png','assets/ui/button_disabled.png','assets/ui/focus.png','assets/ui/check_on.png','assets/ui/check_off.png','assets/ui/slider_grab.png','assets/ui/arrow_down.png','assets/ui/arrow_up.png','assets/ui/arrow_left.png','assets/ui/arrow_right.png')) {
        $import = Join-Path $script:RepoRoot ('game/' + $source + '.import')
        $resource = [regex]::Match([IO.File]::ReadAllText($import), '(?m)^path="res://([^"]+)"')
        if (!$resource.Success -or $paths -notcontains ('assets/' + $resource.Groups[1].Value)) { throw "Shared pixel UI resource missing from APK: $source" }
    }
    if (!$zip.GetEntry('assets/assets/ui/block_dungeons.tres')) {
        $themeRemap = $zip.GetEntry('assets/assets/ui/block_dungeons.tres.remap')
        if (!$themeRemap) { throw 'APK lacks the shared pixel UI theme.' }
        $themeReader = [IO.StreamReader]::new($themeRemap.Open()); try { $themeText = $themeReader.ReadToEnd() } finally { $themeReader.Dispose() }
        $themePath = [regex]::Match($themeText, '(?m)^path="res://([^"]+)"')
        if (!$themePath.Success -or !$zip.GetEntry('assets/' + $themePath.Groups[1].Value)) { throw 'APK theme remap points to a missing resource.' }
    }
    $notice = $zip.GetEntry('assets/licenses/silkscreen_ofl.txt')
    if (!$notice) { throw 'APK lacks the Silkscreen copyright/license notice.' }
    $noticeStream = $notice.Open(); try { $reader = [IO.StreamReader]::new($noticeStream); try { $noticeText = $reader.ReadToEnd() } finally { $reader.Dispose() } } finally { $noticeStream.Dispose() }
    if ($noticeText -cne [IO.File]::ReadAllText((Join-Path $script:RepoRoot 'game/licenses/silkscreen_ofl.txt'))) { throw 'APK font notice differs from its pinned source.' }
    $forbidden = @($paths | Where-Object { $_ -match '(^|/)(editor|editor_export)/|PreviewCaptureHost|preview_capture' })
    if (!$IncludeEditor -and $forbidden.Count) { throw ('Desktop editor resources leaked into APK: ' + ($forbidden -join ', ')) }
    $importedEditorResources = @()
    foreach ($import in Get-ChildItem -LiteralPath (Join-Path $script:RepoRoot 'game/editor') -Filter '*.import' -Recurse -File) {
        $match = [regex]::Match([IO.File]::ReadAllText($import.FullName), '(?m)^path="res://([^"]+)"')
        if ($match.Success) { $importedEditorResources += 'assets/' + $match.Groups[1].Value }
    }
    $leakedImports = @($paths | Where-Object { $importedEditorResources -contains $_ })
    if (!$IncludeEditor -and $leakedImports.Count) { throw ('Imported editor textures leaked into APK: ' + ($leakedImports -join ', ')) }
    if ($IncludeEditor -and ($forbidden.Count -eq 0 -or $leakedImports.Count -ne $importedEditorResources.Count)) { throw 'Explicit editor feature did not retain its resources/textures.' }
    $gameEntry = $zip.Entries | Where-Object FullName -eq 'assets/.godot/mono/publish/arm64/Dungeons.Game.dll'
    $coreEntry = $zip.Entries | Where-Object FullName -eq 'assets/.godot/mono/publish/arm64/Dungeons.Core.dll'
    if (!$gameEntry -or !$coreEntry) { throw 'APK lacks published arm64 game/Core assemblies.' }
    $stream = $gameEntry.Open(); try { $game = Read-AssemblySurface $stream } finally { $stream.Dispose() }
    foreach ($name in @('Dungeons.Game.LaunchOptions','Dungeons.Game.RuntimeLevelFiles','Dungeons.Game.Input.ReplayPlayback','Dungeons.Game.Rendering.ScreenshotScenario','Dungeons.Game.Rendering.ScreenshotScenarios','Dungeons.Game.Rendering.ScreenshotRun','Dungeons.Game.Rendering.CaptureClock')) {
        if (!$game.ContainsKey($name)) { throw "Runtime launch/replay type was stripped: $name" }
    }
    foreach ($name in @('Dungeons.Game.UI.UiTheme','Dungeons.Game.UI.LevelCard','Dungeons.Game.UI.LevelBrowserView','Dungeons.Game.UI.LevelDetailsView','Dungeons.Game.UI.LevelPreviewPool','Dungeons.Game.UI.PreviewPng','Dungeons.Game.UI.HudActionSlot','Dungeons.Game.UI.LowHealthVignette','Dungeons.Game.UI.LowHealthFeedback','Dungeons.Game.UI.HudCooldown','Dungeons.Game.UI.HudObjectives','Dungeons.Game.UI.HudBanner','Dungeons.Game.UI.HudBannerFade','Dungeons.Game.UI.MissionBannerState','Dungeons.Game.UI.HudToastState')) {
        if (!$game.ContainsKey($name)) { throw "Runtime UI type was stripped: $name" }
    }
    $stream = $coreEntry.Open(); try { $core = Read-AssemblySurface $stream } finally { $stream.Dispose() }
    $editorTypeCount = @($game.Keys | Where-Object { $_.StartsWith('Dungeons.Game.Editor.', [StringComparison]::Ordinal) }).Count
    if (!$IncludeEditor -and $editorTypeCount) { throw 'Desktop editor types leaked into the published game DLL.' }
    if ($IncludeEditor) {
        foreach ($name in @('Dungeons.Game.Editor.PreviewCaptureHost','Dungeons.Game.Editor.LevelReloadTool','Dungeons.Game.Editor.PaletteIconCapture')) {
            if (!$game.ContainsKey($name)) { throw "Explicit editor feature did not retain its type: $name" }
        }
    }
    if (@($game.Keys | Where-Object { $_.StartsWith('Dungeons.Game.Tests.', [StringComparison]::Ordinal) }).Count) { throw 'Desktop engine test types leaked into the published game DLL.' }
    foreach ($name in @('Dungeons.Core.Level.LevelDocument','Dungeons.Core.Level.LevelSerializer','Dungeons.Core.Level.LevelValidator','Dungeons.Core.Level.TriggerSchema','Dungeons.Core.Level.IAssetSource','Dungeons.Core.Sim.GameSession')) {
        if (!$core.ContainsKey($name)) { throw "Portable editor API was stripped: $name" }
    }
    foreach ($method in @('Build','ApplyChange','CapturePreview','Reload')) {
        if ($game['Dungeons.Game.Rendering.WorldView'] -notcontains $method) { throw "Reusable game rendering API was stripped: $method" }
    }
    foreach ($method in @('Create','ReloadLevel')) {
        if ($core['Dungeons.Core.Sim.GameSession'] -notcontains $method) { throw "Portable session API was stripped: $method" }
    }
    [ordered]@{ capturedUtc=[DateTime]::UtcNow.ToString('o'); apk=$apk; sha256=(Get-FileHash -LiteralPath $apk -Algorithm SHA256).Hash;
        editorFeatureEnabled=$IncludeEditor.IsPresent; desktopEditorResourceCount=$forbidden.Count; importedEditorResourceCount=$leakedImports.Count;
        desktopEditorTypeCount=$editorTypeCount; desktopTestTypeCount=0; gameTypeCount=$game.Count; portableCoreAndRenderingApisPresent=$true;
        pixelUiResourcesAndNoticePresent=$true; builtinCataloguePreviewCount=@($builtins).Count; physicalExecutionVerified=$false } | ConvertTo-Json | Set-Content -LiteralPath ([IO.Path]::ChangeExtension($apk, '.editor-audit.json')) -Encoding UTF8
    Write-Host "Mobile editor resource/type stripping and retained portable APIs passed: $apk"
}
finally { $zip.Dispose() }
