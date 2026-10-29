using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Dungeons.Core.Sim;
using Dungeons.Game.UI;
using Godot;

namespace Dungeons.Game.Editor;

/// <summary>Single-process desktop soak: real replay/render/audio/results, catalogue and ordinary recorded attempts.</summary>
public partial class SoakHarness : Node
{
    private enum Phase { Boot, Replay, Results, Catalogue, NormalBoot, Normal, NormalQuit, Drain }
    private static readonly string[] Names = ["mossy_hollow_full_clear", "mossy_hollow_ambush_only", "mossy_hollow_death_respawn"];
    private readonly Stopwatch _clock = new();
    private readonly int[] _completed = new int[3];
    private readonly ReplayDocument[] _replays = new ReplayDocument[3];
    private SoakOptions? _options;
    private AppRoot? _app;
    private GameplayScreen? _game;
    private Phase _phase;
    private double _phaseStart, _lastFrame, _nextSample, _lastProgress, _longestGap;
    private long _ticks;
    private int _normalFinalTick;
    private ulong _normalFinalHash;
    private int _case, _cycles, _lastTick, _normalAttempts, _resumes, _drainFrames;
    private bool _finishing, _normalVisited, _pauseExercised, _ownsOutput;
    private int _initialDrawn;
    private int _previousMaxFps;
    private bool _previousLowCpu;
    private StreamWriter? _samples;
    private long _peakWorkingSet, _peakManaged;
    private Exception? _shutdownFailure;
    private int _shutdownStage, _shutdownFrames;
    private ulong _loadingFocusGame;
    private int _loadingFocusChecks, _loadingFocusBeforeReady;
    private readonly Stopwatch _shutdownClock = new();
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    public override void _Ready()
    {
        ProcessPriority = 2000;
        try
        {
            _options = SoakOptions.Parse(OS.GetCmdlineUserArgs());
            if (Directory.Exists(_options.OutputDirectory) || File.Exists(_options.OutputDirectory)) { throw new IOException("Soak output directory must be new."); }
            Directory.CreateDirectory(_options.OutputDirectory);
            _ownsOutput = true;
            for (int index = 0; index < Names.Length; index++)
            {
                string source = Path.Combine(_options.ReplayDirectory, Names[index] + ".dreplay");
                _replays[index] = RuntimeLevelFiles.Replay(source);
                using Stream output = new FileStream(Path.Combine(_options.OutputDirectory, Names[index] + ".dreplay"), FileMode.CreateNew);
                using Stream input = File.OpenRead(source); input.CopyTo(output);
            }
            _previousMaxFps = Engine.MaxFps; _previousLowCpu = OS.LowProcessorUsageMode;
            Engine.MaxFps = 60; OS.LowProcessorUsageMode = false; _initialDrawn = Engine.GetFramesDrawn();
            WriteNew("identity.json", new
            {
                formatVersion = 1, requestedSeconds = _options.Seconds, acceleratedDiagnostic = _options.Fast,
                utc = DateTime.UtcNow.ToString("o"), processId = System.Environment.ProcessId, os = OS.GetName(),
                godot = Engine.GetVersionInfo()["string"].AsString(), display = DisplayServer.GetName(),
                renderer = RenderingServer.GetCurrentRenderingMethod(), adapter = RenderingServer.GetVideoAdapterName(),
                coreModuleId = typeof(GameSession).Module.ModuleVersionId.ToString(), gameModuleId = typeof(AppRoot).Module.ModuleVersionId.ToString(),
                replays = Names.Select((name, index) => new { name, sha256 = Hash(Path.Combine(_options.OutputDirectory, name + ".dreplay")), _replays[index].LevelHash, _replays[index].ContentHash, _replays[index].Snapshot.TickCount, finalHash = _replays[index].Snapshot.FinalHash.ToString("x16") }),
                physicalMobileAcceptance = false, reviewedGolden = false, audioListeningAcceptance = false,
            });
            _samples = new(new FileStream(Path.Combine(_options.OutputDirectory, "samples.jsonl"), FileMode.CreateNew)) { AutoFlush = true };
            _clock.Start(); StartCycle(); Sample();
        }
        catch (Exception error) { Fail(error); }
    }

