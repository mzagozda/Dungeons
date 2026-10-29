using Dungeons.Core.Math;
using Dungeons.Core.Sim;

namespace Dungeons.Game.Input;

/// <summary>One host input sample; screen Z means down until the adapter projects it.</summary>
public readonly struct TouchFrame(Vec2 move, Vec2 aim, bool aimValid, InputButtons pressed, InputButtons held)
{
    public Vec2 Move { get; } = move;
    public Vec2 Aim { get; } = aim;
    public bool AimValid { get; } = aimValid;
    public InputButtons Pressed { get; } = pressed;
    public InputButtons Held { get; } = held;
}

/// <summary>Bounded multitouch capture and gesture state; never writes to the simulation.</summary>
public sealed class TouchState
{
    private struct Finger
    {
        internal int Id;
        internal bool Active, Movement, Cancelled, ManualBow;
        internal InputButtons Action;
        internal Vec2 Origin, Position, Offset;
        internal double Started;
    }
    private readonly Finger[] _fingers = new Finger[10];
    private int _joystick = -1;
    private InputButtons _pressed;
    private Vec2 _fireAim;
    private bool _fireAimValid;
    public TouchLayout Layout { get; } = new();
    public bool JoystickActive => _joystick >= 0;
    public Vec2 JoystickOrigin => _joystick >= 0 ? _fingers[_joystick].Origin : default;
    public Vec2 JoystickOffset => _joystick >= 0 ? (_fingers[_joystick].Position - _fingers[_joystick].Origin).ClampLength(Layout.JoystickRadius) : default;
    public bool Press(int id, Vec2 point, double seconds)
    {
        if (!point.IsFinite || !double.IsFinite(seconds) || Find(id) >= 0) { return false; }
        int slot = -1; for (int i = 0; i < _fingers.Length; i++) { if (!_fingers[i].Active) { slot = i; break; } }
        if (slot < 0) { return false; }
        InputButtons action = 0;
        foreach (TouchTarget target in Layout.Targets) { if (target.Contains(point)) { action = target.Action; break; } }
        bool movement = action == 0 && _joystick < 0 && Layout.MovementSide(point);
        if (!movement && (action == 0 || Held(action))) { return false; }
        Vec2 origin = movement ? Layout.ClampJoystick(point) : point;
        _fingers[slot] = new() { Id = id, Active = true, Movement = movement, Action = action, Origin = origin, Position = movement ? origin : point, Offset = origin - point, Started = seconds };
        if (movement) { _joystick = slot; } return true;
    }
    public bool Drag(int id, Vec2 point, double seconds)
    {
        int slot = Find(id); if (slot < 0 || !point.IsFinite || !double.IsFinite(seconds)) { return false; }
        ref Finger finger = ref _fingers[slot]; if (finger.Movement) { point += finger.Offset; } finger.Position = point;
        if (finger.Movement)
        {
            Vec2 delta = point - finger.Origin; float follow = TouchLayout.FollowDistanceDp * Layout.Scale;
            if (delta.Length > follow) { finger.Origin = Layout.ClampJoystick(point - delta.Normalized * follow); }
        }
        else if (finger.Action == InputButtons.Ranged)
        { finger.ManualBow |= seconds - finger.Started > TouchLayout.BowHoldSeconds || (point - finger.Origin).Length > TouchLayout.BowDragThresholdDp * Layout.Scale; }
        else
        { foreach (TouchTarget target in Layout.Targets) { if (target.Action == finger.Action && !target.Contains(point)) { finger.Cancelled = true; break; } } }
        return true;
    }
    public bool Release(int id, Vec2 point, double seconds, bool cancelled = false)
    {
        int slot = Find(id); if (slot < 0) { return false; }
        cancelled |= !point.IsFinite || !double.IsFinite(seconds);
        Drag(id, point, seconds); ref Finger finger = ref _fingers[slot];
        if (!cancelled && !finger.Cancelled && !finger.Movement)
        {
            if (finger.Action != InputButtons.Ranged) { _pressed |= finger.Action; }
            else if (!finger.ManualBow) { _pressed |= InputButtons.Ranged; _fireAimValid = false; }
            else if ((point - finger.Origin).Length > TouchLayout.BowDeadZoneDp * Layout.Scale)
            { _pressed |= InputButtons.Ranged; _fireAim = (point - finger.Origin).Normalized; _fireAimValid = true; }
        }
        if (finger.Movement) { _joystick = -1; } finger = default; return true;
    }
    public TouchFrame Sample(double seconds, bool consumeEdges = true)
    {
        Vec2 move = default, aim = _fireAim; bool aimValid = _fireAimValid; InputButtons held = 0;
        if (_joystick >= 0)
        {
            Vec2 delta = _fingers[_joystick].Position - _fingers[_joystick].Origin; float length = delta.Length;
            if (length > Layout.JoystickDeadZone) { move = delta.Normalized * MathF.Min(1, (length - Layout.JoystickDeadZone) / (Layout.JoystickRadius - Layout.JoystickDeadZone)); }
        }
        for (int i = 0; i < _fingers.Length; i++)
        {
            ref Finger finger = ref _fingers[i]; if (!finger.Active || finger.Movement || finger.Cancelled) { continue; }
            held |= finger.Action;
            if (finger.Action != InputButtons.Ranged) { continue; }
            finger.ManualBow |= seconds - finger.Started > TouchLayout.BowHoldSeconds;
            if ((_pressed & InputButtons.Ranged) == 0 && finger.ManualBow && (finger.Position - finger.Origin).Length > TouchLayout.BowDeadZoneDp * Layout.Scale)
            { aim = (finger.Position - finger.Origin).Normalized; aimValid = true; }
        }
        var frame = new TouchFrame(move, aim, aimValid, _pressed, held);
        if (consumeEdges) { _pressed = 0; _fireAimValid = false; } return frame;
    }
    public bool Held(InputButtons action)
    { for (int i = 0; i < _fingers.Length; i++) { if (_fingers[i].Active && !_fingers[i].Cancelled && _fingers[i].Action == action) { return true; } } return false; }
    public void Clear() { Array.Clear(_fingers); _joystick = -1; _pressed = 0; _fireAim = default; _fireAimValid = false; }
    private int Find(int id) { for (int i = 0; i < _fingers.Length; i++) { if (_fingers[i].Active && _fingers[i].Id == id) { return i; } } return -1; }
}
