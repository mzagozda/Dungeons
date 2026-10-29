using Dungeons.Game.Editor;
using Xunit;

namespace Dungeons.Core.Tests;

public sealed class SoakPolicyTests
{
    [Fact]
    public void AcceptanceRequiresActualDurationActiveTicksRenderedFramesAndAllFlows()
    {
        var options = new SoakOptions("unused", "unused", 7200, false);
        var valid = new SoakProgress(7200, 388800, 108000, .5, 1, 1, 1, 1);
        Assert.True(SoakPolicy.Acceptance(options, valid, true));
        Assert.False(SoakPolicy.Acceptance(options with { Fast = true }, valid, true));
        Assert.False(SoakPolicy.Acceptance(options with { Seconds = 7199 }, valid, true));
        Assert.False(SoakPolicy.Acceptance(options, valid, false));
        foreach (SoakProgress invalid in new[]
        {
            valid with { ElapsedSeconds = 7199.999 }, valid with { ElapsedSeconds = double.NaN }, valid with { ElapsedSeconds = double.PositiveInfinity },
            valid with { SimulatedTicks = 388799 }, valid with { RenderedFrames = 107999 }, valid with { LongestFrameGapSeconds = 10 },
            valid with { LongestFrameGapSeconds = double.NaN }, valid with { LongestFrameGapSeconds = -.1 }, valid with { FullClears = 0 }, valid with { Ambushes = 0 },
            valid with { Respawns = 0 }, valid with { NormalAttempts = 0 },
        }) { Assert.False(SoakPolicy.Acceptance(options, invalid, true)); }
    }

    [Fact]
    public void ArgumentsKeepUnicodePathsAndAnExplicitDiagnosticMode()
    {
        string output = Path.Combine(Path.GetTempPath(), "soak Résumé Ω-" + Guid.NewGuid().ToString("N")), replays = Path.Combine(Path.GetTempPath(), "replays");
        SoakOptions normal = SoakOptions.Parse(["--soak-output", output, "--soak-replays=" + replays, "--quality=High"]);
        Assert.Equal(Path.GetFullPath(output), normal.OutputDirectory); Assert.Equal(Path.GetFullPath(replays), normal.ReplayDirectory);
        Assert.Equal(7200, normal.Seconds); Assert.False(normal.Fast);
        SoakOptions diagnostic = SoakOptions.Parse(["--soak-output=" + output, "--soak-replays", replays, "--soak-seconds=10", "--soak-fast"]);
        Assert.Equal(10, diagnostic.Seconds); Assert.True(diagnostic.Fast); Assert.False(Directory.Exists(output));
    }

    [Theory]
    [InlineData("--soak-seconds=9")]
    [InlineData("--soak-seconds=28801")]
    [InlineData("--soak-seconds=NaN")]
    [InlineData("--soak-seconds=7200.0")]
    [InlineData("--soak-typo=true")]
    [InlineData("--soak-fast=true")]
    [InlineData("--soak-output=duplicate")]
    public void MalformedOrAmbiguousFlagsFailBeforeCreatingEvidence(string bad)
        => Assert.Throws<FormatException>(() => SoakOptions.Parse(["--soak-output=unused", "--soak-replays=unused", bad]));

    [Fact]
    public void MissingValuesAndRequiredPathsFail()
    {
        Assert.Throws<FormatException>(() => SoakOptions.Parse([]));
        Assert.Throws<FormatException>(() => SoakOptions.Parse(["--soak-output", "--soak-replays=unused"]));
        Assert.Throws<FormatException>(() => SoakOptions.Parse(["--soak-output=unused", "--soak-replays=unused", "--soak-fast", "--soak-fast"]));
    }
}
