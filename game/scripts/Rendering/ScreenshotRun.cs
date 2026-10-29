using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dungeons.Core.Sim;
using Godot;

namespace Dungeons.Game.Rendering;

/// <summary>One named capture's isolated arguments, output and provenance; never loads a player profile.</summary>
public static class ScreenshotRun
{
    private static string[]? _arguments;
    private static GameplayScreen? _game;
    public static ScreenshotScenario? Scenario { get; private set; }
    public static ReplayDocument? Replay { get; private set; }
    public static bool Active => Scenario is not null;
    public static string[] Arguments => _arguments ?? OS.GetCmdlineUserArgs();
    internal static void Bind(GameplayScreen game) => _game = game;
    internal static void Unbind(GameplayScreen game) { if (ReferenceEquals(_game, game)) { _game = null; } }
    internal static bool HasBoundGame => _game is not null;
    internal static void Configure(ScreenshotScenario scenario, string output, string? replay)
    {
        var arguments = scenario.Arguments.ToList(); arguments.Add(scenario.OutputFlag + "=" + output);
        if (scenario.NeedsReplay) { Replay = RuntimeLevelFiles.Replay(replay!); arguments.Add("--m6-replay=" + Path.GetFullPath(replay!)); }
        _arguments = arguments.ToArray(); Scenario = scenario;
        RenderingServer.GlobalShaderParameterSet("capture_time", 0f);
    }
    public static Error Save(Image image, string path)
    {
        if (!Active) { return image.SavePng(path); }
        try { using var file = new FileStream(path, FileMode.CreateNew, System.IO.FileAccess.Write); file.Write(image.SavePngToBuffer()); return Error.Ok; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { Console.Error.WriteLine("SCREENSHOT_SCENARIO=FAIL " + error.Message); return Error.CantCreate; }
    }
    public static void WriteText(string path, string text)
    {
        if (!Active) { File.WriteAllText(path, text); return; }
        using var writer = new StreamWriter(new FileStream(path, FileMode.CreateNew, System.IO.FileAccess.Write)); writer.Write(text);
    }
    public static void Complete(bool passed, Image image, GameSession? session = null, int? captureTick = null, ulong? captureHash = null)
    {
        if (Scenario is null) { return; }
        if (!passed) { GD.Print("SCREENSHOT_SCENARIO=FAIL name=" + Scenario.Id); return; }
        session ??= _game?.Session;
        string output = Arguments.Single(arg => arg.StartsWith(Scenario.OutputFlag + "=", StringComparison.Ordinal))[(Scenario.OutputFlag.Length + 1)..];
        string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(output))).ToLowerInvariant();
        WriteText(Path.ChangeExtension(output, ".capture.json"), new JsonObject
        {
            ["formatVersion"] = 1, ["scenario"] = Scenario.Id, ["passed"] = passed, ["width"] = image.GetWidth(), ["height"] = image.GetHeight(), ["imageSha256"] = hash,
            ["godot"] = Engine.GetVersionInfo()["string"].AsString(), ["renderer"] = RenderingServer.GetCurrentRenderingMethod(), ["adapter"] = RenderingServer.GetVideoAdapterName(),
            ["quality"] = GraphicsRuntime.Instance?.Options.Preset.ToString(), ["shaderTime"] = 0, ["visualStepSeconds"] = GameConstants.Dt,
            ["levelHash"] = session?.World.SourceHash, ["contentHash"] = session?.World.ContentHash, ["seed"] = session?.World.Seed, ["tick"] = captureTick ?? session?.Tick, ["finalHash"] = (captureHash ?? session?.Hash())?.ToString("x16"),
            ["baselineApproved"] = false, ["physicalAcceptance"] = false
        }.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        GD.Print($"SCREENSHOT_SCENARIO={(passed ? "PASS" : "FAIL")} name={Scenario.Id} output={output} sha256={hash}");
    }
}
