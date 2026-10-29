using Dungeons.Core.Math;
using Dungeons.Game.Input;
using Xunit;

namespace Dungeons.Core.Tests;

public sealed class DebugTapGestureTests
{
    [Fact]
    public void FourOverlappingFingersFireOnlyAfterLastReleaseAndCanRepeat()
    {
        var gesture = new DebugTapGesture();
        for (int repeat = 0; repeat < 2; repeat++)
        {
            for (int i = 0; i < 4; i++) { gesture.Press(20 + i, new(40 * i, 50), repeat + i * .01); }
            Assert.True(gesture.IsCaptured);
            for (int i = 0; i < 4; i++) { Assert.Equal(i == 3, gesture.Release(20 + i, new(40 * i, 50), repeat + .1 + i * .01)); }
            Assert.False(gesture.IsCaptured);
        }
    }

    [Theory]
    [InlineData("drag")]
    [InlineData("cancel")]
    [InlineData("hold")]
    [InlineData("fifth")]
    [InlineData("earlyRelease")]
    [InlineData("clock")]
    [InlineData("invalid")]
    public void InvalidChordsCannotToggleOrLeaveCaptureStuck(string invalid)
    {
        var gesture = new DebugTapGesture();
        gesture.Press(20, new(0, 50), 1);
        if (invalid == "earlyRelease") { Assert.False(gesture.Release(20, new(0, 50), 1.01)); }
        for (int i = 1; i < 4; i++) { gesture.Press(20 + i, new(40 * i, 50), 1.02); }
        gesture.Press(21, new(40, 50), 1.02); // Duplicate delivery is not a fifth finger.
        if (invalid == "drag") { gesture.Drag(21, new(80, 50), 1.03); gesture.Drag(21, new(40, 50), 1.04); }
        if (invalid == "invalid") { gesture.Drag(21, new(float.NaN, 0), 1.03); }
        if (invalid == "clock") { gesture.Drag(21, new(40, 50), .9); }
        if (invalid == "fifth") { gesture.Press(24, new(160, 50), 1.03); Assert.False(gesture.Release(24, new(160, 50), 1.04)); }
        for (int i = 0; i < 4; i++) { Assert.False(gesture.Release(20 + i, new(40 * i, 50), invalid == "hold" ? 1.5 : 1.1, invalid == "cancel")); }
        Assert.False(gesture.IsCaptured);
    }

    [Fact]
    public void ClearingForFocusOrLayoutChangeRequiresANewChord()
    {
        var gesture = new DebugTapGesture();
        for (int i = 0; i < 4; i++) { gesture.Press(i, Vec2.Zero, 0); }
        gesture.Clear(); Assert.False(gesture.IsCaptured);
        for (int i = 0; i < 4; i++) { Assert.False(gesture.Release(i, Vec2.Zero, .1)); }
        for (int i = 0; i < 3; i++) { gesture.Press(i, Vec2.Zero, 1); }
        for (int i = 0; i < 3; i++) { Assert.False(gesture.Release(i, Vec2.Zero, 1.1)); }
    }
}
