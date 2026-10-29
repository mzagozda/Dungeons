using Dungeons.Core.Content;
using Dungeons.Core.Level;
using Dungeons.Core.Sim;
using Dungeons.Game.Rendering;
using Dungeons.Game.UI;
using Dungeons.Game.Input;
using Dungeons.Game.Persistence;
using Godot;
using Dungeons.Game.Logging;

namespace Dungeons.Game;

/// <summary>Fixed-step host and rendered scene. Gameplay rules remain in GameSession.</summary>
public partial class GameplayScreen : Node3D
{
    private readonly IHaptics _haptics;
    public GameplayScreen() { _haptics = new GodotHaptics(_input); }
    /// <summary>Substitutes the host haptics service without changing simulation input or state.</summary>
    public GameplayScreen(IHaptics haptics) { _haptics = haptics ?? throw new ArgumentNullException(nameof(haptics)); }
    private double _accumulator;
    private Label _loading = null!;
    private CanvasLayer? _pause;
    private readonly InputAdapter _input = new();
    /// <summary>Authoritative deterministic session.</summary>
    public GameSession Session { get; private set; } = null!;
    /// <summary>Rendered world.</summary>
    public WorldView View { get; private set; } = null!;
    /// <summary>Bound HUD.</summary>
    public Hud Hud { get; private set; } = null!;
    public TouchControls? Touch { get; private set; }
    public bool VibrationEnabled { get; set; } = true;
    public bool ExternalUiOpen { get; set; }
    public event Action? SettingsRequested;
    private Dungeons.Game.Audio.GameplayAudio? _audio;
    private SessionLog? _sessionLog;
    /// <summary>Host returns to the catalogue when requested.</summary>
    public event Action? QuitRequested;
    /// <summary>Allows the host to report one failed load/tick and discard its partial scene.</summary>
    public event Action<Exception>? Failed;
    /// <summary>Builds the same scene used by normal play and integration tests.</summary>
    public void Build(LevelDocument level, ContentRegistry content, ulong seed, RunStore? store = null, RunTicket? ticket = null, bool touchPreview = false)
    {
        InputAdapter.EnsureBindings();
        _level = level; _store = store; _ticket = ticket;
        if (GameLog.Instance?.Logger is RollingFileLogger logger) { _sessionLog = new(logger, level.Id, seed); }
        Session = GameSession.Create(level, content, seed, _replayDocument?.Snapshot.Difficulty ?? DifficultyProfile.Default, log: _sessionLog, record: _replayDocument is null, startOverride: _replayDocument?.Snapshot.StartOverride); _sessionLog?.Bind(Session);
        if (_replayDocument is not null) { _playback = new(Session, _replayDocument); }
        _sessionLog?.Log(Dungeons.Core.Util.LogLevel.Info, "Sim", "Mission started.");
        View = new() { Name = "WorldView" }; AddChild(View); View.Build(level, content, Session);
        var effects = new CanvasLayer { Layer = 0 }; AddChild(effects);
        var vignette = new ColorRect { MouseFilter = Control.MouseFilterEnum.Ignore, Material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/vignette.gdshader") } };
        effects.AddChild(vignette); vignette.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        Hud = new() { Name = "Hud" }; AddChild(Hud); Hud.Build(Session, View);
        if (OS.HasFeature("mobile") || touchPreview || ScreenshotRun.Arguments.Contains("--touch-preview"))
        {
            var touchLayer = new CanvasLayer { Layer = 4 }; AddChild(touchLayer);
            Touch = new() { Name = "TouchControls" }; touchLayer.AddChild(Touch); Touch.Touched += _input.ActivateTouch;
            Touch.PauseRequested += TogglePause; Touch.DebugRequested += Hud.ToggleDebugOverlay; Touch.SafeInsetsChanged += (start, end) => { _input.Clear(); Hud.ApplySafeInsets(start, end, Touch.PauseReserveCanvas); };
            Touch.Build(Session.World, View.Camera); _input.BindTouch(Touch.State, true); Hud.UsesTouch = true;
            if (IsReplay) { Touch.Visible = false; Touch.AcceptInput = false; }
            Hud.RangedCounterPosition = () =>
            {
                foreach (TouchTarget target in Touch.State.Layout.Targets) { if (target.Action == InputButtons.Ranged) { return Touch.DpToViewport(target.Centre); } }
                return null;
            };
        }
        var debug = new DebugGeometry { Name = "DebugGeometry" }; AddChild(debug); debug.Build(level, Session); Hud.DebugVisibilityChanged += visible => debug.Visible = visible;
        var loadingLayer = new CanvasLayer { Layer = 5 }; AddChild(loadingLayer);
        _loading = new Label { Text = Tr("LOADING_LEVEL"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        loadingLayer.AddChild(_loading); _loading.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); _loading.AddThemeFontSizeOverride("font_size", 28);
        BuildDeathOverlay();
        if (Dungeons.Game.Audio.AudioManager.Instance is Dungeons.Game.Audio.AudioManager audio)
        { _audio = new(); AddChild(_audio); _audio.Build(audio, Session, level, content, View.Camera); }
        SettingsRuntime.Instance?.Attach(this);
        if (OS.HasFeature("mobile")) { DisplayServer.ScreenSetKeepOn(true); }
    }
    public override void _Process(double delta)
    {
        try { Advance(delta); }
        catch (Exception error) { Session?.Pause(); ProcessMode = ProcessModeEnum.Disabled; GameLog.Error("Sim", error.ToString()); Failed?.Invoke(error); }
    }
    private void Advance(double delta)
    {
        if (Session is null || !View.ReadyForPlay) { return; }
        _loading.Visible = false;
        RefreshFlow();
        if (ReplayComplete) { _accumulator = 0; ReportReplayCompletion(); return; }
        if (Touch is not null) { Touch.Visible = !IsReplay && _input.UsesTouch && Session.State == SessionState.Running; Touch.AcceptInput = !IsReplay && Session.State == SessionState.Running && Session.World.Players[0].Alive; }
        if (Session.State != SessionState.Running) { _accumulator = 0; return; }
        if (!IsReplay) { _input.Sample(View.Camera, Session.World); }
        if (!IsReplay && _input.GamepadDisconnected) { TogglePause(); return; }
        Hud.UsesGamepad = _input.UsesGamepad;
        Hud.UsesTouch = Touch is not null && _input.UsesTouch;
        _accumulator += System.Math.Min(delta, GameConstants.MaxFrameDelta); int steps = 0;
        ulong start = Time.GetTicksUsec();
        while (_accumulator >= GameConstants.Dt && steps < GameConstants.MaxTicksPerFrame)
        { if (_playback is null) { Session.Step(_input.ConsumeTickInput(Session.World)); } else { _playback.Step(); } _accumulator -= GameConstants.Dt; steps++; if (ReplayComplete) { _accumulator = 0; break; } }
        if (steps == GameConstants.MaxTicksPerFrame && _accumulator >= GameConstants.Dt) { _accumulator = 0; }
        Hud.TickMilliseconds = steps > 0 ? (Time.GetTicksUsec() - start) / (1000.0 * steps) : 0;
        ReadOnlySpan<SimEvent> events = Session.DrainEvents();
        HapticFeedback.Play(_haptics, events, Session.World.Players.Count > 0 ? Session.World.Players[0].Id : 0, VibrationEnabled, IsReplay);
        _audio?.Consume(events); Hud.ConsumeEvents(events); View.ConsumeEvents(events); View.Interpolate((float)(_accumulator / GameConstants.Dt));
        RefreshFlow();
        ReportReplayCompletion();
    }
    public override void _UnhandledInput(InputEvent @event)
    {
        if (ExternalUiOpen) { return; }
        if (IsReplay && !@event.IsActionPressed("pause")) { return; }
        if (@event is InputEventScreenTouch { Pressed: true } && Touch is not null && Session?.State == SessionState.Running && View.ReadyForPlay && Session.World.Players.Count > 0 && Session.World.Players[0].Alive)
        { _input.ActivateTouch(); Touch.Visible = true; Touch.AcceptInput = true; Touch._UnhandledInput(@event); return; }
        if (Session?.State == SessionState.Running && Session.World.Players.Count > 0 && !Session.World.Players[0].Alive) { GetViewport().SetInputAsHandled(); return; }
        if (@event.IsActionPressed("pause") && @event is not InputEventKey { Echo: true } && Session is not null)
        { TogglePause(); GetViewport().SetInputAsHandled(); }
        else if (Session?.State == SessionState.Running) { _input.HandleEvent(@event); }
    }
    public override void _Notification(int what)
    {
        if (what == NotificationApplicationFocusOut || what == NotificationApplicationPaused)
        { _input.Clear(); if (Session?.State == SessionState.Running && IsInsideTree()) { TogglePause(deferUi: true); } }
        if (what == NotificationApplicationResumed) { Touch?.RefreshMetrics(); }
        if (what == NotificationWMGoBackRequest && Session is not null && !ExternalUiOpen)
        { if (Session.State is SessionState.Running or SessionState.Paused) { TogglePause(); } else { QuitRequested?.Invoke(); } }
    }
    private void TogglePause()
        => TogglePause(deferUi: false);
    private void TogglePause(bool deferUi)
    {
        if (Session.State is SessionState.Won or SessionState.Failed) { return; }
        _accumulator = 0;
        StopVibration(); _input.Clear();
        if (Session.State == SessionState.Paused) { Session.Resume(); View.ProcessMode = ProcessModeEnum.Inherit; _pause?.QueueFree(); _pause = null; if (OS.HasFeature("mobile")) { DisplayServer.ScreenSetKeepOn(true); } return; }
        View.ProcessMode = ProcessModeEnum.Disabled;
        if (Touch is not null) { Touch.ClearInputs(); Touch.AcceptInput = false; Touch.Visible = false; }
        Session.Pause(); var layer = new CanvasLayer { Layer = 10 }; if (!deferUi) { AddChild(layer); } _pause = layer;
        if (OS.HasFeature("mobile")) { DisplayServer.ScreenSetKeepOn(false); }
        var panel = new PanelContainer { CustomMinimumSize = new(320, 200) }; layer.AddChild(panel); panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center); panel.Position -= panel.CustomMinimumSize / 2;
        var box = new VBoxContainer(); panel.AddChild(box); box.AddChild(new Label { Text = Tr("PAUSED"), HorizontalAlignment = HorizontalAlignment.Center });
        var resume = new Button { Text = Tr("RESUME") }; box.AddChild(resume); resume.Pressed += () => TogglePause();
        var restart = new Button { Name = "Restart", Text = Tr("RESTART") }; box.AddChild(restart); restart.Pressed += () => RestartRequested?.Invoke();
        if (SettingsRuntime.Instance is not null) { var settings = new Button { Text = Tr("SETTINGS") }; box.AddChild(settings); settings.Pressed += () => SettingsRequested?.Invoke(); }
        var quit = new Button { Text = Tr("QUIT_TO_LEVELS") }; box.AddChild(quit); quit.Pressed += () => QuitRequested?.Invoke();
        if (!deferUi) { resume.GrabFocus(); }
        else
        {
            Callable.From(() =>
            {
                if (GodotObject.IsInstanceValid(this) && IsInsideTree() && _pause == layer && !layer.IsQueuedForDeletion())
                { AddChild(layer); resume.GrabFocus(); }
                else if (GodotObject.IsInstanceValid(layer) && !layer.IsQueuedForDeletion()) { layer.QueueFree(); }
            }).CallDeferred();
        }
    }
    private void StopVibration() => _haptics.Stop();
    public void PauseForExternalUi() { if (Session.State == SessionState.Running) { TogglePause(); } }
    public void ApplySettings(GameSettings settings)
    {
        _input.Clear(); _input.Assist = settings.Controls.AutoAim; VibrationEnabled = settings.Controls.Vibration; if (!VibrationEnabled) { StopVibration(); }
        View.ReduceMotion = settings.Accessibility.ReduceMotion; View.CombatOverlay.ApplyAccessibility(settings.Accessibility); Hud.ApplyAccessibility(settings.Accessibility);
        Touch?.ConfigureLayout(settings.Controls);
    }
}