    public override void _Process(double delta)
    {
        if (_finishing) { AdvanceShutdown(); return; }
        if (_options is null || !_clock.IsRunning) { return; }
        try
        {
            double now = _clock.Elapsed.TotalSeconds; _longestGap = System.Math.Max(_longestGap, now - _lastFrame); _lastFrame = now;
            if (now >= _nextSample) { Sample(); }
            if (_longestGap >= 10) { throw new InvalidDataException("Frame loop stalled for at least ten wall-clock seconds."); }
            switch (_phase)
            {
                case Phase.Boot:
                    GameplayScreen? loaded = CurrentGame();
                    ResumeDuringLoading(loaded);
                    if (loaded?.Session is not null && loaded.View.ReadyForPlay)
                    {
                        _game = loaded; _lastTick = 0; _lastProgress = now; _game.SetProcessUnhandledInput(false); _game.VibrationEnabled = false;
                        _game.Failed += Fail;
                        if (_options.Fast) { _game.SetProcess(false); }
                        Change(Phase.Replay);
                    }
                    else { Deadline(30, "replay loading"); }
                    break;
                case Phase.Replay:
                    ResumeIfPaused();
                    if (_options.Fast) { for (int step = 0; step < 12 && !_game!.ReplayComplete; step++) { _game._Process(.25); } }
                    CountTicks();
                    if (_game!.ReplayComplete)
                    {
                        ReplayDocument replay = _replays[_case];
                        if (_game.Session.Tick != replay.Snapshot.TickCount || _game.Session.Hash() != replay.Snapshot.FinalHash) { throw new InvalidDataException("Rendered soak replay differs from its verified source."); }
                        if (_case == 0 && (_game.Session.State != SessionState.Won || !_game.ResultsVisible)) { throw new InvalidDataException("Full-clear replay has no genuine result controls."); }
                        if (_case == 2 && _game.Session.World.Deaths == 0) { throw new InvalidDataException("Respawn fixture did not exercise a death."); }
                        _completed[_case]++; _cycles++; Sample();
                        GD.Print($"SOAK_CYCLE=PASS case={Names[_case]} cycle={_cycles} tick={_game.Session.Tick} hash={_game.Session.Hash():x16}");
                        Change(Phase.Results);
                    }
                    else if (now - _lastProgress > 30) { throw new InvalidDataException("Replay made no simulation progress for thirty seconds."); }
                    break;
                case Phase.Results:
                    if (now - _phaseStart < (_options.Fast ? .1 : 2)) { break; }
                        if (_case == 0) { ((Button)_game!.GetNode("ResultsScreen").FindChild("PrimaryAction", true, false)).EmitSignal(BaseButton.SignalName.Pressed); }
                    else { _game!.GetNode("ReplayStatus").FindChildren("*", "Button", true, false).OfType<Button>().Single().EmitSignal(BaseButton.SignalName.Pressed); }
                    _game = null; Change(Phase.Catalogue);
                    break;
                case Phase.Catalogue:
                    LevelBrowserView? browser = _app!.FindChildren("LevelBrowser", "ScrollContainer", true, false).OfType<LevelBrowserView>().SingleOrDefault();
                    if (browser is null) { Deadline(10, "catalogue return"); break; }
                    if (!_normalVisited)
                    {
                        browser.Cards.Single(card => card.Entry.Id == _replays[0].Snapshot.LevelId).SelectButton.EmitSignal(BaseButton.SignalName.Pressed);
                        Button play = _app.FindChildren("*", "Button", true, false).OfType<Button>().First(button => button.IsVisibleInTree() && button.Text == _app.Tr("PLAY"));
                        play.EmitSignal(BaseButton.SignalName.Pressed); Change(Phase.NormalBoot);
                    }
                    else { Drain(); }
                    break;
                case Phase.NormalBoot:
                    GameplayScreen? normal = CurrentGame();
                    ResumeDuringLoading(normal);
                    if (normal?.Session is not null && normal.View.ReadyForPlay && !normal.IsReplay)
                    {
                        _game = normal; _lastTick = 0; _lastProgress = now; _pauseExercised = false;
                        _game.SetProcessUnhandledInput(false); _game.VibrationEnabled = false; _game.Failed += Fail; Change(Phase.Normal);
                    }
                    else { Deadline(30, "ordinary play loading"); }
                    break;
                case Phase.Normal:
                    ResumeIfPaused(); CountTicks();
                    if (!_pauseExercised && now - _phaseStart > (_options.Fast ? .1 : 2))
                    { PauseAction(_game!); _pauseExercised = true; }
                    if (now - _phaseStart >= (_options.Fast ? 1 : 10))
                    {
                        if (_game!.Session.Recorder is null) { throw new InvalidDataException("Ordinary play did not create a recording/profile attempt."); }
                        _normalFinalTick = _game.Session.Tick; _normalFinalHash = _game.Session.Hash();
                        if (_game.Session.State == SessionState.Running) { PauseAction(_game); }
                        Button quit = _game.FindChildren("*", "Button", true, false).OfType<Button>().First(button => button.IsVisibleInTree() && button.Text == _app!.Tr("QUIT_TO_LEVELS"));
                        quit.EmitSignal(BaseButton.SignalName.Pressed); _normalAttempts++; _normalVisited = true; _game = null; Change(Phase.NormalQuit);
                    }
                    break;
                case Phase.NormalQuit:
                    if (_app!.FindChildren("LevelBrowser", "ScrollContainer", true, false).OfType<LevelBrowserView>().Any())
                    {
                        var store = new Persistence.RunStore(Path.Combine(_options.OutputDirectory, "profile"));
                        if (store.Profile.Levels[_replays[0].Snapshot.LevelId].Attempts != _normalAttempts) { throw new InvalidDataException("Normal attempt profile was not persisted exactly once."); }
                        VerifyNormalArchive();
                        Drain();
                    }
                    else { Deadline(10, "normal attempt close"); }
                    break;
                case Phase.Drain:
                    if (++_drainFrames < 5) { break; }
                    if (now >= _options.Seconds && _completed.All(count => count > 0) && _normalAttempts > 0) { Finish(); }
                    else { _case = (_case + 1) % Names.Length; StartCycle(); }
                    break;
            }
        }
        catch (Exception error) { Fail(error); }
    }

