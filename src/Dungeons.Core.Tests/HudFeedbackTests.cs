using Dungeons.Game.UI;
using Xunit;

namespace Dungeons.Core.Tests;

public sealed class HudFeedbackTests
{
    [Fact]
    public void WarningUsesStrictQuarterHealthAndClearsOnHealingDeathOrInvalidMaximum()
    {
        var warning = new LowHealthFeedback(); warning.Advance(25, 100, true, 0, false, false); Assert.False(warning.Active);
        warning.Advance(24, 100, true, 0, false, false); Assert.True(warning.Active); Assert.Equal(1, warning.BorderBlend);
        warning.Advance(24, 100, false, 0, false, false); Assert.False(warning.Active); Assert.Equal(0, warning.BorderBlend);
        warning.Advance(1, 0, true, 0, false, false); Assert.False(warning.Active);
        warning.Advance(0, 100, true, 0, false, false); Assert.False(warning.Active);
        warning.Advance(int.MaxValue / 4, int.MaxValue, true, 0, false, false); Assert.True(warning.Active);
        warning.Advance(int.MaxValue, int.MaxValue, true, 0, false, false); Assert.False(warning.Active);
    }
    [Fact]
    public void PulseIsFrameIndependentBoundedAndPauseAwareWithStaticReduceMotionWarning()
    {
        var a = new LowHealthFeedback(); var b = new LowHealthFeedback();
        for (int i = 0; i < 60; i++) { a.Advance(1, 100, true, 1d / 60, false, false); }
        b.Advance(1, 100, true, 1, false, false); Assert.Equal(a.BorderBlend, b.BorderBlend, 5);
        float frozen = a.BorderBlend; a.Advance(1, 100, true, 5, true, false); Assert.Equal(frozen, a.BorderBlend);
        a.Advance(1, 100, true, double.NaN, false, false); Assert.Equal(frozen, a.BorderBlend);
        a.Advance(1, 100, true, double.PositiveInfinity, false, false); Assert.Equal(frozen, a.BorderBlend);
        for (int i = 0; i < 100; i++) { a.Advance(1, 100, true, .1, false, false); Assert.InRange(a.BorderBlend, .29999f, 1); }
        a.Advance(1, 100, true, .1, false, true); Assert.Equal(1, a.BorderBlend);
        a.Advance(1, 100, true, .4, false, true); Assert.Equal(1, a.BorderBlend);
        a.Advance(100, 100, true, 0, false, false); Assert.Equal(0, a.BorderBlend);
        a.Advance(1, 100, true, 0, false, false); Assert.Equal(1, a.BorderBlend);
    }
    [Fact]
    public void CooldownUsesAuthoringMultiplierCapAndOverflowSafeSeconds()
    {
        Assert.Equal(30, HudCooldown.Seconds(1800)); Assert.Equal(1, HudCooldown.Seconds(1)); Assert.Equal(0, HudCooldown.Seconds(-1));
        Assert.Equal(35791395, HudCooldown.Seconds(int.MaxValue));
        Assert.Equal(.5f, HudCooldown.Fraction(450, 1800, .5f)); Assert.Equal(.5f, HudCooldown.Fraction(1800, 1800, 2));
        Assert.Equal(1, HudCooldown.Fraction(int.MaxValue, int.MaxValue, 100)); Assert.Equal(0, HudCooldown.Fraction(0, 1800, 1));
        Assert.Equal(1, HudCooldown.Fraction(1, 0, 1)); Assert.Equal(1, HudCooldown.Fraction(1, 1800, float.NaN));
    }
}
