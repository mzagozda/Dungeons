using Dungeons.Core.Content;
using Dungeons.Core.Level;
using Dungeons.Game.Persistence;
using Godot;
using Dungeons.Game.Logging;
using Dungeons.Game.UI;

namespace Dungeons.Game;

/// <summary>Boot/menu/catalogue orchestration. No simulation rules or level-specific ids.</summary>
public partial class AppRoot : Node
{
    private readonly string? _dataDirectory;
    private readonly IPlatformPaths? _platformPaths;
    public AppRoot() { }
    /// <summary>Embeds the app with an isolated profile/settings/level folder, also used by runtime QA.</summary>
    public AppRoot(string dataDirectory) { _dataDirectory = System.IO.Path.GetFullPath(dataDirectory); }
    /// <summary>Embeds a profile with a separately owned platform level directory.</summary>
    public AppRoot(string dataDirectory, IPlatformPaths platformPaths) : this(dataDirectory)
    { _platformPaths = platformPaths ?? throw new ArgumentNullException(nameof(platformPaths)); }
    private string DataDirectory => _dataDirectory ?? ProjectSettings.GlobalizePath("user://");
    private string LevelsDirectory => _platformPaths?.UserLevelsDir ?? (_dataDirectory is null ? PlatformPaths.UserLevelsDir : System.IO.Path.Combine(_dataDirectory, "levels"));
    private IReadOnlyList<CatalogEntry> ScanLevels() => CatalogBridge.Scan(_content, LevelsDirectory, _logs?.Logger);
    private bool _startupPending;
    private ContentRegistry _content = null!;
    private CanvasLayer? _menu;
    private GameplayScreen? _game;
    private IReadOnlyList<CatalogEntry> _catalog = [];
    private string? _screenshotTest;
    private RunStore? _runs;
    private SettingsRuntime? _settings;
    private GameLog? _logs;
    private Dungeons.Game.Audio.AudioManager? _audio;
    private Action? _back;
    private ConfirmationDialog? _quitConfirmation;
    public override void _Ready()
    {
        GetTree().QuitOnGoBack = false;
        if (LaunchArguments.Any(a => a.StartsWith("--m0-", StringComparison.Ordinal)))
        { GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, "res://scenes/main.tscn"); return; }
#if DUNGEONS_EDITOR_TOOLS
        if (Editor.PackagedQa.TryStart(this, LaunchArguments)) { return; }
#endif
        // Fixed diagnostic scenes remain available in debug mobile packages, where
        // newer export templates intentionally ignore generic scene overrides.
        if (OS.IsDebugBuild() && LaunchArguments.Any(a => a.StartsWith("--m3-screenshot=", StringComparison.Ordinal)))
        { GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, "res://scenes/m3_preview.tscn"); return; }
        if (OS.IsDebugBuild() && LaunchArguments.Any(a => a.StartsWith("--m7-screenshot=", StringComparison.Ordinal)))
        { GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, "res://scenes/m7_preview.tscn"); return; }
        GetTree().AutoAcceptQuit = false;
        try { _launch = LaunchOptions.Parse(LaunchArguments); _screenshotTest = _launch.Screenshot; _replayMode = _launch.Replay is not null; }
        catch (FormatException error) { _launchError = error.Message; }
        _startupPending = true;
    }
    public override void _Process(double delta)
    {
        if (_quitting) { return; }
        if (_startupPending && (_launchError is not null || _launch.Validate is not null || _launch.ListScreenshots)) { _startupPending = false; ValidateAndQuit(); return; }
        if (_startupPending && Dungeons.Game.Rendering.GraphicsRuntime.Instance?.TryRendererRelaunch() == true) { return; }
        if (_startupPending && _launch.ScreenshotScenario is { } scenario) { _startupPending = false; StartScreenshotScenario(scenario); return; }
        if (_game is not null && _screenshotTest is not null && _game.View.ReadyForPlay && (_game.IsReplay ? _game.ReplayComplete : _game.Session.Tick >= 120))
        {
            using Image screenshot = GetViewport().GetTexture().GetImage(); Error error = screenshot.SavePng(_screenshotTest);
            GD.Print($"APP_SCREENSHOT={(error == Error.Ok ? "PASS" : "FAIL")} chunks={_game.View.ChunkCount} health={_game.Hud.HealthText} objective={_game.Hud.ObjectiveText}");
            _screenshotTest = null; QuitApp(error == Error.Ok ? 0 : 1); return;
        }
        if (!_startupPending) { if (StartupComplete) { PumpImportInbox(delta); } return; }
        try
        {
            if (!AdvanceStartup()) { return; }
            _startupPending = false;
            if (_screenshotTest is null)
            {
                _audio = new(); AddChild(_audio); _audio.Initialize(new(_content)); _audio.BindUi(this);
                _settings = new(); AddChild(_settings);
                _settings.Initialize(DataDirectory, GameSettings.Defaults(Dungeons.Game.Rendering.GraphicsRuntime.Instance!.InitialSelection.Preset, OS.HasFeature("mobile")));
                if (_launch.Replay is null) { _runs = new(DataDirectory, message => GameLog.Warning("Io", message)); }
            }
            StartupComplete = true; LaunchRequestedLevel();
        }
        catch (Exception error) { _startupPending = false; ClearBoot(); HandleLaunchFailure(error); }
    }
    private VBoxContainer Menu(string title)
    {
        _menu?.QueueFree(); _menu = new CanvasLayer(); AddChild(_menu);
        var background = new ColorRect { Color = new("#1a1a1f") }; _menu.AddChild(background); background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var margin = new MenuLayout { Name = "MenuFrame" }; _menu.AddChild(margin); margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        foreach (string edge in new[] { "left", "top", "right", "bottom" }) { margin.AddThemeConstantOverride("margin_" + edge, 48); }
        var box = new VBoxContainer(); margin.AddChild(box); box.AddThemeConstantOverride("separation", 16);
        var label = new Label { Text = title, AutowrapMode = TextServer.AutowrapMode.WordSmart, MaxLinesVisible = 2 }; label.AddThemeFontSizeOverride("font_size", 36); label.Modulate = new("#e2b644"); box.AddChild(label); margin.BindTitle(label); return box;
    }
    private Button Button(VBoxContainer box, string key, Action pressed)
    { var button = new Button { Text = Tr(key), CustomMinimumSize = new(0, 52) }; box.AddChild(button); button.Pressed += pressed; return button; }
    private void MainMenu()
    {
        _back = ConfirmQuit;
        VBoxContainer box = Menu(Tr("GAME_TITLE")); Button(box, "PLAY", Levels).GrabFocus();
        Button(box, "SETTINGS", OpenSettings);
        Button(box, "CREDITS", () => { _back = MainMenu; var credits = Menu(Tr("CREDITS")); credits.AddChild(new Label { Text = Tr("CREDITS_ORIGINAL"), AutowrapMode = TextServer.AutowrapMode.WordSmart }); Button(credits, "BACK", MainMenu); });
        if (!OS.HasFeature("mobile")) { Button(box, "QUIT", () => QuitApp()); }
    }
    private void Start(CatalogEntry entry, ulong? seed = null)
    {
        if (!entry.IsPlayable) { ShowDiagnostics(entry, all: true); return; }
        StartDocument(entry.Document!, seed);
    }
    private void ReleaseGame() { if (_game is not null) { _game.CloseAttempt(); _game.ProcessMode = ProcessModeEnum.Disabled; _game.QueueFree(); _game = null; } }
    private void ShowError(string message) { Action back = StartupComplete ? MainMenu : RetryStartup; _back = back; var box = Menu(Tr("LOAD_FAILED")); box.AddChild(new Label { Text = message, AutowrapMode = TextServer.AutowrapMode.WordSmart }); Button(box, StartupComplete ? "BACK" : "RETRY", back); }
    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest) { QuitApp(); return; }
        if (what != NotificationWMGoBackRequest) { return; }
        if (_diagnostics?.Visible == true) { _diagnostics.Hide(); return; }
        if (_settingsPanel is not null || _game is null) { _audio?.Play("core:sfx_ui_back"); }
        if (_settingsPanel is not null) { _settingsPanel.Close(); return; }
        if (_game is null) { if (_quitConfirmation?.Visible == true) { _quitConfirmation.Hide(); } else { _back?.Invoke(); } }
    }
    private void ConfirmQuit()
    {
        if (_quitConfirmation is null)
        { _quitConfirmation = new() { Title = Tr("QUIT"), DialogText = Tr("CONFIRM_QUIT"), OkButtonText = Tr("QUIT"), CancelButtonText = Tr("CANCEL") }; AddChild(_quitConfirmation); _quitConfirmation.Confirmed += () => QuitApp(); }
        _quitConfirmation.PopupCentered(new(360, 160));
    }
}
