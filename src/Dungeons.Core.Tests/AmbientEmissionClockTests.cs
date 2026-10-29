using Dungeons.Game.Rendering;
using Xunit;

namespace Dungeons.Core.Tests;

public sealed class AmbientEmissionClockTests
{
    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(144)]
    public void FractionalBudgetsKeepTheSameRateAcrossRenderFrequencies(int fps)
    {
        var clock = new AmbientEmissionClock(); int emitted = 0;
        for (int frame = 0; frame < fps * 10; frame++) { emitted += clock.Advance(1.0 / fps, 24); }
        Assert.Equal(240, emitted);
    }
    [Fact]
    public void StallsOffscreenResetAndRateChangesRemainBounded()
    {
        var clock = new AmbientEmissionClock(); Assert.Equal(32, clock.Advance(60, 128)); Assert.Equal(0, clock.Advance(0, 128));
        Assert.Equal(0, clock.Advance(.02, 24)); clock.Reset(); Assert.Equal(0, clock.Advance(.03, 24));
        Assert.Equal(1, clock.Advance(.07, 4)); clock.Reset(); Assert.Equal(0, clock.Advance(0, 0));
        foreach (double delta in new[] { -1.0, double.NaN, double.PositiveInfinity }) { Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(delta, 1)); }
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(1, 129));
    }
}
