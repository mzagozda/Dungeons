using System.Globalization;

namespace Dungeons.Game;

/// <summary>Application flags after Godot's -- separator. Renderer/input flags remain owned by their hosts.</summary>
public sealed class LaunchOptions
{
    public string? Level { get; private set; }
    public string? Replay { get; private set; }
    public string? Validate { get; private set; }
    public string? Screenshot { get; private set; }
    public string? ScreenshotOutput { get; private set; }
    public Rendering.ScreenshotScenario? ScreenshotScenario { get; private set; }
    public ulong? Seed { get; private set; }
    public bool ReplayQuit { get; private set; }
    public bool ListScreenshots { get; private set; }
    public static LaunchOptions Parse(IReadOnlyList<string> arguments)
    {
        var result = new LaunchOptions(); var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < arguments.Count; i++)
        {
            string argument = arguments[i]; int equals = argument.IndexOf('='); string key = equals < 0 ? argument : argument[..equals];
            if (key is not ("--level" or "--seed" or "--replay" or "--headless-validate" or "--screenshot-test" or "--screenshot-output" or "--screenshot-list" or "--replay-quit")) { continue; }
            if (!seen.Add(key)) { throw new FormatException("Duplicate argument: " + key); }
            if (key == "--replay-quit") { if (equals >= 0) { throw new FormatException("--replay-quit takes no value."); } result.ReplayQuit = true; continue; }
            if (key == "--screenshot-list") { if (equals >= 0) { throw new FormatException("--screenshot-list takes no value."); } result.ListScreenshots = true; continue; }
            string value = equals >= 0 ? argument[(equals + 1)..] : i + 1 < arguments.Count && !arguments[i + 1].StartsWith("--", StringComparison.Ordinal) ? arguments[++i] : "";
            if (string.IsNullOrWhiteSpace(value)) { throw new FormatException("Missing value for " + key); }
            switch (key)
            {
                case "--level": result.Level = value; break;
                case "--replay": result.Replay = value; break;
                case "--headless-validate": result.Validate = value; break;
                case "--screenshot-test": result.Screenshot = value; break;
                case "--screenshot-output": result.ScreenshotOutput = value; break;
                case "--seed":
                    if (!ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out ulong seed)) { throw new FormatException("--seed requires an unsigned 64-bit decimal integer."); }
                    result.Seed = seed; break;
            }
        }
        if (result.Validate is not null && (result.Level is not null || result.Replay is not null || result.Seed is not null || result.Screenshot is not null || result.ReplayQuit)) { throw new FormatException("--headless-validate cannot be combined with gameplay flags."); }
        if (result.ListScreenshots && (result.Level is not null || result.Replay is not null || result.Seed is not null || result.Validate is not null || result.Screenshot is not null || result.ScreenshotOutput is not null || result.ReplayQuit)) { throw new FormatException("--screenshot-list cannot be combined with gameplay/validation flags."); }
        if (result.Screenshot is not null && !Rendering.ScreenshotScenarios.IsImagePath(result.Screenshot))
        {
            result.ScreenshotScenario = Rendering.ScreenshotScenarios.Find(result.Screenshot) ?? throw new FormatException("Unknown screenshot scenario: " + result.Screenshot + ". Available: " + string.Join(", ", Rendering.ScreenshotScenarios.All.Select(item => item.Id)));
            if (result.Level is not null || result.Seed is not null || result.ReplayQuit) { throw new FormatException("Named screenshot fixtures cannot be combined with --level, --seed or --replay-quit."); }
            if (result.ScreenshotScenario.NeedsReplay != (result.Replay is not null)) { throw new FormatException(result.ScreenshotScenario.NeedsReplay ? "This screenshot scenario requires --replay." : "This screenshot scenario does not accept --replay."); }
        }
        else
        {
            if (result.ScreenshotOutput is not null) { throw new FormatException("--screenshot-output requires a named --screenshot-test scenario."); }
            if (result.Screenshot is not null && result.Level is null && result.Replay is null) { throw new FormatException("PNG screenshot capture requires --level or --replay."); }
        }
        if (result.ReplayQuit && result.Replay is null) { throw new FormatException("--replay-quit requires --replay."); }
        if (result.Seed is not null && result.Level is null && result.Replay is null) { throw new FormatException("--seed requires --level or --replay."); }
        return result;
    }
}
