using System.Text.Json.Nodes;
using Dungeons.Core.Content;
using Dungeons.Core.Level;
using Dungeons.Core.Sim;
using Godot;

namespace Dungeons.Game.Rendering;

/// <summary>Real mission/death UI captures; full completion uses the recorded gameplay candidate.</summary>
public partial class M6Preview : Node3D
{
    private GameplayScreen _game = null!;
    private string _mode = "", _path = "", _replayPath = "";
    private int _warmup, _captureFrames;
    private bool _capture;
    public override void _Ready()
    {
        try
        {
            foreach (string arg in ScreenshotRun.Arguments)
            {
                if (arg.StartsWith("--m6-mode=", StringComparison.Ordinal)) { _mode = arg[10..]; }
                if (arg.StartsWith("--m6-screenshot=", StringComparison.Ordinal)) { _path = arg[16..]; }
                if (arg.StartsWith("--m6-replay=", StringComparison.Ordinal)) { _replayPath = arg[12..]; }
            }
            if (_mode is not ("complete" or "failed" or "death" or "objects") || _path.Length == 0) { throw new ArgumentException("M6 capture requires a mode and screenshot path."); }
            var content = ContentBridge.LoadRegistry(); LevelDocument level = ContentBridge.LoadBuiltins(content)[0];
            if (_mode == "objects") { level = ObjectFixture(content); }
            else if (_mode != "complete")
            {
                JsonObject source = level.ToJsonObject();
                if (_mode == "failed")
                { source["objectives"] = new JsonArray(); source["completion"]!["requires"] = new JsonArray(); source["triggers"] = JsonNode.Parse("""[{"id":"fail","event":{"type":"timer","seconds":0.2},"actions":[{"type":"fail_level"}]}]"""); }
                else
                { JsonNode enemy = source["entities"]!.AsArray().Single(entity => entity!["id"]!.GetValue<string>() == "z_bank_1")!; enemy["x"] = 5.5; enemy["z"] = 36.5; }
                LevelLoadResult loaded = LevelLoader.Load(source.ToJsonString(), content); level = loaded.Document ?? throw new InvalidOperationException(string.Join('\n', loaded.Diagnostics));
            }
            _game = new CaptureGameplayScreen(); AddChild(_game);
            if (_mode == "complete" && ScreenshotRun.Replay is { } recorded) { _game.BuildReplay(level, content, recorded); }
            else { _game.Build(level, content, 1); }
            _game.SetProcess(false);
        }
        catch (Exception error) { Fail(error); }
    }
    public override void _Process(double delta)
    {
        try
        {
            if (!_game.View.ReadyForPlay) { return; }
            if (_warmup++ == 0) { _game._Process(0); }
            if (_warmup < 12) { _game.View.Interpolate(1); return; }
            if (_capture)
            {
                // Scene transforms and RenderingServer batches need rendered frames after
                // fast-forwarding the simulation; the GUI can update a frame earlier.
                if (_captureFrames++ < 3) { return; }
                using Image image = GetViewport().GetTexture().GetImage(); Error saved = ScreenshotRun.Save(image, _path);
                int hidden = _mode == "objects" ? HiddenBatchCount(_game.View) : 0;
                bool passed = saved == Error.Ok && (_mode == "objects" ? hidden == 2 && _game.View.EnabledLightCount == 1 : _mode == "death" ? _game.DeathVisible : _game.ResultsVisible);
                ScreenshotRun.Complete(passed, image, _game.Session);
                if (_mode == "objects") { GD.Print($"M6_OBJECTS hiddenBatches={hidden} lights={_game.View.EnabledLightCount}"); }
                GD.Print($"M6_VIEW hero={_game.Session.World.Players[0].Pos} camera={_game.View.Camera.Position}");
                GD.Print($"M6_PREVIEW={(passed ? "PASS" : "FAIL")} mode={_mode} state={_game.Session.State} tick={_game.Session.Tick} kills={_game.Session.World.Kills} deaths={_game.Session.World.Deaths}");
                CaptureShutdown.Begin(this, passed ? 0 : 1); return;
            }
            if (_mode == "complete")
            {
                ReplaySnapshot replay;
                if (ScreenshotRun.Replay is { } recorded) { replay = recorded.Snapshot; }
                else { ReplayLoadResult read = ReplayJson.Read(System.IO.File.ReadAllText(_replayPath)); replay = read.Document?.Snapshot ?? throw new InvalidDataException(read.Error); }
                ReplayVerification verified = ReplayVerifier.Verify(_game.Session, replay); if (!verified.Success || _game.Session.State != SessionState.Won) { throw new InvalidOperationException(verified.Message); }
            }
            else if (_mode == "objects")
            { for (int tick = 0; tick < 6; tick++) { _game.Session.Step(default(PlayerInput)); } }
            else
            {
                for (int tick = 0; tick < 2400 && _game.Session.State == SessionState.Running && (_mode != "death" || _game.Session.World.Players[0].Alive); tick++)
                { _game.Session.Step(default(PlayerInput)); _game.Session.DrainEvents(); }
            }
            _game.View.ConsumeEvents(_game.Session.DrainEvents()); _game.View.Interpolate(1); _game.View._Process(1); _game.View._Process(1); _game.Hud._Process(0); _game._Process(0); _capture = true;
        }
        catch (Exception error) { Fail(error); }
    }
    private void Fail(Exception error) { GD.PushError(error.ToString()); SetProcess(false); GetTree().Quit(1); }
    internal static int HiddenBatchCount(WorldView view) => view.GetChildren().OfType<MultiMeshInstance3D>().SelectMany(n => Enumerable.Range(0, n.Multimesh.InstanceCount).Select(i => n.Multimesh.GetInstanceTransform(i))).Count(t => MathF.Abs(t.Basis.Determinant()) < .00001f);
    internal static LevelDocument ObjectFixture(ContentRegistry content)
    {
        JsonObject source = ContentBridge.LoadBuiltins(content)[0].ToJsonObject(); var kept = new JsonArray();
        foreach (JsonNode? node in source["entities"]!.AsArray())
        {
            string type = node!["type"]!.GetValue<string>();
            if (type is "core:player_spawn" or "core:exit" or "core:checkpoint" || node["id"]!.GetValue<string>() is "sm_1" or "torch_bridge" or "prop_crate_1") { kept.Add(node.DeepClone()); }
        }
        JsonNode marker = kept.Single(e => e!["id"]!.GetValue<string>() == "sm_1")!; marker["x"] = 8.5; marker["z"] = 36.5;
        source["entities"] = kept; source["objectives"] = new JsonArray(); source["completion"]!["requires"] = new JsonArray();
        source["triggers"] = JsonNode.Parse("""[{"id":"replace","event":{"type":"timer","seconds":0.1},"actions":[{"type":"remove_entity","id":"torch_bridge"},{"type":"remove_entity","id":"prop_crate_1"},{"type":"spawn","at":"sm_1","entity":{"type":"core:torch"}},{"type":"spawn","at":"sm_1","entity":{"type":"core:prop_crate"}},{"type":"spawn","at":"sm_1","entity":{"type":"core:gate","props":{"state":"open","width":1}}}]}]""");
        LevelLoadResult loaded = LevelLoader.Load(source.ToJsonString(), content); return loaded.Document ?? throw new InvalidOperationException(string.Join('\n', loaded.Diagnostics));
    }
}
