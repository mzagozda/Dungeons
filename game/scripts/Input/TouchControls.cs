using Dungeons.Core.Sim;
using Dungeons.Game.Persistence;
using Godot;
using CoreVec2 = Dungeons.Core.Math.Vec2;

namespace Dungeons.Game.Input;

/// <summary>Native-resolution touch presentation and viewport-coordinate input capture.</summary>
public partial class TouchControls : Control
{
    private World _world = null!;
    private Camera3D _camera = null!;
    private Vector2 _dpToCanvas = Vector2.One, _lastSize;
    private Vector2? _previewDp;
    private Vector4 _previewInsets;
    private int _metricsFrame;
    private float _layoutScale = 1;
    private bool _leftHanded;
    private TouchPlacement[] _positions = [];
    private int _pauseFinger = -1;
    private bool _pauseCancelled;
    private readonly DebugTapGesture _debugTap = new();
    private Font _font = null!;
    private readonly Vector2[] _cooldownWedge = new Vector2[34];
    public TouchState State { get; } = new();
    public Vector2 CanvasDpSize { get; private set; }
    public bool AcceptInput { get; set; }
    public bool LayoutAvailable { get; private set; }
    public event Action? Touched;
    public event Action? PauseRequested;
    public event Action? DebugRequested;
    public event Action<Vector2, Vector2>? SafeInsetsChanged;
    public float PauseReserveCanvas => 72 * State.Layout.Scale * _dpToCanvas.X;
    public void Build(World world, Camera3D camera)
    { _world = world; _camera = camera; _font = GetThemeDefaultFont(); MouseFilter = MouseFilterEnum.Ignore; SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); RefreshMetrics(); }
    public void ClearInputs() { ClearGameplayInputs(); _debugTap.Clear(); }
    private void ClearGameplayInputs() { State.Clear(); _pauseFinger = -1; _pauseCancelled = false; }
    public CoreVec2 PauseCentre => new(State.Layout.Maximum.X - 36 * State.Layout.Scale, State.Layout.Minimum.Z + 36 * State.Layout.Scale);
    public void ConfigureLayout(float scale, bool leftHanded)
    { _layoutScale = scale; _leftHanded = leftHanded; _lastSize = default; RefreshMetrics(); }
    public void ConfigureLayout(ControlSettings settings)
    {
        _positions = settings.TouchPositions.Select(pair => new TouchPlacement(Enum.Parse<InputButtons>(pair.Key), new(pair.Value.X, pair.Value.Y))).ToArray();
        ConfigureLayout(settings.TouchScale, settings.LeftHanded);
    }
    /// <summary>Development preview and integration-test metrics; production reads the display.</summary>
    public void ConfigurePreview(Vector2 sizeDp, Vector4 insetsDp)
    { _previewDp = sizeDp; _previewInsets = insetsDp; _lastSize = default; RefreshMetrics(); }
    public Vector2 DpToViewport(CoreVec2 point) => GetGlobalTransformWithCanvas() * (new Vector2(point.X, point.Z) * _dpToCanvas);
    private CoreVec2 ViewportToDp(Vector2 point)
    { Vector2 local = (GetGlobalTransformWithCanvas().AffineInverse() * point) / _dpToCanvas; return new(local.X, local.Y); }
    public void RefreshMetrics()
    {
        if (!IsInsideTree() || Size.X <= 0 || Size.Y <= 0) { return; }
        Vector2 windowPixels = GetWindow().Size;
        if (DisplayServer.GetName() == "headless") { windowPixels = Size; }
        float density = OS.HasFeature("mobile") ? Mathf.Clamp(DisplayServer.ScreenGetScale(), .5f, 4) : 1;
        Vector2 sizeDp = _previewDp ?? windowPixels / density;
        CanvasDpSize = sizeDp;
        Vector2 start = Vector2.Zero, end = Vector2.Zero;
        if (_previewDp is not null) { start = new(_previewInsets.X, _previewInsets.Y); end = new(_previewInsets.Z, _previewInsets.W); }
        else if (OS.HasFeature("mobile"))
        {
            Rect2I safe = DisplayServer.GetDisplaySafeArea();
            if (safe.Size.X > 0 && safe.Size.Y > 0)
            {
                Vector2 local = (Vector2)safe.Position - (Vector2)GetWindow().Position;
                start = local.Clamp(Vector2.Zero, windowPixels) / density;
                end = (windowPixels - local - (Vector2)safe.Size).Clamp(Vector2.Zero, windowPixels) / density;
            }
        }
        _dpToCanvas = Size / sizeDp;
        CoreVec2 minimum = new(start.X, start.Y), maximum = new(sizeDp.X - end.X, sizeDp.Y - end.Y);
        if (_lastSize == Size && State.Layout.Minimum == minimum && State.Layout.Maximum == maximum) { return; }
        ClearInputs(); float fittedScale = TouchLayout.FitScale(maximum - minimum, _layoutScale); LayoutAvailable = fittedScale > 0;
        if (LayoutAvailable) { State.Layout.Configure(new(sizeDp.X, sizeDp.Y), minimum, new(end.X, end.Y), fittedScale, _leftHanded); State.Layout.TrySetPositions(_positions); }
        _lastSize = Size; SafeInsetsChanged?.Invoke(start * _dpToCanvas, end * _dpToCanvas); QueueRedraw();
    }
    public override void _Process(double delta)
    { if (!AcceptInput) { ClearInputs(); } if (_metricsFrame++ % 30 == 0) { RefreshMetrics(); } QueueRedraw(); }
    public override void _Input(InputEvent input)
    {
        if (!OS.IsDebugBuild() || !AcceptInput || !LayoutAvailable) { return; }
        double seconds = Time.GetTicksUsec() / 1_000_000d; bool captured = _debugTap.IsCaptured, fired = false;
        if (input is InputEventScreenTouch touch)
        {
            if (touch.Pressed && !touch.Canceled) { _debugTap.Press(touch.Index, ViewportToDp(touch.Position), seconds); }
            else { fired = _debugTap.Release(touch.Index, ViewportToDp(touch.Position), seconds, touch.Canceled); }
        }
        else if (input is InputEventScreenDrag drag) { _debugTap.Drag(drag.Index, ViewportToDp(drag.Position), seconds); }
        else { return; }
        if (captured || _debugTap.IsCaptured)
        { ClearGameplayInputs(); GetViewport().SetInputAsHandled(); QueueRedraw(); }
        if (fired) { DebugRequested?.Invoke(); }
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if (!Visible || !AcceptInput || !LayoutAvailable) { return; }
        double seconds = Time.GetTicksUsec() / 1_000_000d; bool handled = false;
        if (input is InputEventScreenTouch touch)
        {
            CoreVec2 point = ViewportToDp(touch.Position);
            if (touch.Pressed && !touch.Canceled && PauseHit(point)) { _pauseFinger = touch.Index; _pauseCancelled = false; handled = true; }
            else if (_pauseFinger == touch.Index)
            { bool pause = !touch.Canceled && !_pauseCancelled && PauseHit(point); _pauseFinger = -1; handled = true; if (pause) { PauseRequested?.Invoke(); } }
            else { handled = touch.Pressed && !touch.Canceled ? State.Press(touch.Index, point, seconds) : State.Release(touch.Index, point, seconds, touch.Canceled); }
            if (handled && touch.Pressed) { Touched?.Invoke(); }
        }
        else if (input is InputEventScreenDrag drag)
        { if (drag.Index == _pauseFinger) { _pauseCancelled |= !PauseHit(ViewportToDp(drag.Position)); handled = true; } else { handled = State.Drag(drag.Index, ViewportToDp(drag.Position), seconds); } }
        if (handled) { GetViewport().SetInputAsHandled(); QueueRedraw(); }
    }
    public override void _Draw()
    {
        if (_world is null || _world.Players.Count == 0 || !LayoutAvailable || State.Layout.Maximum == CoreVec2.Zero) { return; }
        DrawSetTransform(Vector2.Zero, 0, _dpToCanvas);
        if (State.JoystickActive)
        {
            Vector2 centre = V(State.JoystickOrigin); DrawCircle(centre, State.Layout.JoystickRadius, new Color(.08f, .09f, .12f, .6f));
            DrawArc(centre, State.Layout.JoystickRadius, 0, Mathf.Tau, 48, new Color(1, 1, 1, .6f), 2, true);
            DrawCircle(centre + V(State.JoystickOffset), 22 * State.Layout.Scale, new Color(.9f, .9f, .95f, .9f));
        }
        Entity hero = _world.Players[0];
        Vector2 pause = V(PauseCentre); DrawCircle(pause, 24 * State.Layout.Scale, new Color(.08f, .09f, .12f, _pauseFinger >= 0 ? .9f : .6f));
        DrawLine(pause + new Vector2(-5, -9) * State.Layout.Scale, pause + new Vector2(-5, 9) * State.Layout.Scale, Colors.White, 4 * State.Layout.Scale, true);
        DrawLine(pause + new Vector2(5, -9) * State.Layout.Scale, pause + new Vector2(5, 9) * State.Layout.Scale, Colors.White, 4 * State.Layout.Scale, true);
        foreach (TouchTarget target in State.Layout.Targets)
        {
            Vector2 centre = V(target.Centre); float opacity = State.Held(target.Action) ? .9f : .6f;
            bool cooling = target.Action == InputButtons.Roll && (hero.Action.RollCooldown > 0 || hero.Action.RollRemaining > 0) || target.Action == InputButtons.Potion && hero.Action.PotionCooldown > 0 || target.Action == InputButtons.Ranged && hero.Inventory.Arrows == 0;
            Color ink = new(cooling ? .5f : 1, cooling ? .5f : 1, cooling ? .5f : 1, opacity);
            DrawCircle(centre, target.Radius, new Color(.08f, .09f, .12f, opacity)); DrawArc(centre, target.Radius, 0, Mathf.Tau, 48, ink, 2, true);
            DrawIcon(target.Action, centre, State.Layout.Scale, ink);
            if (target.Action == InputButtons.Potion && hero.Action.PotionCooldown > 0)
            {
                float fraction = UI.HudCooldown.Fraction(hero.Action.PotionCooldown, hero.Def.Controls.PotionCooldown, hero.Stats.CooldownMultiplier);
                UI.CooldownSweep.Draw(this, _cooldownWedge, centre, target.Radius, fraction, new Color(.12f, .12f, .15f, .5f));
            }
            int count = target.Action == InputButtons.Ranged ? hero.Inventory.Arrows : target.Action == InputButtons.Roll ? UI.HudCooldown.Seconds(hero.Action.RollCooldown) : target.Action == InputButtons.Potion ? UI.HudCooldown.Seconds(hero.Action.PotionCooldown) : 0;
            if (target.Action == InputButtons.Ranged || count > 0)
            { string text = count.ToString(); float size = 12 * State.Layout.Scale; DrawString(_font, centre + new Vector2(-_font.GetStringSize(text, fontSize: (int)size).X / 2, target.Radius - 5 * State.Layout.Scale), text, fontSize: (int)size, modulate: ink); }
        }
        TouchFrame gesture = State.Sample(Time.GetTicksUsec() / 1_000_000d, consumeEdges: false);
        if (gesture.AimValid && State.Held(InputButtons.Ranged))
        {
            CoreVec2 aim = InputAdapter.Project(_camera, new(gesture.Aim.X, -gesture.Aim.Z));
            Vector3 origin = new(hero.Pos.X, hero.Y + .15f, hero.Pos.Z), tip = origin + new Vector3(aim.X, 0, aim.Z) * 3;
            Vector2 from = V(ViewportToDp(_camera.UnprojectPosition(origin))), to = V(ViewportToDp(_camera.UnprojectPosition(tip)));
            DrawLine(from, to, Colors.Gold, 3, true); Vector2 direction = (to - from).Normalized();
            DrawLine(to, to - direction.Rotated(.5f) * 14, Colors.Gold, 3, true); DrawLine(to, to - direction.Rotated(-.5f) * 14, Colors.Gold, 3, true);
        }
        DrawSetTransform(Vector2.Zero);
    }
    private void DrawIcon(InputButtons action, Vector2 centre, float scale, Color colour)
    {
        Vector2 P(float x, float y) => centre + new Vector2(x, y) * scale;
        if (action == InputButtons.Attack)
        { DrawLine(P(-12, 13), P(14, -14), colour, 4 * scale, true); DrawLine(P(-14, 5), P(-5, 14), colour, 3 * scale, true); DrawLine(P(-13, -13), P(13, 13), colour, 3 * scale, true); }
        else if (action == InputButtons.Roll)
        { DrawArc(centre, 14 * scale, .4f, 5.5f, 24, colour, 3 * scale, true); DrawLine(P(10, -10), P(17, -11), colour, 3 * scale, true); DrawLine(P(17, -11), P(17, -3), colour, 3 * scale, true); }
        else if (action == InputButtons.Ranged)
        { DrawArc(P(-8, 0), 15 * scale, -Mathf.Pi / 2, Mathf.Pi / 2, 24, colour, 3 * scale, true); DrawLine(P(-8, -15), P(-8, 15), colour, 2 * scale, true); DrawLine(P(-15, 0), P(18, 0), colour, 2 * scale, true); DrawLine(P(11, -5), P(18, 0), colour, 2 * scale, true); DrawLine(P(11, 5), P(18, 0), colour, 2 * scale, true); }
        else
        { DrawRect(new(P(-10, -4), new Vector2(20, 19) * scale), colour, false, 3 * scale); DrawRect(new(P(-5, -14), new Vector2(10, 10) * scale), colour, false, 3 * scale); DrawLine(P(-12, -15), P(12, -15), colour, 3 * scale, true); DrawLine(P(-6, 6), P(6, 6), colour, 3 * scale, true); }
    }
    private static Vector2 V(CoreVec2 point) => new(point.X, point.Z);
    private bool PauseHit(CoreVec2 point) => CoreVec2.DistanceSquared(point, PauseCentre) <= 24 * 24 * State.Layout.Scale * State.Layout.Scale;
}
