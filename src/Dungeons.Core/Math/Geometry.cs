namespace Dungeons.Core.Math;

/// <summary>An integer cell coordinate, with Z increasing down ASCII rows.</summary>
public readonly struct IntVec2(int x, int z) : IEquatable<IntVec2>
{
    /// <summary>Column.</summary>
    public int X { get; } = x;
    /// <summary>Row.</summary>
    public int Z { get; } = z;
    /// <summary>World-space centre of this cell.</summary>
    public Vec2 Center => new(X + 0.5f, Z + 0.5f);
    /// <summary>Cell containing a world-space point.</summary>
    public static IntVec2 FromPosition(Vec2 position) => new((int)MathF.Floor(position.X), (int)MathF.Floor(position.Z));
    /// <inheritdoc />
    public bool Equals(IntVec2 other) => X == other.X && Z == other.Z;
    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is IntVec2 other && Equals(other);
    /// <inheritdoc />
    public override int GetHashCode() => unchecked(X * 397 ^ Z);
    /// <summary>Cell equality.</summary>
    public static bool operator ==(IntVec2 a, IntVec2 b) => a.Equals(b);
    /// <summary>Cell inequality.</summary>
    public static bool operator !=(IntVec2 a, IntVec2 b) => !a.Equals(b);
}

/// <summary>An immutable Y-up position for interchange with presentation.</summary>
public readonly struct Vec3(float x, float y, float z)
{
    /// <summary>East component.</summary>
    public float X { get; } = x;
    /// <summary>Height.</summary>
    public float Y { get; } = y;
    /// <summary>South component.</summary>
    public float Z { get; } = z;
    /// <summary>Ground projection.</summary>
    public Vec2 Xz => new(X, Z);
}

/// <summary>A cell-unit rectangle with inclusive edges, as used by triggers.</summary>
public readonly struct Rect(float x, float z, float width, float height)
{
    /// <summary>West edge.</summary>
    public float X { get; } = x;
    /// <summary>North edge.</summary>
    public float Z { get; } = z;
    /// <summary>East/west extent.</summary>
    public float Width { get; } = width;
    /// <summary>North/south extent.</summary>
    public float Height { get; } = height;
    /// <summary>Whether the point lies on or within the rectangle.</summary>
    public bool Contains(Vec2 p) => p.X >= X && p.X <= X + Width && p.Z >= Z && p.Z <= Z + Height;
}

/// <summary>Portable scalar operations used by deterministic systems.</summary>
public static class MathUtil
{
    /// <summary>Rejects infinities and NaN without depending on newer runtime APIs.</summary>
    public static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    /// <summary>Clamps a scalar between ordered bounds.</summary>
    public static float Clamp(float value, float min, float max) => MathF.Max(min, MathF.Min(max, value));
    /// <summary>Converts authoring seconds to integral ticks, rounding up (§6).</summary>
    public static int SecondsToTicks(float seconds)
    {
        if (!IsFinite(seconds) || seconds < 0 || seconds > int.MaxValue / (double)Sim.GameConstants.TickRate)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds));
        }
        return checked((int)MathF.Ceiling(seconds * Sim.GameConstants.TickRate));
    }
    /// <summary>Quantises state to 1/1024 m using symmetric half-away-from-zero rounding.</summary>
    public static int Quantize(float value) => checked((int)(value >= 0
        ? MathF.Floor(value * Sim.GameConstants.HashScale + 0.5f)
        : -MathF.Floor(-value * Sim.GameConstants.HashScale + 0.5f)));
}
