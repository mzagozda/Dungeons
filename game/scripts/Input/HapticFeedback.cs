using Dungeons.Core.Sim;

namespace Dungeons.Game.Input;

/// <summary>Presentation-only pulse selection; one death pulse supersedes hits in a catch-up frame.</summary>
public static class HapticFeedback
{
    public static void Play(IHaptics haptics, ReadOnlySpan<SimEvent> events, int heroId, bool enabled, bool replay)
    {
        int duration = Duration(events, heroId, enabled && !replay);
        if (duration > 0) { haptics.Play(duration == 60 ? HapticPattern.HeroDeath : HapticPattern.HeroHit); }
    }
    public static int Duration(ReadOnlySpan<SimEvent> events, int heroId, bool enabled)
    {
        if (!enabled || heroId <= 0) { return 0; }
        int duration = 0;
        foreach (ref readonly SimEvent item in events)
        {
            if (item.EntityId != heroId) { continue; }
            if (item.Kind == SimEventKind.PlayerDied) { duration = 60; }
            else if (item.Kind == SimEventKind.DamageApplied && item.Amount > 0) { duration = System.Math.Max(duration, 30); }
        }
        return duration;
    }
}
