using Dungeons.Game;
using Dungeons.Game.Rendering;
using Xunit;

namespace Dungeons.Core.Tests;

public sealed class ScreenshotScenarioTests
{
    [Fact]
    public void EveryNamedFixtureAcceptsBothCommandFormsAndHasUniqueBoundedMetadata()
    {
        Assert.Equal(22, ScreenshotScenarios.All.Count); Assert.Equal(22, ScreenshotScenarios.All.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count());
        foreach (ScreenshotScenario scenario in ScreenshotScenarios.All)
        {
            var arguments = new List<string> { "--screenshot-test", scenario.Id, "--screenshot-output=Résumé Ω/example.png" };
            if (scenario.NeedsReplay) { arguments.AddRange(["--replay", "full clear.dreplay"]); }
            LaunchOptions options = LaunchOptions.Parse(arguments); Assert.Same(scenario, options.ScreenshotScenario); Assert.Equal("Résumé Ω/example.png", options.ScreenshotOutput);
            arguments[0] = "--screenshot-test=" + scenario.Id; arguments.RemoveAt(1); Assert.Same(scenario, LaunchOptions.Parse(arguments).ScreenshotScenario);
            Assert.StartsWith("res://scenes/", scenario.Scene, StringComparison.Ordinal); Assert.EndsWith(".tscn", scenario.Scene, StringComparison.Ordinal);
            Assert.StartsWith("--", scenario.OutputFlag, StringComparison.Ordinal); Assert.DoesNotContain(scenario.Arguments, arg => arg.StartsWith(scenario.OutputFlag, StringComparison.Ordinal));
            Assert.True(scenario.Width >= 0 && scenario.Height >= 0); Assert.Equal(scenario.Width == 0, scenario.Height == 0);
        }
        Assert.True(LaunchOptions.Parse(["--screenshot-list"]).ListScreenshots); Assert.Null(ScreenshotScenarios.Find("missing"));
    }
    [Fact]
    public void NamedFixturesRejectAmbiguousInputsAndDoNotConfuseLegacyPngPathsWithNames()
    {
        string[][] invalid = [["--screenshot-test=typo"], ["--screenshot-test=hero-roll", "--seed=1"], ["--screenshot-test=hero-roll", "--level=x"], ["--screenshot-test=hero-roll", "--replay=x"], ["--screenshot-test=mission-complete"], ["--screenshot-test=mission-complete", "--replay=x", "--replay-quit"], ["--screenshot-output=x.png"], ["--screenshot-test=x.png"], ["--screenshot-test=x.png", "--level=x", "--screenshot-output=y.png"], ["--screenshot-list=x"], ["--screenshot-list", "--headless-validate=x"], ["--screenshot-list", "--screenshot-test=hero-roll"], ["--screenshot-test=hero-roll", "--screenshot-output=x", "--screenshot-output=y"]];
        foreach (string[] args in invalid) { Assert.Throws<FormatException>(() => LaunchOptions.Parse(args)); }
        LaunchOptions legacy = LaunchOptions.Parse(["--level=x", "--screenshot-test=C:/images/hero-roll.png"]); Assert.Null(legacy.ScreenshotScenario); Assert.Equal("C:/images/hero-roll.png", legacy.Screenshot);
        Assert.Null(LaunchOptions.Parse(["--replay=x", "--screenshot-test=out.png"]).ScreenshotScenario);
    }
}
