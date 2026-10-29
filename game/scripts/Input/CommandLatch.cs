using Dungeons.Core.Math;
using Dungeons.Core.Sim;

namespace Dungeons.Game.Input;

/// <summary>Retains action edges between render frames and consumes each edge once.</summary>
public sealed class CommandLatch
{
    private Vec2 _move, _aim;
    private bool _aimValid;
    private InputButtons _pressed, _held;
    private Vec2 _edgeAim;
    private bool _edgeAimValid;
    private InputButtons _edgeAimButtons;
    /// <summary>Updates continuous values and accumulates transient button presses.</summary>
    public void Sample(Vec2 move, Vec2 aim, bool aimValid, InputButtons pressed, InputButtons held)
    { _move = move; _aim = aim; _aimValid = aimValid; _pressed |= pressed; _held = held; }
    /// <summary>Records an event edge, including press/release pairs inside one render frame.</summary>
    public void Press(InputButtons buttons) => _pressed |= buttons;
    /// <summary>Captures a release gesture's aim with its edge until the next tick.</summary>
    public void Press(InputButtons buttons, Vec2 aim, bool aimValid)
    { _pressed |= buttons; _edgeAimButtons = buttons; _edgeAim = aim; _edgeAimValid = aimValid; }
    /// <summary>Returns latest analog state and consumes accumulated edges.</summary>
    public PlayerInput ConsumeTickInput()
    {
        bool capturedAim = (_pressed & _edgeAimButtons) != 0;
        var input = new PlayerInput(_move, capturedAim ? _edgeAim : _aim, capturedAim ? _edgeAimValid : _aimValid, _pressed, _held);
        _pressed = _edgeAimButtons = InputButtons.None; return input;
    }
    /// <summary>Discards all state on focus loss, pause, or scene transition.</summary>
    public void Clear() { _move = _aim = _edgeAim = Vec2.Zero; _aimValid = _edgeAimValid = false; _pressed = _held = _edgeAimButtons = InputButtons.None; }
}
