using Dungeons.Game.Input;
using Dungeons.Game.Rendering;
using Godot;

namespace Dungeons.Game.Persistence;

/// <summary>Applies persisted host preferences; input commands remain the replay boundary.</summary>
public partial class SettingsRuntime : Node
{
    private readonly List<GameplayScreen> _games = [];
    private SettingsStore _store = null!;
    private bool _applyWindow, _graphicsOverride;
    private Vector2I _lastWindowSize;
    public static SettingsRuntime? Instance { get; private set; }
    public GameSettings Current => _store.Current;
    public GameSettings Defaults { get; private set; } = new();
    public event Action<GameSettings>? Changed;
    public override void _Ready() { Instance = this; }
    public void Initialize(string directory, GameSettings defaults, bool applyWindow = true)
    {
        Defaults = defaults; _applyWindow = applyWindow; _graphicsOverride = GraphicsRuntime.Instance?.Selection.Rule == "command-line";
        _store = new(directory, defaults, message => Dungeons.Game.Logging.GameLog.Warning("Io", message)); Apply(Current, restoreWindow: true);
    }
    public bool Commit(GameSettings settings)
    {
        if (!_store.Save(settings)) { return false; }
        Apply(Current, restoreWindow: false); Changed?.Invoke(Current); return true;
    }
    public void Attach(GameplayScreen game) { _games.Add(game); game.ApplySettings(Current); }
    public void Detach(GameplayScreen game) => _games.Remove(game);
    private void Apply(GameSettings settings, bool restoreWindow)
    {
        SettingsValidation.Validate(settings);
        Dungeons.Game.Logging.GameLog.Info("View", "Settings applied; graphics " + settings.Video.Preset + "; UI scale " + settings.Video.UiScale.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (!_graphicsOverride) { GraphicsRuntime.Instance?.ConfigureSettings(settings.Video); }
        SettingsBindings.Apply(settings.Controls);
        SetVolume("Master", settings.Audio.Master); SetVolume("Music", settings.Audio.Music); SetVolume("SFX", settings.Audio.Sfx); SetVolume("UI", settings.Audio.Ui); SetVolume("Ambience", settings.Audio.Ambience);
        if (_applyWindow)
        {
            Window window = GetWindow(); ApplyUiScale();
            Dungeons.Game.UI.UiTheme.ApplyTextScale(window, settings.Accessibility.TextScale);
            if (DisplayServer.GetName() != "headless")
            {
                if (!OS.HasFeature("mobile"))
                {
                    window.Mode = settings.Video.Fullscreen ? Window.ModeEnum.Fullscreen : Window.ModeEnum.Windowed;
                    if (restoreWindow && !settings.Video.Fullscreen)
                    {
                        Rect2I screen = DisplayServer.ScreenGetUsableRect();
                        window.Size = new(System.Math.Min(settings.Video.WindowWidth, screen.Size.X), System.Math.Min(settings.Video.WindowHeight, screen.Size.Y));
                        if (settings.Video.WindowX is int x && settings.Video.WindowY is int y)
                        { window.Position = new(Math.Clamp(x, screen.Position.X - window.Size.X + 80, screen.End.X - 80), Math.Clamp(y, screen.Position.Y, screen.End.Y - 80)); }
                    }
                }
                DisplayServer.WindowSetVsyncMode(settings.Video.Vsync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
            }
        }
        for (int i = _games.Count - 1; i >= 0; i--) { if (!GodotObject.IsInstanceValid(_games[i])) { _games.RemoveAt(i); } else { _games[i].ApplySettings(settings); } }
    }
    private static void SetVolume(string name, int percent)
    {
        int bus = AudioServer.GetBusIndex(name);
        if (bus < 0) { AudioServer.AddBus(); bus = AudioServer.BusCount - 1; AudioServer.SetBusName(bus, name); AudioServer.SetBusSend(bus, "Master"); }
        AudioServer.SetBusVolumeDb(bus, percent == 0 ? -80 : Mathf.LinearToDb(percent / 100f)); AudioServer.SetBusMute(bus, percent == 0);
    }
    private void ApplyUiScale()
    {
        Window window = GetWindow(); float factor = Current.Video.UiScale;
        if (OS.HasFeature("mobile") && window.Size.Y > 0)
        { float baseHeight = window.GetVisibleRect().Size.Y * window.ContentScaleFactor; factor *= Mathf.Clamp(DisplayServer.ScreenGetScale(), .5f, 4) * baseHeight / window.Size.Y; }
        window.ContentScaleFactor = factor; _lastWindowSize = window.Size;
    }
    public override void _Process(double delta)
    { if (_store is not null && _applyWindow && _lastWindowSize != GetWindow().Size) { ApplyUiScale(); } }
    public void RememberWindow()
    {
        if (!_applyWindow || OS.HasFeature("mobile") || DisplayServer.GetName() == "headless") { return; }
        Window window = GetWindow(); bool fullscreen = window.Mode is Window.ModeEnum.Fullscreen or Window.ModeEnum.ExclusiveFullscreen;
        VideoSettings video = Current.Video with { Fullscreen = fullscreen };
        if (window.Mode == Window.ModeEnum.Windowed)
        { video = video with { WindowWidth = Math.Clamp(window.Size.X, 640, 7680), WindowHeight = Math.Clamp(window.Size.Y, 360, 4320), WindowX = window.Position.X, WindowY = window.Position.Y }; }
        _store.Save(Current with { Video = video });
    }
    public override void _Notification(int what)
    { if (_store is not null && (what == NotificationApplicationPaused || what == NotificationWMCloseRequest)) { RememberWindow(); _store.Save(Current); } }
    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (_store is not null && !OS.HasFeature("mobile") && input is InputEventKey { PhysicalKeycode: Key.Enter, AltPressed: true, Pressed: true, Echo: false })
        { Commit(Current with { Video = Current.Video with { Fullscreen = !Current.Video.Fullscreen } }); GetViewport().SetInputAsHandled(); }
    }
    public override void _ExitTree() { if (_store is not null) { RememberWindow(); } if (Instance == this) { Instance = null; } _games.Clear(); }
}
