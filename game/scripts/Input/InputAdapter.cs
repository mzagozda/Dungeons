using Dungeons.Core.Sim;
using Godot;
using Dungeons.Game.Persistence;
using CoreVec2 = Dungeons.Core.Math.Vec2;

namespace Dungeons.Game.Input;

/// <summary>Godot device sampling and camera projection; the result is the recorded Core command.</summary>
public sealed class InputAdapter
{
    private readonly CommandLatch _latch = new();
    private bool _gamepad;
    private int _joy;
    private TouchState? _touch;
    public bool UsesTouch { get; private set; } = OS.HasFeature("mobile");
    public AimAssist Assist { get; set; } = AimAssist.Normal;
    public void BindTouch(TouchState state, bool active) { _touch = state; UsesTouch = active; }
    public void ActivateTouch() { if (!UsesTouch) { _latch.Clear(); } UsesTouch = true; _gamepad = false; }
    /// <summary>Whether the latest active device is a gamepad.</summary>
    public bool UsesGamepad => _gamepad;
    public int ActiveGamepad => _gamepad ? _joy : -1;
    /// <summary>Whether that active gamepad disconnected during sampling.</summary>
    public bool GamepadDisconnected { get; private set; }
    private static readonly (string name, InputButtons flag)[] Actions = [("attack", InputButtons.Attack), ("ranged", InputButtons.Ranged), ("roll", InputButtons.Roll), ("potion", InputButtons.Potion), ("interact", InputButtons.Interact)];
    /// <summary>Registers defaults only when no saved InputMap action already exists.</summary>
    public static void EnsureBindings()
        => VisitDefaults(Bind);
    internal static void VisitDefaults(Action<string, Key, Key, InputEvent[]> visit)
    {
        visit("move_left", Key.A, Key.Left, [new InputEventJoypadMotion { Axis = JoyAxis.LeftX, AxisValue = -1 }]);
        visit("move_right", Key.D, Key.Right, [new InputEventJoypadMotion { Axis = JoyAxis.LeftX, AxisValue = 1 }]);
        visit("move_up", Key.W, Key.Up, [new InputEventJoypadMotion { Axis = JoyAxis.LeftY, AxisValue = -1 }]);
        visit("move_down", Key.S, Key.Down, [new InputEventJoypadMotion { Axis = JoyAxis.LeftY, AxisValue = 1 }]);
        visit("attack", Key.J, Key.None, [new InputEventMouseButton { ButtonIndex = MouseButton.Left }, new InputEventJoypadButton { ButtonIndex = JoyButton.X }]);
        visit("ranged", Key.K, Key.None, [new InputEventMouseButton { ButtonIndex = MouseButton.Right }, new InputEventJoypadButton { ButtonIndex = JoyButton.RightShoulder }, new InputEventJoypadMotion { Axis = JoyAxis.TriggerRight, AxisValue = 1 }]);
        visit("roll", Key.Space, Key.Shift, [new InputEventJoypadButton { ButtonIndex = JoyButton.A }, new InputEventJoypadButton { ButtonIndex = JoyButton.B }]);
        visit("potion", Key.Q, Key.None, [new InputEventJoypadButton { ButtonIndex = JoyButton.Y }]);
        visit("interact", Key.E, Key.None, [new InputEventJoypadButton { ButtonIndex = JoyButton.DpadDown }]);
        visit("pause", Key.Escape, Key.None, [new InputEventJoypadButton { ButtonIndex = JoyButton.Start }]);
    }
    private static void Bind(string action, Key first, Key second, params InputEvent[] other)
    {
        if (InputMap.HasAction(action)) { return; }
        InputMap.AddAction(action, .2f);
        InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = first });
        if (second != Key.None) { InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = second }); }
        foreach (InputEvent input in other)
        { if (input is InputEventJoypadButton or InputEventJoypadMotion) { input.Device = -1; } InputMap.ActionAddEvent(action, input); }
    }
    /// <summary>Captures action edges and the most recently used device.</summary>
    public void HandleEvent(InputEvent input)
    {
        if (input.Device == InputEvent.DeviceIdEmulation && input is InputEventMouse) { return; }
        if (input is InputEventJoypadButton || input is InputEventJoypadMotion motion && MathF.Abs(motion.AxisValue) > .3f) { _gamepad = true; _joy = input.Device; }
        else if (input is InputEventKey or InputEventMouseButton or InputEventMouseMotion) { _gamepad = false; }
        if (input is InputEventKey or InputEventMouse || _gamepad) { UsesTouch = false; _touch?.Clear(); }
        if (input is InputEventKey { Echo: true }) { return; }
        foreach (var action in Actions) { if (input.IsActionPressed(action.name)) { _latch.Press(action.flag); } }
    }
    /// <summary>Samples every render frame, even when no simulation step is due.</summary>
    public void Sample(Camera3D camera, World world)
    {
        if (UsesTouch && _touch is not null)
        {
            GamepadDisconnected = false; TouchFrame frame = _touch.Sample(Time.GetTicksUsec() / 1_000_000d);
            CoreVec2 touchMove = Project(camera, new(frame.Move.X, -frame.Move.Z));
            bool manual = frame.AimValid && (frame.Pressed & InputButtons.Ranged) != 0;
            CoreVec2 touchAim = manual ? Project(camera, new(frame.Aim.X, -frame.Aim.Z)).Normalized : default;
            _latch.Sample(touchMove, default, false, frame.Pressed, frame.Held);
            if ((frame.Pressed & InputButtons.Ranged) != 0) { _latch.Press(InputButtons.Ranged, touchAim, manual); }
            return;
        }
        Vector2 stick = Godot.Input.GetVector("move_left", "move_right", "move_down", "move_up");
        CoreVec2 move = Project(camera, stick);
        CoreVec2 aim = default; bool aimValid = false;
        GamepadDisconnected = _gamepad && !Godot.Input.GetConnectedJoypads().Contains(_joy);
        if (_gamepad && Godot.Input.GetConnectedJoypads().Contains(_joy))
        {
            Vector2 right = new(Godot.Input.GetJoyAxis(_joy, JoyAxis.RightX), -Godot.Input.GetJoyAxis(_joy, JoyAxis.RightY));
            if (right.LengthSquared() > .09f) { aim = Project(camera, right).Normalized; aimValid = true; }
        }
        else if (!OS.HasFeature("mobile") && world.Players.Count > 0)
        {
            Entity hero = world.Players[0]; Vector2 cursor = camera.GetViewport().GetMousePosition();
            Vector3? point = new Plane(Vector3.Up, hero.Y).IntersectsRay(camera.ProjectRayOrigin(cursor), camera.ProjectRayNormal(cursor));
            if (point is Vector3 target) { aim = Assist == AimAssist.Off ? (new CoreVec2(target.X, target.Z) - hero.Pos).Normalized : AutoAim.ResolveCursor(world, hero, new(target.X, target.Z), Assist == AimAssist.Strong); aimValid = aim != CoreVec2.Zero; }
        }
        InputButtons pressed = 0, held = 0;
        foreach (var action in Actions)
        { if (Godot.Input.IsActionJustPressed(action.name)) { pressed |= action.flag; } if (Godot.Input.IsActionPressed(action.name)) { held |= action.flag; } }
        _latch.Sample(move, aim, aimValid, pressed, held);
    }
    internal static CoreVec2 Project(Camera3D camera, Vector2 screen)
    {
        Vector3 right = camera.GlobalBasis.X, forward = -camera.GlobalBasis.Z;
        return (new CoreVec2(right.X, right.Z).Normalized * screen.X + new CoreVec2(forward.X, forward.Z).Normalized * screen.Y).ClampLength(1);
    }
    /// <summary>Consumes edges once while preserving held and analog state for catch-up ticks.</summary>
    public PlayerInput ConsumeTickInput() => _latch.ConsumeTickInput();
    public PlayerInput ConsumeTickInput(World world) => AimAssistPolicy.Apply(world, _latch.ConsumeTickInput(), Assist, UsesTouch);
    /// <summary>Discards stale device state.</summary>
    public void Clear() { _latch.Clear(); _touch?.Clear(); GamepadDisconnected = false; _gamepad = false; }
}
