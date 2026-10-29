using Dungeons.Game.Persistence;
using Godot;

namespace Dungeons.Game;

public partial class AppRoot
{
    private FileDialog? _importPicker;
    private AcceptDialog? _importResult;
    private double _importPoll;
    private void ImportLevel()
    {
        if (OS.HasFeature("ios")) { ShowImportResult(Tr("IMPORT_FILES_HELP")); return; }
        // Godot 4.4.1 advertises the Android picker even below its Java API 29 implementation.
        if (OS.HasFeature("android"))
        {
            if (PlatformPaths.AndroidSdk < 29) { ShowImportResult(Tr("IMPORT_OPEN_WITH_HELP")); return; }
        }
        if (_importPicker is null)
        {
            _importPicker = new() { Name = "LevelImportPicker", FileMode = FileDialog.FileModeEnum.OpenFile, Access = FileDialog.AccessEnum.Filesystem,
                UseNativeDialog = DisplayServer.HasFeature(DisplayServer.Feature.NativeDialogFile), Filters = ["*.dlvl;Block Dungeons level;application/octet-stream"],
                Title = Tr("IMPORT_LEVEL"), ModeOverridesTitle = false };
            AddChild(_importPicker); _importPicker.FileSelected += path =>
            {
                LevelImportResult result = LevelImportBridge.Import(path, _content, LevelsDirectory);
                if (result.Success) { Levels(); }
                ShowImportResult(result.Success ? Tr("IMPORT_SUCCEEDED") : Tr("IMPORT_FAILED") + "\n" + string.Join('\n', result.Diagnostics));
            };
        }
        _importPicker.PopupCenteredClamped(new(800, 560), .85f);
    }
    private void PumpImportInbox(double delta)
    {
        if (!OS.HasFeature("android") || _runs is null || (_importPoll += delta) < 1) { return; }
        _importPoll = 0; string directory = System.IO.Path.Combine(DataDirectory, "incoming-levels");
        if (!System.IO.Directory.Exists(directory)) { return; }
        try
        {
            string copyError = System.IO.Path.Combine(directory, "copy.error");
            if (System.IO.File.Exists(copyError)) { System.IO.File.Delete(copyError); _game?.PauseForExternalUi(); ShowImportResult(Tr("IMPORT_FAILED")); }
            string? pending = System.IO.Directory.EnumerateFiles(directory, "incoming-*.dlvl").Order(StringComparer.Ordinal).FirstOrDefault();
            if (pending is null) { return; }
            var result = LevelImportBridge.Import(pending, _content, LevelsDirectory); _game?.PauseForExternalUi();
            if (result.Success) { System.IO.File.Delete(pending); if (_game is null) { Levels(); } }
            else { System.IO.File.Move(pending, pending + ".rejected"); Dungeons.Game.Logging.GameLog.Warning("Level", "An incoming level package failed validation."); }
            ShowImportResult(result.Success ? Tr("IMPORT_SUCCEEDED") : Tr("IMPORT_FAILED") + "\n" + string.Join('\n', result.Diagnostics));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { Dungeons.Game.Logging.GameLog.Warning("Io", "Incoming level import unavailable: " + error.Message); }
    }
    private void ShowImportResult(string message)
    {
        if (_importResult is null) { _importResult = new() { Title = Tr("IMPORT_LEVEL") }; AddChild(_importResult); }
        _importResult.DialogText = message; _importResult.PopupCenteredClamped(new(640, 260), .85f);
    }
}
