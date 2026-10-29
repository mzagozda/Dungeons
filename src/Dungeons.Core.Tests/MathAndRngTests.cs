using System.Globalization;
using System.Text;
using Dungeons.Core.Math;
using Dungeons.Core.Sim;
using Dungeons.Core.Util;
using Xunit;

namespace Dungeons.Core.Tests;

public sealed class MathAndRngTests
{
    [Fact]
    public void CoordinateConventionTests()
    {
        Assert.Equal(new Vec2(0, -1), Vec2.North);
        Assert.Equal(new Vec2(0.5f, 0.5f), new IntVec2(0, 0).Center);
        Assert.Equal(new IntVec2(3, 5), IntVec2.FromPosition(new Vec2(3.99f, 5.01f)));
        Assert.Equal(new IntVec2(-1, -1), IntVec2.FromPosition(new Vec2(-0.01f, -0.01f)));
        Assert.Equal(new Vec2(3, 5), new Vec3(3, 2, 5).Xz);
        Assert.Equal(2, new Vec3(3, 2, 5).Y);
        var area = new Rect(1, 2, 3, 4);
        Assert.True(area.Contains(new Vec2(1, 2)));
        Assert.True(area.Contains(new Vec2(4, 6)));
        Assert.False(area.Contains(new Vec2(4.1f, 6)));
        Assert.False(area.Contains(new Vec2(0, 1)));
        Assert.True(new IntVec2(1, 2).Equals((object)new IntVec2(1, 2)));
        Assert.False(new IntVec2(1, 2).Equals("other"));
        Assert.Equal(new IntVec2(1, 2).GetHashCode(), new IntVec2(1, 2).GetHashCode());
        Assert.True(new IntVec2(1, 2) != new IntVec2(2, 1));
    }

    [Fact]
    public void VectorsPreserveAnalogMagnitudeAndUseInvariantDiagnostics()
    {
        var v = new Vec2(3, 4);
        Assert.Equal(5, v.Length); Assert.Equal(25, v.LengthSquared);
        Assert.Equal(new Vec2(0.6f, 0.8f), v.Normalized);
        Assert.Equal(new Vec2(6, 8), v * 2);
        Assert.Equal(new Vec2(-3, -4), -v);
        Assert.Equal(Vec2.Zero, v - v);
        Assert.Equal(v, Vec2.Lerp(Vec2.Zero, v * 2, 0.5f));
        Assert.Equal(25, Vec2.Dot(v, v)); Assert.Equal(25, Vec2.DistanceSquared(Vec2.Zero, v));
        Assert.Equal(new Vec2(0.1f, 0.2f), new Vec2(0.1f, 0.2f).ClampLength(1));
        Assert.Equal(Vec2.Zero, Vec2.Zero.Normalized);
        Assert.Equal(Vec2.Zero, new Vec2(float.NaN, 1).Normalized);
        Assert.False(new Vec2(0, float.PositiveInfinity).IsFinite);
        Assert.True(v.Equals((object)new Vec2(3, 4))); Assert.False(v.Equals(null));
        Assert.Equal(v.GetHashCode(), new Vec2(3, 4).GetHashCode());
        CultureInfo previous = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE"); Assert.Equal("(1.5, 2.5)", new Vec2(1.5f, 2.5f).ToString()); }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void DurationAndQuantizationBoundaries()
    {
        Assert.Equal(0, MathUtil.SecondsToTicks(0)); Assert.Equal(6, MathUtil.SecondsToTicks(0.1f));
        Assert.Equal(1, MathUtil.SecondsToTicks(1f / 60)); Assert.Equal(7, MathUtil.SecondsToTicks(0.101f));
        Assert.Throws<ArgumentOutOfRangeException>(() => MathUtil.SecondsToTicks(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => MathUtil.SecondsToTicks(float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => MathUtil.SecondsToTicks(float.MaxValue));
        Assert.Equal(1, MathUtil.Quantize(0.5f / 1024)); Assert.Equal(-1, MathUtil.Quantize(-0.5f / 1024));
        Assert.Equal(3, MathUtil.Clamp(8, 1, 3));
    }

    [Fact]
    public void XoshiroSeedOneMatchesIndependentReferenceSequence()
    {
        // xoshiro128** 1.1 reference + four low SplitMix64 words; generated independently in Python.
        uint[] expected = [264704485, 1194741267, 1678852802, 535420451, 3471461366, 2588997986, 4171330986, 2629103435];
        var rng = new Rng(1);
        foreach (uint word in expected) { Assert.Equal(word, rng.NextU32()); }
        var restored = new Rng(rng.State);
        for (int i = 0; i < 100; i++) { Assert.Equal(rng.NextU32(), restored.NextU32()); }
        Assert.Throws<ArgumentException>(() => new Rng(default(RngState)));
        Rng zero = default; _ = zero.NextU32(); Assert.NotEqual(0u, zero.State.S0 | zero.State.S1 | zero.State.S2 | zero.State.S3);
    }

    [Fact]
    public void RandomStreamsStayIndependentAndRangesStayBounded()
    {
        var a = new RandomStreams(42); var b = new RandomStreams(42);
        for (int i = 0; i < 10000; i++)
        {
            _ = a.Ai.NextU32();
            Assert.Equal(a.Combat.NextU32(), b.Combat.NextU32());
            Assert.Equal(a.Loot.NextU32(), b.Loot.NextU32()); Assert.Equal(a.Spawn.NextU32(), b.Spawn.NextU32());
            Assert.InRange(a.Ai.NextFloat(), 0f, 0.99999999f);
            Assert.InRange(a.Ai.NextInt(-10, 11), -10, 10);
            Assert.InRange(a.Ai.NextInt(int.MinValue, int.MaxValue), int.MinValue, int.MaxValue - 1);
            Assert.InRange(a.Ai.Range(-3, 4), -3, 4);
        }
        Rng rng = new(1); var state = rng.State;
        Assert.False(rng.Chance(0)); Assert.True(rng.Chance(1)); Assert.Equal(state.S0, rng.State.S0);
        _ = rng.Chance(0.5f); Assert.Equal(5, rng.Range(5, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.Range(2, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.Range(float.NaN, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.Chance(-0.1f));
    }

    [Theory]
    [InlineData("", 14695981039346656037UL)]
    [InlineData("a", 12638187200555641996UL)]
    [InlineData("hello", 11831194018420276491UL)]
    public void FnvKnownVectors(string text, ulong expected)
    {
        Assert.Equal(expected, Fnv1a.Compute(Encoding.UTF8.GetBytes(text)));
        var hash = new Fnv1a(); hash.Add(text); Assert.Equal(expected, hash.Value);
    }

    [Theory]
    [InlineData("żółć 🌲")]
    [InlineData("\ud800")]
    [InlineData("λ")]
    public void FnvUtf8AndIntegerEncodingAreCanonical(string text)
    {
        var hash = new Fnv1a(); hash.Add(text);
        Assert.Equal(Fnv1a.Compute(Encoding.UTF8.GetBytes(text)), hash.Value);
        var integer = new Fnv1a(); integer.Add(0x01020304u);
        Assert.Equal(Fnv1a.Compute(new byte[] { 4, 3, 2, 1 }), integer.Value);
        var wide = new Fnv1a(); wide.Add(0x0102030405060708UL);
        Assert.Equal(Fnv1a.Compute(new byte[] { 8, 7, 6, 5, 4, 3, 2, 1 }), wide.Value);
    }
}
