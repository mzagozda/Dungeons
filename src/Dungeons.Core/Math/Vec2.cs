using System.Globalization;

namespace Dungeons.Core.Math;

/// <summary>An immutable ground-plane vector. X is east and Z is south (§6).</summary>
public readonly struct Vec2(float x, float z) : IEquatable<Vec2>
{
    /// <summary>East/west component.</summary>
    public float X { get; } = x;
    /// <summary>South/north component.</summary>
    public float Z { get; } = z;
    /// <summary>Zero vector.</summary>
    public static Vec2 Zero => default;
    /// <summary>World north, negative Z.</summary>
    public static Vec2 North => new(0, -1);
    /// <summary>Squared Euclidean length.</summary>
    public float LengthSquared => X * X + Z * Z;
    /// <summary>Euclidean length.</summary>
    public float Length => MathF.Sqrt(LengthSquared);
    /// <summary>Whether both components are finite.</summary>
    public bool IsFinite => MathUtil.IsFinite(X) && MathUtil.IsFinite(Z);
    /// <summary>Unit vector, or zero for a zero or non-finite vector.</summary>
    public Vec2 Normalized => LengthSquared > 0 && IsFinite ? this / Length : Zero;
    /// <summary>Preserves analogue magnitude while capping length.</summary>
    public Vec2 ClampLength(float maximum) => LengthSquared > maximum * maximum ? Normalized * maximum : this;
    /// <summary>Dot product.</summary>
    public static float Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Z * b.Z;
    /// <summary>Squared separation.</summary>
    public static float DistanceSquared(Vec2 a, Vec2 b) => (a - b).LengthSquared;
    /// <summary>Linear interpolation, without clamping t.</summary>
    public static Vec2 Lerp(Vec2 a, Vec2 b, float t) => a + (b - a) * t;
    /// <summary>Componentwise addition.</summary>
    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Z + b.Z);
    /// <summary>Componentwise subtraction.</summary>
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Z - b.Z);
    /// <summary>Negation.</summary>
    public static Vec2 operator -(Vec2 a) => new(-a.X, -a.Z);
    /// <summary>Scalar multiplication.</summary>
    public static Vec2 operator *(Vec2 a, float b) => new(a.X * b, a.Z * b);
    /// <summary>Scalar division.</summary>
    public static Vec2 operator /(Vec2 a, float b) => new(a.X / b, a.Z / b);
    /// <summary>Exact equality for recorded input/state values.</summary>
    public bool Equals(Vec2 other) => X.Equals(other.X) && Z.Equals(other.Z);
    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Vec2 other && Equals(other);
    /// <inheritdoc />
    public override int GetHashCode() => unchecked(X.GetHashCode() * 397 ^ Z.GetHashCode());
    /// <summary>Exact equality.</summary>
    public static bool operator ==(Vec2 a, Vec2 b) => a.Equals(b);
    /// <summary>Exact inequality.</summary>
    public static bool operator !=(Vec2 a, Vec2 b) => !a.Equals(b);
    /// <inheritdoc />
    public override string ToString() => string.Format(CultureInfo.InvariantCulture, "({0}, {1})", X, Z);
}
