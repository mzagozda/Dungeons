using Dungeons.Core.Level;
using Dungeons.Core.Sim;
using Dungeons.Game.Logging;
using System.Text.Json.Nodes;
using Godot;

namespace Dungeons.Game;

public partial class AppRoot
{
    private readonly string[]? _launchArguments;
    private LaunchOptions _launch = new();
    private string? _launchError;
    private bool _replayMode;
    private bool _quitting;
    private async void QuitApp(int exitCode = 0)
    {
        if (_quitting) { return; } _quitting = true;
        CancelStartup(); ClearBoot();
        SceneTree tree = GetTree(); ReleaseGame(); _audio?.StopForShutdown(); _logs?.FlushNow();
        // Let queued scene removal and stopped audio playbacks drain before native server teardown.
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        // Fixed-FPS/headless frames can finish faster than one real audio mix. The
        // mixer releases stopped Vorbis playbacks on its wall-clock thread.
        if (_audio is not null) { await Task.Delay(TimeSpan.FromSeconds(System.Math.Clamp(AudioServer.GetOutputLatency() + AudioServer.GetTimeToNextMix(), .1, 1))); }
        // C# resource wrappers can outlive their freed scene. Finalize them while
        // the rendering server still exists, then drain deferred RID releases.
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        tree.Quit(exitCode);
    }
    public AppRoot(string dataDirectory, string[] arguments) : this(dataDirectory) { _launchArguments = arguments.ToArray(); }
    private string[] LaunchArguments => _launchArguments ?? OS.GetCmdlineUserArgs();
    private void ValidateAndQuit()
    {
        if (_launchError is not null) { Console.Error.WriteLine("COMMAND_LINE=FAIL " + _launchError); GetTree().Quit(2); return; }
        if (_launch.ListScreenshots)
        {
            var catalogue = new JsonArray(Rendering.ScreenshotScenarios.All.Select(scenario => (JsonNode)new JsonObject
            {
                ["id"] = scenario.Id, ["scene"] = scenario.Scene, ["outputFlag"] = scenario.OutputFlag, ["expectedMarker"] = scenario.ExpectedMarker,
                ["arguments"] = new JsonArray(scenario.Arguments.Select(arg => (JsonNode?)JsonValue.Create(arg)).ToArray()), ["needsReplay"] = scenario.NeedsReplay,
                ["width"] = scenario.Width, ["height"] = scenario.Height,
            }).ToArray());
            GD.Print("SCREENSHOT_SCENARIOS=" + catalogue.ToJsonString());
            GetTree().Quit(); return;
        }
        try
        {
            var content = ContentBridge.LoadRegistry(); LevelLoadResult result = RuntimeLevelFiles.Load(_launch.Validate!, content); int errors = 0, warnings = 0;
            foreach (Diagnostic diagnostic in result.Diagnostics)
            { GD.Print(diagnostic.ToString()); if (diagnostic.Severity == DiagnosticSeverity.Error) { errors++; } else if (diagnostic.Severity == DiagnosticSeverity.Warning) { warnings++; } }
            GD.Print($"HEADLESS_VALIDATE={(result.Success ? "PASS" : "FAIL")} errors={errors} warnings={warnings} level={result.Document?.Id ?? "unknown"}"); GetTree().Quit(result.Success ? 0 : 1);
        }
        catch (Exception error) { Console.Error.WriteLine("HEADLESS_VALIDATE=FAIL " + error.Message); GetTree().Quit(error is InvalidDataException ? 1 : 2); }
    }
    private LevelDocument ResolveLevel(string value)
    {
        CatalogEntry? entry = _catalog.FirstOrDefault(item => item.Id == value);
        if (entry is not null)
        { if (!entry.IsPlayable) { throw new InvalidDataException(string.Join('\n', entry.Diagnostics)); } return entry.Document!; }
        LevelLoadResult result = RuntimeLevelFiles.Load(value, _content);
        foreach (Diagnostic diagnostic in result.Diagnostics)
        { _logs?.Logger.Log(diagnostic.Severity switch { DiagnosticSeverity.Error => Dungeons.Core.Util.LogLevel.Error, DiagnosticSeverity.Warning => Dungeons.Core.Util.LogLevel.Warning, _ => Dungeons.Core.Util.LogLevel.Info }, "Level", diagnostic.ToString()); }
        return result.Success ? result.Document! : throw new InvalidDataException(string.Join('\n', result.Diagnostics));
    }
    private void LaunchRequestedLevel()
    {
        ReplayDocument? replay = _launch.Replay is null ? null : RuntimeLevelFiles.Replay(_launch.Replay);
        if (replay is not null && _launch.Seed is ulong seed && seed != replay.Snapshot.Seed) { throw new InvalidDataException("--seed differs from the replay's recorded seed."); }
        string? requested = _launch.Level ?? replay?.Snapshot.LevelId;
        if (requested is null) { MainMenu(); return; }
        StartDocument(ResolveLevel(requested), _launch.Seed, replay);
    }
    private void HandleLaunchFailure(Exception error)
    {
        ReleaseGame();
        if (error is InvalidDataException or IOException or FormatException or UnauthorizedAccessException)
        { _logs?.Logger.Log(Dungeons.Core.Util.LogLevel.Error, "Level", error.Message); _logs?.FlushNow(); }
        else { GameLog.Error("View", error.ToString()); }
        if (_replayMode) { Console.Error.WriteLine("REPLAY_PLAYBACK=FAIL " + error.Message); }
        if (DisplayServer.GetName() == "headless" || _launch.ReplayQuit || _screenshotTest is not null)
        { Console.Error.WriteLine("APP_LAUNCH=FAIL " + error.Message); QuitApp(1); }
        else { ShowError(error.Message); }
    }
    private void StartDocument(LevelDocument document, ulong? seed = null, ReplayDocument? replay = null)
    {
        try
        {
            _replayMode = replay is not null;
            if (replay is null && _runs is null && _screenshotTest is null) { _runs = new(DataDirectory, message => GameLog.Warning("Io", message)); }
            ulong? authoredSeed = document.ToJsonObject()["seed"]?.GetValue<ulong>();
            var ticket = replay is null ? _runs?.Begin(document.Id, authoredSeed, seed) : null; ulong actualSeed = replay?.Snapshot.Seed ?? ticket?.Seed ?? seed ?? authoredSeed ?? 1;
            _menu?.QueueFree(); _menu = null;
            _game = _screenshotTest is not null || _launch.ReplayQuit ? new Rendering.CaptureGameplayScreen() : new GameplayScreen();
            _game.Name = "Gameplay"; AddChild(_game);
            if (_launch.ReplayQuit && _screenshotTest is not null && DisplayServer.GetName() != "headless")
            { DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled); GD.Print("REPLAY_CAPTURE=uncapped"); }
            if (replay is null) { _game.Build(document, _content, actualSeed, _runs, ticket); } else { _game.BuildReplay(document, _content, replay); }
            _game.QuitRequested += () => { ReleaseGame(); _replayMode = false; Levels(); };
            _game.Failed += HandleLaunchFailure;
            _game.RestartRequested += () => { ReleaseGame(); StartDocument(document, actualSeed, replay); };
            _game.SettingsRequested += OpenSettings;
            _game.ReplayFinished += () => { if (_launch.ReplayQuit && _screenshotTest is null) { QuitApp(); } };
        }
        catch (Exception error) { HandleLaunchFailure(error); }
    }
}
