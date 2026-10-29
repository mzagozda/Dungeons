using Dungeons.Core.Sim;
using Dungeons.Game.Rendering;
using Dungeons.Game.Persistence;
using Dungeons.Core.Content;
using Godot;

namespace Dungeons.Game.UI;

/// <summary>Reads authoritative snapshots; never changes simulation state.</summary>
public partial class Hud : CanvasLayer
{
    private GameSession _session = null!;
    private WorldView _view = null!;
    private Label _health = null!, _objective = null!, _message = null!, _debug = null!;
    private ProgressBar _bar = null!;
    private Label _actions = null!;
    private Control _root = null!;
    private HealthBarPattern _pattern = null!;
    private float _textScale = 1;
    private float _pauseReserve;
    private HudCounters _compactCounters = null!;
    private ExitLockIndicator _exitLock = null!;
    private PickupFlights _pickupFlights = null!;
    private HudActionSlot _potionSlot = null!, _bowSlot = null!;
    private LowHealthVignette _vignette = null!;
    private readonly LowHealthFeedback _lowHealth = new();
    private StyleBoxFlat _barBackground = null!;
    private StyleBoxFlat _barOutline = null!;
    private bool _reduceMotion;
    /// <summary>Current native touch-bow counter position, refreshed after layout/mirroring changes.</summary>
    public Func<Vector2?>? RangedCounterPosition { get; set; }
    /// <summary>Active presentation-only pickup tweens.</summary>
    public int ActivePickupFlights => _pickupFlights.ActiveCount;
    /// <summary>Whether a living hero is approaching an exit whose mission requirements are incomplete.</summary>
    public bool ExitLockVisible => _exitLock.Visible;
    /// <summary>Rendered health text, also observed by scene integration tests.</summary>
    public string HealthText => _health.Text;
    /// <summary>Rendered primary objective text.</summary>
    public string ObjectiveText => _objective.Text;
    /// <summary>Rendered action availability and ammunition.</summary>
    public string ActionText => _actions.Text;
    /// <summary>Current input scheme controls the displayed action glyphs.</summary>
    public bool UsesGamepad { get; set; }
    public bool UsesTouch { get; set; }
    public bool DebugVisible => _debug.Visible;
    public void ToggleDebugOverlay()
    {
        if (!OS.IsDebugBuild()) { return; }
        _debug.Visible = !_debug.Visible; DebugVisibilityChanged?.Invoke(_debug.Visible);
    }
    public void ApplySafeInsets(Vector2 topLeft, Vector2 bottomRight, float pauseReserve = 0)
    {
        _root.OffsetLeft = topLeft.X; _root.OffsetTop = topLeft.Y; _root.OffsetRight = -bottomRight.X; _root.OffsetBottom = -bottomRight.Y;
        _pauseReserve = pauseReserve;
    }
    /// <summary>Last host-measured simulation cost.</summary>
    public double TickMilliseconds { get; set; }
    /// <summary>Connects the debug HUD toggle to optional world overlays.</summary>
    public event Action<bool>? DebugVisibilityChanged;
    /// <summary>Creates responsive snapshot-bound controls.</summary>
    public void Build(GameSession session, WorldView view)
    {
        _session = session; _view = view;
        _vignette = new() { Name = "LowHealthVignette", Visible = false }; AddChild(_vignette); _vignette.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var root = new Control { Name = "HudRoot", MouseFilter = Control.MouseFilterEnum.Ignore }; _root = root; AddChild(root); root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        BuildMissionControls(root);
        _compactCounters = new() { AnchorLeft = 1, AnchorRight = 1, OffsetTop = 28, OffsetBottom = 90, Visible = false }; root.AddChild(_compactCounters);
        _bar = new ProgressBar { ShowPercentage = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        root.AddChild(_bar); _bar.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterBottom); _bar.OffsetLeft = -200; _bar.OffsetRight = 200; _bar.OffsetTop = -76; _bar.OffsetBottom = -48;
        _barBackground = new() { BgColor = new("#1a1a1f"), BorderColor = Colors.White, BorderWidthBottom = 2, BorderWidthTop = 2, BorderWidthLeft = 2, BorderWidthRight = 2 };
        _bar.AddThemeStyleboxOverride("background", _barBackground);
        _bar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = new("#c94444") });
        _pattern = new() { Visible = false }; _bar.AddChild(_pattern);
        // Keep the outline above the full-width fill and accessibility pattern.
        _barOutline = (StyleBoxFlat)_barBackground.Duplicate(); _barOutline.DrawCenter = false;
        var outline = new Panel { Name = "HealthOutline", MouseFilter = Control.MouseFilterEnum.Ignore }; _bar.AddChild(outline); outline.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); outline.AddThemeStyleboxOverride("panel", _barOutline);
        _health = new Label { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        _bar.AddChild(_health); _health.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); _health.AddThemeFontSizeOverride("font_size", 20);
        _actions = Text(root, new(-480, -43), new(960, 34), 18); _actions.AnchorLeft = _actions.AnchorRight = .5f; _actions.AnchorTop = _actions.AnchorBottom = 1; _actions.HorizontalAlignment = HorizontalAlignment.Center;
        _potionSlot = new() { Name = "PotionSlot", AnchorLeft = .5f, AnchorRight = .5f, AnchorTop = 1, AnchorBottom = 1 }; root.AddChild(_potionSlot);
        _bowSlot = new() { Name = "BowSlot", AnchorLeft = .5f, AnchorRight = .5f, AnchorTop = 1, AnchorBottom = 1 }; root.AddChild(_bowSlot);
        _debug = Text(root, new(32, 120), new(600, 210), 18); _debug.Name = "DebugStats"; _debug.Visible = false;
        _exitLock = new() { Name = "ExitLock", Visible = false }; _notice.AddLeading(_exitLock);
        _pickupFlights = new() { Name = "PickupFlights" }; root.AddChild(_pickupFlights); _pickupFlights.Bind(PickupTarget);
        Refresh();
    }
    public override void _Process(double delta) { if (_session is not null) { Refresh(delta); } }
    /// <summary>Uses retained event ids/positions after a collected entity has left its pool.</summary>
    public void ConsumeEvents(ReadOnlySpan<SimEvent> events)
    {
        if (_session.World.Players.Count == 0) { return; } int hero = _session.World.Players[0].Id;
        foreach (SimEvent ev in events)
        {
            if (ev.Kind == SimEventKind.CheckpointActivated) { _checkpointToast.Show(Tr("HUD_CHECKPOINT"), 3); continue; }
            if (ev.Kind == SimEventKind.ShowMessage) { _messageFade.Retrigger(); continue; }
            if (ev.Kind != SimEventKind.PickupCollected || ev.SourceId != hero || ev.Amount <= 0 || !_view.Registry.Entities.TryGetValue(ev.Text, out EntityDef? definition) || definition.Pickup is not PickupSpec pickup) { continue; }
            Vector3 point = new(ev.Position.X, ev.Y + .35f, ev.Position.Z);
            if (!_view.Camera.IsPositionBehind(point)) { _pickupFlights.Collect(pickup.Kind, _view.Camera.UnprojectPosition(point)); }
        }
    }
    private Vector2 PickupTarget(PickupKind kind)
    {
        if (kind == PickupKind.Emerald)
        { return _compactCounters.GetGlobalTransformWithCanvas() * new Vector2(8 * _counterScale, 14 * _counterScale); }
        if (kind == PickupKind.Arrows)
        { if (UsesTouch && RangedCounterPosition?.Invoke() is Vector2 target) { return target; } if (_bowSlot.Visible) { return _bowSlot.GetGlobalTransformWithCanvas() * (_bowSlot.Size * .5f); } }
        return _bar.GetGlobalTransformWithCanvas() * (_bar.Size * .5f);
    }
    public override void _UnhandledKeyInput(InputEvent @event)
    { if (OS.IsDebugBuild() && @event is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.F3 }) { ToggleDebugOverlay(); GetViewport().SetInputAsHandled(); } }
    private void Refresh(double delta = 0)
    {
        if (_session.World.Players.Count == 0) { return; }
        Entity hero = _session.World.Players[0]; _bar.MaxValue = hero.Health.Max; _bar.Value = hero.Health.Current;
        _lowHealth.Advance(hero.Health.Current, hero.Health.Max, hero.Alive, delta, _session.State != SessionState.Running, _reduceMotion);
        _vignette.Visible = _lowHealth.Active;
        Color border = Colors.White.Lerp(new Color("#c94444"), _lowHealth.BorderBlend); if (_barBackground.BorderColor != border) { _barBackground.BorderColor = border; _barOutline.BorderColor = border; }
        bool compact = _root.Size.Y < 420;
        Vector2 canvasSize = GetViewport().GetVisibleRect().Size;
        float reference = MathF.Max(.25f, MathF.Min(canvasSize.X, canvasSize.Y) * GetWindow().ContentScaleFactor / 1080);
        float halfWidth = MathF.Min(200 * reference, MathF.Max(70 * reference, _root.Size.X * .25f)); _bar.OffsetLeft = -halfWidth; _bar.OffsetRight = halfWidth;
        float bottomMargin = (compact && UsesTouch ? 24 : 48) * reference; _bar.OffsetBottom = -bottomMargin; _bar.OffsetTop = -bottomMargin - 28 * reference * MathF.Max(1, _textScale);
        int healthFont = (int)MathF.Round(20 * reference * _textScale), hintsFont = (int)MathF.Round(18 * reference * _textScale);
        if (_health.GetThemeFontSize("font_size") != healthFont) { _health.AddThemeFontSizeOverride("font_size", healthFont); }
        if (_actions.GetThemeFontSize("font_size") != hintsFont) { _actions.AddThemeFontSizeOverride("font_size", hintsFont); }
        _potionSlot.Visible = _bowSlot.Visible = !UsesTouch;
        float slotSize = MathF.Min(64 * reference * MathF.Max(1, _textScale), MathF.Max(24, (_root.Size.X - 32) / 4)), slotTop = (_bar.OffsetTop + _bar.OffsetBottom - slotSize) / 2;
        _potionSlot.OffsetRight = _bar.OffsetLeft - 12 * reference; _potionSlot.OffsetLeft = _potionSlot.OffsetRight - slotSize;
        _bowSlot.OffsetLeft = _bar.OffsetRight + 12 * reference; _bowSlot.OffsetRight = _bowSlot.OffsetLeft + slotSize;
        _potionSlot.OffsetTop = _bowSlot.OffsetTop = slotTop; _potionSlot.OffsetBottom = _bowSlot.OffsetBottom = slotTop + slotSize;
        float hintsWidth = MathF.Min(960, MathF.Max(1, _root.Size.X - 32));
        _actions.OffsetLeft = -hintsWidth / 2; _actions.OffsetRight = hintsWidth / 2;
        _actions.OffsetBottom = slotTop - 8 * reference; _actions.OffsetTop = _actions.OffsetBottom - MathF.Max(34 * reference * _textScale, _actions.GetCombinedMinimumSize().Y);
        _potionSlot.Bind(true, hero.Action.PotionCooldown, HudCooldown.Fraction(hero.Action.PotionCooldown, hero.Def.Controls.PotionCooldown, hero.Stats.CooldownMultiplier), 0, hero.Alive);
        _bowSlot.Bind(false, 0, 0, hero.Inventory.Arrows, hero.Alive);
        if (_pattern.Visible) { _pattern.Size = new(_bar.Size.X * hero.Health.Current / System.Math.Max(1, hero.Health.Max), _bar.Size.Y); _pattern.QueueRedraw(); }
        _actions.Visible = !UsesTouch;
        _exitLock.Visible = false;
        if (hero.Alive && _session.World.Mission.ExitLocked)
        {
            foreach (Entity entity in _session.World.Entities)
            { if (entity is { Alive: true, Kind: EntityKind.Exit } && Dungeons.Core.Math.Vec2.DistanceSquared(hero.Pos, entity.Pos) <= 9) { _exitLock.Visible = true; break; } }
        }
        string health = $"{hero.Health.Current}/{hero.Health.Max}"; if (_health.Text != health) { _health.Text = health; }
        RefreshMissionHud(hero, delta, reference, compact);
        string roll = hero.Action.RollRemaining > 0 ? Tr("HUD_ROLLING") : hero.Action.RollCooldown == 0 ? Tr("HUD_READY") : $"{HudCooldown.Seconds(hero.Action.RollCooldown)}s";
        string potion = hero.Action.PotionCooldown == 0 ? Tr("HUD_READY") : $"{HudCooldown.Seconds(hero.Action.PotionCooldown)}s";
        _actions.Text = string.Format(Tr(UsesGamepad ? "HUD_ACTIONS_GAMEPAD" : "HUD_ACTIONS"), hero.Inventory.Arrows, roll, potion);
        if (_debug.Visible)
        {
            _debug.Text = $"FPS {Engine.GetFramesPerSecond()} | frame {GetProcessDeltaTime() * 1000:F2} ms | tick {TickMilliseconds:F3} ms\n" +
                $"entities {_session.World.Entities.Count} | draws {RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame)}\n" +
                $"lights {_view.EnabledLightCount} | particles {_view.Particles.LiveCount}/{WorldParticles.Capacity} | chunks {_view.ChunkCount} | seed {_session.World.Seed:x16}\n" +
                $"hero ({(int)MathF.Floor(hero.Pos.X)}, {(int)MathF.Floor(hero.Pos.Z)}) h={hero.Y:F2} | tick {_session.Tick}\n" + AiSummary() + "\n" + Tr("DEBUG_LAYERS");
            if (GraphicsRuntime.Instance is GraphicsRuntime graphics)
            { _debug.Text += $"\nquality {graphics.Options.Preset} | device {graphics.DeviceRule} | source {graphics.Selection.Rule} | {graphics.Renderer}\n3D scale {graphics.Adaptive.Scale:F2} | avg {graphics.Adaptive.AverageFrameMs:F2} ms | thermal {graphics.Adaptive.ThermalLimited}"; }
            if (Dungeons.Game.Audio.AudioManager.Instance is Dungeons.Game.Audio.AudioManager audio)
            { _debug.Text += $"\naudio {audio.Driver} | voices {audio.ActiveVoiceCount}/{audio.PreparedVoiceCount} | music {audio.MusicId} | duck {audio.MusicDuckDb:F0} dB"; }
            if (Dungeons.Game.Logging.GameLog.Instance?.Logger is Dungeons.Game.Logging.RollingFileLogger logger)
            { _debug.Text += $"\nlogs queued {logger.PendingCount} | dropped {logger.DroppedEntries}"; }
        }
    }
    private string Localize(string text) => text.StartsWith('@') ? Tr(text[1..]) : text;
    public void ApplyAccessibility(AccessibilitySettings settings)
    {
        _textScale = settings.TextScale;
        _reduceMotion = settings.ReduceMotion;
        _pickupFlights.UiScale = settings.TextScale; _pickupFlights.ReduceMotion = settings.ReduceMotion;
        _pattern.Visible = settings.HighContrastHealth; _pattern.QueueRedraw();
        _bar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = HealthBarPattern.Tint(settings.HealthPalette) });
        foreach (var item in new[] { (_objective, 24), (_message, 24), (_health, 20), (_actions, 18), (_debug, 18) })
        { item.Item1.AddThemeFontSizeOverride("font_size", (int)MathF.Round(item.Item2 * settings.TextScale)); }
    }
    private static Label Text(Control root, Vector2 position, Vector2 size, int fontSize)
    {
        var label = new Label { Position = position, Size = size, AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", fontSize); label.AddThemeColorOverride("font_shadow_color", Colors.Black); label.AddThemeConstantOverride("shadow_offset_x", 2); label.AddThemeConstantOverride("shadow_offset_y", 2); root.AddChild(label); return label;
    }
}
