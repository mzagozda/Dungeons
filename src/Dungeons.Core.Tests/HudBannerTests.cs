using Dungeons.Game.UI;
using Xunit;

namespace Dungeons.Core.Tests;

public sealed class HudBannerTests
{
    [Fact]
    public void AuthoredMessageUsesCoreExpiryAndTwoTenthsForBothFades()
    {
        var state = new MissionBannerState(); state.Update("A", 60, .1, false); Assert.Equal(.5f, state.Alpha);
        state.Update("A", 60, .1, false); Assert.Equal(1, state.Alpha);
        state.Update("A", 60, 100, false); Assert.Equal(1, state.Alpha); // UI time cannot expire Core's message.
        state.Update("A", 6, 0, false); Assert.Equal(.5f, state.Alpha);
        state.Update("", 0, .1, false); Assert.Equal("", state.Text); Assert.Equal(0, state.Alpha);
    }
    [Fact]
    public void RetriggerReplacementPauseAndInvalidDeltasHaveExplicitBehavior()
    {
        var state = new MissionBannerState(); state.Update("A", 60, .1, false);
        state.Update("A", 60, 1, true); Assert.Equal(.5f, state.Alpha);
        state.Update("A", 60, double.NaN, false); Assert.Equal(.5f, state.Alpha);
        state.Update("A", 60, double.PositiveInfinity, false); Assert.Equal(.5f, state.Alpha);
        state.Retrigger(); state.Update("A", 60, .05, false); Assert.Equal(.25f, state.Alpha);
        state.Update("B", 60, .05, false); Assert.Equal("B", state.Text); Assert.Equal(.25f, state.Alpha);
        state.Update("B", 120, .05, false); Assert.Equal(.25f, state.Alpha);
        state.Clear(); Assert.Equal(0, state.Alpha); Assert.Equal("", state.Text);
    }
    [Fact]
    public void ToastIsNewestWinsFrameIndependentAndFreezesInPause()
    {
        var a = new HudToastState(); var b = new HudToastState(); a.Show("Checkpoint", 3); b.Show("Checkpoint", 3);
        a.Advance(.1, false); for (int i = 0; i < 10; i++) { b.Advance(.01, false); } Assert.Equal(a.Alpha, b.Alpha, 5);
        a.Advance(10, true); Assert.Equal(.5f, a.Alpha); a.Show("Next", 1); a.Advance(.2, false); Assert.Equal("Next", a.Text); Assert.Equal(1, a.Alpha);
        a.Advance(.7, false); Assert.Equal(.5f, a.Alpha, 5); a.Advance(.1, false); Assert.Equal(0, a.Alpha, 5);
        a.Advance(.01, false); Assert.Equal("", a.Text);
        a.Show("Invalid", double.NaN); Assert.Equal("", a.Text); a.Show("Invalid", 0); Assert.Equal("", a.Text);
    }
}
