namespace Dungeons.Game.Input;

/// <summary>Presentation-only patterns; platform adapters own device selection and pulse shape.</summary>
public enum HapticPattern { HeroHit, HeroDeath }

/// <summary>Host service boundary; simulation and replay data never depend on hardware vibration.</summary>
public interface IHaptics
{
    void Play(HapticPattern pattern);
    void Stop();
}