    private void StartCycle()
    {
        _normalVisited = _case != 0;
        string data = Path.Combine(_options!.OutputDirectory, "profile");
        _app = new AppRoot(data, ["--replay=" + Path.Combine(_options.OutputDirectory, Names[_case] + ".dreplay")]) { Name = "SoakApp" };
        AddChild(_app); _game = null; Change(Phase.Boot);
    }
    private GameplayScreen? CurrentGame() => _app?.GetChildren().OfType<GameplayScreen>().FirstOrDefault(game => !game.IsQueuedForDeletion());
    private void Change(Phase phase) { _phase = phase; _phaseStart = _clock.Elapsed.TotalSeconds; }
    private void Deadline(double seconds, string operation) { if (_clock.Elapsed.TotalSeconds - _phaseStart > seconds) { throw new TimeoutException("Soak stalled during " + operation); } }
    private void CountTicks()
    {
        int tick = _game!.Session.Tick;
        if (tick < _lastTick) { throw new InvalidDataException("Tick counter regressed within a soak scene."); }
        if (tick > _lastTick) { _ticks += tick - _lastTick; _lastProgress = _clock.Elapsed.TotalSeconds; }
        _lastTick = tick;
    }
    private static void PauseAction(GameplayScreen game)
    { using var action = new InputEventAction { Action = "pause", Pressed = true }; game._UnhandledInput(action); }
    private void ResumeIfPaused(GameplayScreen? target = null)
    { target ??= _game; if (target?.Session is GameSession session && session.State == SessionState.Paused) { PauseAction(target); _resumes++; } }
    private void ResumeDuringLoading(GameplayScreen? game)
    {
        if (game?.Session is null) { return; }
        if (_options!.Fast && _loadingFocusGame != game.GetInstanceId())
        {
            _loadingFocusGame = game.GetInstanceId(); _loadingFocusChecks++;
            if (!game.View.ReadyForPlay) { _loadingFocusBeforeReady++; }
            game._Notification((int)NotificationApplicationFocusOut);
            if (game.Session.State != SessionState.Paused) { throw new InvalidDataException("Loading focus-loss diagnostic did not pause the actual game."); }
        }
        ResumeIfPaused(game);
    }
    private void VerifyNormalArchive()
    {
        string folder = Path.Combine(_options!.OutputDirectory, "profile", "replays");
        string latest = Directory.GetFiles(folder, "run-*.dreplay").OrderDescending(StringComparer.Ordinal).First();
        ReplayDocument replay = RuntimeLevelFiles.Replay(latest);
        if (replay.Snapshot.TickCount != _normalFinalTick || replay.Snapshot.FinalHash != _normalFinalHash) { throw new InvalidDataException("Persisted ordinary replay differs from the closed attempt."); }
        var content = ContentBridge.LoadRegistry(); var level = ContentBridge.LoadBuiltins(content).Single(level => level.Id == replay.Snapshot.LevelId);
        ReplayVerification verification = ReplayVerifier.Verify(GameSession.Create(level, content, replay.Snapshot.Seed, replay.Snapshot.Difficulty, startOverride: replay.Snapshot.StartOverride), replay);
        if (!verification.Success) { throw new InvalidDataException("Persisted ordinary replay failed Core verification: " + verification.Message); }
        string archive = Path.Combine(_options.OutputDirectory, "attempts"); Directory.CreateDirectory(archive);
        File.Copy(latest, Path.Combine(archive, $"attempt-{_normalAttempts:D4}.dreplay"), false);
        WriteNew($"attempt-{_normalAttempts:D4}.json", new { tick = _normalFinalTick, hash = _normalFinalHash.ToString("x16"), sha256 = Hash(latest), verified = true, reviewedGolden = false });
    }
    private void Drain() { _app!.QueueFree(); _app = null; _game = null; _drainFrames = 0; Change(Phase.Drain); }
    private SoakProgress Progress() => new(_clock.Elapsed.TotalSeconds, _ticks, (ulong)(Engine.GetFramesDrawn() - _initialDrawn), _longestGap, _completed[0], _completed[1], _completed[2], _normalAttempts);
    private void Sample()
    {
        using Process process = Process.GetCurrentProcess(); long working = process.WorkingSet64, managed = GC.GetTotalMemory(false);
        _peakWorkingSet = System.Math.Max(_peakWorkingSet, working); _peakManaged = System.Math.Max(_peakManaged, managed);
        object sample = new
        {
            utc = DateTime.UtcNow.ToString("o"), processId = System.Environment.ProcessId, phase = _phase.ToString(), caseName = Names[_case], cycles = _cycles,
            progress = Progress(), workingSetBytes = working, managedBytes = managed,
            nodes = Performance.GetMonitor(Performance.Monitor.ObjectNodeCount), resources = Performance.GetMonitor(Performance.Monitor.ObjectResourceCount),
            orphans = Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount), textureBytes = Performance.GetMonitor(Performance.Monitor.RenderTextureMemUsed),
            aliveEntities = _game?.Session.World.Entities.Count, _resumes,
        };
        _samples?.WriteLine(JsonSerializer.Serialize(sample));
        string target = Path.Combine(_options!.OutputDirectory, "heartbeat.json"), temporary = target + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(sample, _json));
        for (int attempt = 0; ; attempt++)
        {
            try { File.Move(temporary, target, true); break; }
            catch (IOException) when (attempt < 3) { Thread.Sleep(5); }
        }
        _nextSample = _clock.Elapsed.TotalSeconds + 5;
    }
    private void Fail(Exception error)
    {
        Console.Error.WriteLine("SOAK=FAIL " + error); Finish(error);
    }
    private void Finish(Exception? failure = null)
    {
        if (_finishing) { return; } _finishing = true; _shutdownFailure = failure; _shutdownClock.Restart();
        try
        {
            _game?.CloseAttempt(); _app?.QueueFree(); _app = null; _game = null;
        }
        catch (Exception error) { _shutdownFailure ??= error; Console.Error.WriteLine("SOAK=FAIL during release " + error); }
    }
    private void AdvanceShutdown()
    {
        try
        {
            // Keep the tree/server alive while queued nodes, mixer playbacks and
            // native resources drain. Leave wrapper collection to the engine's
            // normal lifecycle; no ToSignal awaiter survives tree shutdown.
            if (_shutdownStage == 0)
            {
                if (++_shutdownFrames < 3) { return; }
                _shutdownStage = 1; _shutdownClock.Restart(); return;
            }
            if (_shutdownStage == 1)
            {
                double drain = System.Math.Clamp(AudioServer.GetOutputLatency() + AudioServer.GetTimeToNextMix(), .2, 1);
                if (_shutdownClock.Elapsed.TotalSeconds < drain) { return; }
                _shutdownStage = 2; _shutdownFrames = 0; return;
            }
            if (++_shutdownFrames < 3) { return; }
            CompleteShutdown();
        }
        catch (Exception error) { _shutdownFailure ??= error; Console.Error.WriteLine("SOAK=FAIL during cleanup " + error); CompleteShutdown(); }
    }
    private void CompleteShutdown()
    {
        SetProcess(false); Exception? failure = _shutdownFailure;
        try
        {
            if (_options is not null && _clock.IsRunning)
            {
                Sample(); SoakProgress progress = Progress();
                bool native = DisplayServer.GetName() != "headless", acceptance = failure is null && SoakPolicy.Acceptance(_options, progress, native);
                bool diagnostic = _options.Fast || _options.Seconds < 7200;
                if (failure is null && !diagnostic && !acceptance) { failure = new InvalidDataException("Wall-clock duration, active ticks, native rendering or required flow coverage did not satisfy the soak gate."); }
                WriteNew("result.json", new { passed = failure is null, soakAcceptance = acceptance, diagnostic, progress, peakWorkingSetBytes = _peakWorkingSet, peakManagedBytes = _peakManaged, pausesResumed = _resumes, loadingFocusChecks = _loadingFocusChecks, loadingFocusBeforeReady = _loadingFocusBeforeReady, error = failure?.ToString(), physicalMobileAcceptance = false, reviewedGolden = false, audioListeningAcceptance = false });
                GD.Print($"{(acceptance ? "SOAK" : "SOAK_DIAGNOSTIC")}={(failure is null ? "PASS" : "FAIL")} seconds={progress.ElapsedSeconds:F2} ticks={_ticks} cycles={_cycles}");
            }
            else if (_ownsOutput && failure is not null) { WriteNew("result.json", new { passed = false, soakAcceptance = false, error = failure.ToString() }); }
        }
        catch (Exception error) { Console.Error.WriteLine("SOAK=FAIL during cleanup " + error); failure = error; }
        finally { _samples?.Dispose(); GetTree().Quit(failure is null ? 0 : 1); }
    }
    public override void _ExitTree() { Engine.MaxFps = _previousMaxFps; OS.LowProcessorUsageMode = _previousLowCpu; _samples?.Dispose(); }
    private void WriteNew<T>(string name, T value) { using Stream output = new FileStream(Path.Combine(_options!.OutputDirectory, name), FileMode.CreateNew); JsonSerializer.Serialize(output, value, _json); }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
}
