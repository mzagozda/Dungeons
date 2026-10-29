using System.Globalization;

namespace Dungeons.Game.Editor;

/// <summary>Desktop QA contract. Accelerated and short executions cannot satisfy the wall-clock soak gate.</summary>
public sealed record SoakOptions(string OutputDirectory, string ReplayDirectory, int Seconds, bool Fast)
{
    public static SoakOptions Parse(IReadOnlyList<string> args)
    {
        string? output = null, replays = null; int seconds = 7200; bool fast = false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < args.Count; index++)
        {
            string argument = args[index]; int equals = argument.IndexOf('='); string key = equals < 0 ? argument : argument[..equals];
            if (!key.StartsWith("--soak-", StringComparison.Ordinal)) { continue; }
            if (key is not ("--soak-output" or "--soak-replays" or "--soak-seconds" or "--soak-fast")) { throw new FormatException("Unknown soak argument: " + key); }
            if (!seen.Add(key)) { throw new FormatException("Duplicate soak argument: " + key); }
            if (key == "--soak-fast") { if (equals >= 0) { throw new FormatException("--soak-fast takes no value."); } fast = true; continue; }
            string value = equals >= 0 ? argument[(equals + 1)..] : index + 1 < args.Count && !args[index + 1].StartsWith("--", StringComparison.Ordinal) ? args[++index] : "";
            if (string.IsNullOrWhiteSpace(value)) { throw new FormatException("Missing value for " + key); }
            if (key == "--soak-output") { output = Path.GetFullPath(value); }
            else if (key == "--soak-replays") { replays = Path.GetFullPath(value); }
            else if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out seconds) || seconds is < 10 or > 28800)
            { throw new FormatException("--soak-seconds requires 10..28800 actual seconds."); }
        }
        if (output is null || replays is null) { throw new FormatException("--soak-output and --soak-replays are required."); }
        return new(output, replays, seconds, fast);
    }
}

public readonly record struct SoakProgress(double ElapsedSeconds, long SimulatedTicks, ulong RenderedFrames, double LongestFrameGapSeconds,
    int FullClears, int Ambushes, int Respawns, int NormalAttempts);

public static class SoakPolicy
{
    public static bool Acceptance(SoakOptions options, SoakProgress progress, bool nativeRenderer)
        => !options.Fast && nativeRenderer && options.Seconds >= 7200 && double.IsFinite(progress.ElapsedSeconds)
        && progress.ElapsedSeconds >= options.Seconds && double.IsFinite(progress.LongestFrameGapSeconds) && progress.LongestFrameGapSeconds is >= 0 and < 10
        && progress.SimulatedTicks >= progress.ElapsedSeconds * 54 && progress.RenderedFrames >= progress.ElapsedSeconds * 15
        && progress.FullClears > 0 && progress.Ambushes > 0 && progress.Respawns > 0 && progress.NormalAttempts > 0;
}
