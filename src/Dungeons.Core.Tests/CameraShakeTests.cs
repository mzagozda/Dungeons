using Dungeons.Game.Rendering;
using Xunit;

namespace Dungeons.Core.Tests;

public sealed class CameraShakeTests
{
    [Fact]
    public void NoiseDecaysAndNeverExceedsTheQuarterMetreLimit()
    {
        var shake = new CameraShakeState(); Assert.Equal((0f, 0f), shake.Offset(0));
        shake.Trigger(10, 9, 2); Assert.Equal(.25f, shake.Strength(10)); Assert.Equal(.125f, shake.Strength(11));
        for (int i = 0; i < 2400; i++)
        { var (x, y) = shake.Offset(10 + i / 1200.0); Assert.InRange(MathF.Sqrt(x * x + y * y), 0, shake.Strength(10 + i / 1200.0) + .000001f); }
        Assert.Equal((0f, 0f), shake.Offset(12));
        shake.Trigger(13, .1f, 1); shake.Clear(); Assert.Equal((0f, 0f), shake.Offset(13));
    }

    [Fact]
    public void OverlapsRetainTheStrongerDecayingImpulseAndInvalidRequestsDoNothing()
    {
        var a = new CameraShakeState(); a.Trigger(1, .2f, 1); a.Trigger(1.25, .01f, .5f);
        Assert.Equal(.15f, a.Strength(1.25), 6); a.Trigger(1.25, float.NaN, 10); a.Trigger(1.25, 1, float.PositiveInfinity); a.Trigger(1.25, 1, 0);
        Assert.Equal(.15f, a.Strength(1.25), 6); Assert.Equal(0, a.Strength(1.75));
        var b = new CameraShakeState(); a.Clear(); a.Trigger(2, .2f, 1); b.Trigger(2, .2f, 1);
        Assert.Equal(a.Offset(2.31), b.Offset(2.31));
    }
}
