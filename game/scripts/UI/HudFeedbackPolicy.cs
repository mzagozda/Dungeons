using Dungeons.Core.Sim;

namespace Dungeons.Game.UI;

/// <summary>Presentation-only, pause-aware warning clock. It never advances a simulation timer.</summary>
public sealed class LowHealthFeedback
{
    public const float VignetteOpacity = .3f;
    public const double PulseSeconds = 1.2;
    private double _phase;
    public bool Active { get; private set; }
    public float BorderBlend { get; private set; }
    public void Advance(int health, int maximum, bool alive, double delta, bool paused, bool reduceMotion)
    {
        bool low = alive && health > 0 && maximum > 0 && (long)health * 4 < maximum;
        if (!low) { Active = false; BorderBlend = 0; _phase = 0; return; }
        if (!Active) { _phase = 0; }
        Active = true;
        if (!paused && double.IsFinite(delta) && delta > 0) { _phase = (_phase + delta % PulseSeconds) % PulseSeconds; }
        BorderBlend = reduceMotion ? 1 : .65f + .35f * (float)System.Math.Cos(_phase * System.Math.Tau / PulseSeconds);
    }
}

/// <summary>Matches the Core cooldown multiplier/cap and uses overflow-safe display rounding.</summary>
public static class HudCooldown
{
    public static int Seconds(int remaining) => (int)System.Math.Max(0, ((long)remaining + GameConstants.TickRate - 1) / GameConstants.TickRate);
    public static float Fraction(int remaining, int authoredTicks, float multiplier)
    {
        if (remaining <= 0) { return 0; }
        double duration = System.Math.Ceiling((double)authoredTicks * multiplier);
        if (!double.IsFinite(duration) || duration <= 0) { return 1; }
        duration = System.Math.Min(int.MaxValue, duration);
        return (float)System.Math.Clamp(remaining / duration, 0, 1);
    }
}
