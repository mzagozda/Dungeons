using Dungeons.Game.Persistence;
using Godot;

namespace Dungeons.Game.Input;

/// <summary>Only the game's action names cross the JSON/InputMap boundary.</summary>
public static class SettingsBindings
{
    public static Dictionary<string, InputBinding[]> Defaults()
    {
        var result = new Dictionary<string, InputBinding[]>(StringComparer.Ordinal);
        InputAdapter.VisitDefaults((action, first, second, others) =>
        {
            var bindings = new List<InputBinding> { new(BindingKind.Key, (int)first) };
            if (second != Key.None) { bindings.Add(new(BindingKind.Key, (int)second)); }
            foreach (InputEvent input in others) { if (Describe(input) is InputBinding binding) { bindings.Add(binding); } }
            result.Add(action, bindings.ToArray());
        });
        return result;
    }
    private static InputBinding? Describe(InputEvent input) => input switch
    {
        InputEventKey key => new(BindingKind.Key, (int)key.PhysicalKeycode, Shift: key.ShiftPressed, Control: key.CtrlPressed, Alt: key.AltPressed, Meta: key.MetaPressed),
        InputEventMouseButton mouse => new(BindingKind.MouseButton, (int)mouse.ButtonIndex),
        InputEventJoypadButton joy => new(BindingKind.JoyButton, (int)joy.ButtonIndex),
        InputEventJoypadMotion motion => new(BindingKind.JoyAxis, (int)motion.Axis, motion.AxisValue < 0 ? -1 : 1), _ => null,
    };
    public static void Apply(ControlSettings settings)
    {
        foreach (string action in SettingsValidation.Actions) { if (InputMap.HasAction(action)) { Godot.Input.ActionRelease(action); InputMap.EraseAction(action); } }
        InputAdapter.EnsureBindings();
        foreach (var pair in settings.Bindings)
        { InputMap.ActionEraseEvents(pair.Key); foreach (InputBinding binding in pair.Value) { InputMap.ActionAddEvent(pair.Key, ToEvent(binding)); } }
    }
    public static InputEvent ToEvent(InputBinding binding) => binding.Kind switch
    {
        BindingKind.Key => new InputEventKey { PhysicalKeycode = (Key)binding.Code, ShiftPressed = binding.Shift, CtrlPressed = binding.Control, AltPressed = binding.Alt, MetaPressed = binding.Meta },
        BindingKind.MouseButton => new InputEventMouseButton { ButtonIndex = (MouseButton)binding.Code },
        BindingKind.JoyButton => new InputEventJoypadButton { ButtonIndex = (JoyButton)binding.Code, Device = -1 },
        BindingKind.JoyAxis => new InputEventJoypadMotion { Axis = (JoyAxis)binding.Code, AxisValue = binding.Direction, Device = -1 },
        _ => throw new ArgumentOutOfRangeException(nameof(binding)),
    };
    public static InputBinding? Capture(InputEvent input)
    {
        InputBinding? result = input switch
        {
        InputEventKey { Pressed: true, Echo: false } key when key.PhysicalKeycode != Key.None || key.Keycode != Key.None => new(BindingKind.Key, (int)(key.PhysicalKeycode == Key.None ? key.Keycode : key.PhysicalKeycode), Shift: key.ShiftPressed, Control: key.CtrlPressed, Alt: key.AltPressed, Meta: key.MetaPressed),
        InputEventMouseButton { Pressed: true } mouse when mouse.Device != InputEvent.DeviceIdEmulation => new(BindingKind.MouseButton, (int)mouse.ButtonIndex),
        InputEventJoypadButton { Pressed: true } joy => new(BindingKind.JoyButton, (int)joy.ButtonIndex),
        InputEventJoypadMotion motion when MathF.Abs(motion.AxisValue) > .7f => new(BindingKind.JoyAxis, (int)motion.Axis, motion.AxisValue < 0 ? -1 : 1),
        _ => null,
        };
        return result is InputBinding binding && SettingsValidation.IsValidBinding(binding) ? result : null;
    }
    public static Dictionary<string, InputBinding[]> Snapshot()
    {
        var result = new Dictionary<string, InputBinding[]>(StringComparer.Ordinal);
        foreach (string action in SettingsValidation.Actions)
        {
            var bindings = new List<InputBinding>();
            foreach (InputEvent input in InputMap.ActionGetEvents(action))
            {
                InputBinding? binding = Describe(input);
                if (binding is InputBinding item) { bindings.Add(item); }
            }
            result.Add(action, bindings.ToArray());
        }
        return result;
    }
}
