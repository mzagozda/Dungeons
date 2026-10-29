namespace Dungeons.Game.Rendering;

/// <summary>Presentation-only fractional emission budget, bounded after a stalled frame.</summary>
public struct AmbientEmissionClock
{
    private double _fraction;
    /// <summary>Consumes elapsed view time; catches up at most a quarter second and 32 particles.</summary>
    public int Advance(double delta, int particlesPerSecond)
    {
        if (!double.IsFinite(delta) || delta < 0 || particlesPerSecond is < 0 or > 128) { throw new ArgumentOutOfRangeException(nameof(delta)); }
        _fraction += System.Math.Min(delta, .25) * particlesPerSecond;
        int count = System.Math.Min(32, (int)System.Math.Floor(_fraction + 1e-9));
        _fraction = System.Math.Max(0, _fraction - count); return count;
    }
    /// <summary>Offscreen/removed emitters do not accumulate deferred bursts.</summary>
    public void Reset() => _fraction = 0;
}

/// <summary>Supported authored render.particles styles, independent of simulation entities/RNG.</summary>
public enum AmbientParticleKind { TorchFlame, BeaconStream }
