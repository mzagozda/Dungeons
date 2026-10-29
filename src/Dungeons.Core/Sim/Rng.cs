namespace Dungeons.Core.Sim;

/// <summary>Complete xoshiro128** state, suitable for hashing/restoration.</summary>
public readonly struct RngState(uint s0, uint s1, uint s2, uint s3)
{
    /// <summary>First word.</summary>
    public uint S0 { get; } = s0;
    /// <summary>Second word.</summary>
    public uint S1 { get; } = s1;
    /// <summary>Third word.</summary>
    public uint S2 { get; } = s2;
    /// <summary>Fourth word.</summary>
    public uint S3 { get; } = s3;
}

/// <summary>xoshiro128** seeded by four low words of SplitMix64 (§7.5).</summary>
public struct Rng
{
    private uint _s0, _s1, _s2, _s3;
    /// <summary>Creates a nonzero generator state from a 64-bit mission seed.</summary>
    public Rng(ulong seed)
    {
        _s0 = (uint)SplitMix64(ref seed);
        _s1 = (uint)SplitMix64(ref seed);
        _s2 = (uint)SplitMix64(ref seed);
        _s3 = (uint)SplitMix64(ref seed);
        if ((_s0 | _s1 | _s2 | _s3) == 0) { _s0 = 1; }
    }
    /// <summary>Restores an exact state; rejects the all-zero absorbing state.</summary>
    public Rng(RngState state)
    {
        if ((state.S0 | state.S1 | state.S2 | state.S3) == 0) { throw new ArgumentException("RNG state must be nonzero.", nameof(state)); }
        _s0 = state.S0; _s1 = state.S1; _s2 = state.S2; _s3 = state.S3;
    }
    /// <summary>Current generator words.</summary>
    public readonly RngState State => new(_s0, _s1, _s2, _s3);
    /// <summary>Next uniformly distributed 32-bit value.</summary>
    public uint NextU32()
    {
        // default(Rng) is valid and deterministic as well.
        if ((_s0 | _s1 | _s2 | _s3) == 0) { _s0 = 1; }
        uint result = unchecked(RotateLeft(_s1 * 5, 7) * 9);
        uint t = _s1 << 9;
        _s2 ^= _s0; _s3 ^= _s1; _s1 ^= _s2; _s0 ^= _s3; _s2 ^= t;
        _s3 = RotateLeft(_s3, 11);
        return result;
    }
    /// <summary>Next float in [0, 1), using exactly 24 random bits.</summary>
    public float NextFloat() => (NextU32() >> 8) * (1f / 16777216f);
    /// <summary>Unbiased integer in [minInclusive, maxExclusive), including full signed ranges.</summary>
    public int NextInt(int minInclusive, int maxExclusive)
    {
        if (minInclusive >= maxExclusive) { throw new ArgumentOutOfRangeException(nameof(maxExclusive)); }
        uint range = unchecked((uint)((long)maxExclusive - minInclusive));
        uint threshold = unchecked(0u - range) % range;
        uint value;
        do { value = NextU32(); } while (value < threshold);
        return (int)(minInclusive + (long)(value % range));
    }
    /// <summary>Float within finite ordered bounds.</summary>
    public float Range(float min, float max)
    {
        if (!Math.MathUtil.IsFinite(min) || !Math.MathUtil.IsFinite(max) || min > max || !Math.MathUtil.IsFinite(max - min)) { throw new ArgumentOutOfRangeException(nameof(max)); }
        return min + (max - min) * NextFloat();
    }
    /// <summary>Bernoulli sample. Zero/one probabilities consume no randomness.</summary>
    public bool Chance(float probability)
    {
        if (!Math.MathUtil.IsFinite(probability) || probability < 0 || probability > 1) { throw new ArgumentOutOfRangeException(nameof(probability)); }
        return probability >= 1 || (probability > 0 && NextFloat() < probability);
    }
    /// <summary>SplitMix64 seeding primitive, updating its state in place.</summary>
    public static ulong SplitMix64(ref ulong state)
    {
        unchecked
        {
            ulong z = (state += 0x9E3779B97F4A7C15UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
    private static uint RotateLeft(uint value, int bits) => (value << bits) | (value >> (32 - bits));
}

/// <summary>Independent random streams with stable, domain-separated salts.</summary>
public sealed class RandomStreams
{
    /// <summary>Damage variance stream.</summary>
    public Rng Combat;
    /// <summary>Loot stream.</summary>
    public Rng Loot;
    /// <summary>AI decisions stream.</summary>
    public Rng Ai;
    /// <summary>Spawn placement/variants stream.</summary>
    public Rng Spawn;
    /// <summary>Creates four independently seeded generators.</summary>
    public RandomStreams(ulong seed)
    {
        Combat = Create(seed, 0x434F4D424154UL);
        Loot = Create(seed, 0x4C4F4F54UL);
        Ai = Create(seed, 0x4149UL);
        Spawn = Create(seed, 0x535041574EUL);
    }
    private static Rng Create(ulong seed, ulong salt)
    {
        ulong state = seed ^ salt;
        return new Rng(Rng.SplitMix64(ref state));
    }
}
