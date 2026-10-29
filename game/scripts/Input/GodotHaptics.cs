using Godot;

namespace Dungeons.Game.Input;

/// <summary>Routes the two v1 patterns to the most recently active, connected device.</summary>
internal sealed class GodotHaptics(InputAdapter input) : IHaptics
{
    private int _rumblingGamepad = -1;
    public void Play(HapticPattern pattern)
    {
        int duration = pattern switch { HapticPattern.HeroHit => 30, HapticPattern.HeroDeath => 60, _ => throw new ArgumentOutOfRangeException(nameof(pattern)) };
        if (input.UsesGamepad && Godot.Input.GetConnectedJoypads().Contains(input.ActiveGamepad))
        { _rumblingGamepad = input.ActiveGamepad; Godot.Input.StartJoyVibration(_rumblingGamepad, .35f, pattern == HapticPattern.HeroDeath ? .75f : .4f, duration / 1000f); }
        else if (input.UsesTouch && OS.HasFeature("mobile")) { Godot.Input.VibrateHandheld(duration); }
    }
    public void Stop()
    { if (_rumblingGamepad >= 0) { Godot.Input.StopJoyVibration(_rumblingGamepad); _rumblingGamepad = -1; } }
}
