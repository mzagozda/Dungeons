using System.Diagnostics;
using System.Text.Json.Nodes;
using Dungeons.Core.Level;
using Dungeons.Core.Level.Commands;
using Dungeons.Core.Sim;
using Dungeons.Game.Editor;
using Dungeons.Game.Rendering;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace Dungeons.Game.Tests;

[TestSuite, RequireGodotRuntime]
public sealed class LevelReloadTests
{
    [TestCase, GodotExceptionMonitor]
    public async Task ReloadedWorldRebindsActorsWhileRetainingUnaffectedChunkNodes()
    {
        var content = ContentBridge.LoadRegistry(); LevelDocument before = ContentBridge.LoadBuiltins(content)[0];
        var session = new GameSession(before, 42); var view = new WorldView(); using ISceneRunner runner = ISceneRunner.Load(view, autoFree: true);
        view.Build(before, content, session); await Until(runner, () => view.ReadyForPlay);
        var roots = Chunks(view); Camera3D camera = view.Camera; Node3D hero = view.GetNode<Node3D>("Entity_" + session.World.Players[0].Id);
        LevelDocument after = LevelLoader.Load(before.ToJson(), content).Document!; new SetHeight(10, 35, 1).Apply(after);
        session.ReloadLevel(after); view.Reload(after, session, LevelDocumentDiff.ChangedChunks(before, after)); await Until(runner, () => view.ReadyForPlay);
        AssertThat(view.Camera).IsEqual(camera); AssertThat(GodotObject.IsInstanceValid(hero)).IsFalse();
        foreach (var root in roots) { AssertThat(view.GetNode(root.Key).GetInstanceId() != root.Value).IsEqual(root.Key == "Chunk_0_2"); }
        AssertThat(view.GetNode<Node3D>("Entity_" + session.World.Players[0].Id).Position.Y).IsEqual(session.World.Players[0].Y);
        bool rejected = false; var unchanged = Chunks(view);
        try { view.Reload(before, session, default); } catch (ArgumentException) { rejected = true; }
        AssertThat(rejected).IsTrue(); AssertThat(Chunks(view).SequenceEqual(unchanged)).IsTrue();
    }

    [TestCase, GodotExceptionMonitor]
    public async Task ActualDiskEditsRejectPartialFilesReloadWithADiffAndHandleResize()
    {
        string directory = Path.Combine(Path.GetTempPath(), "dungeons-reload-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "level.json");
        try
        {
            var content = ContentBridge.LoadRegistry(); LevelDocument original = ContentBridge.LoadBuiltins(content)[0]; System.IO.File.WriteAllText(path, original.ToJson());
            var tool = new LevelReloadTool { LevelPath = path }; using ISceneRunner runner = ISceneRunner.Load(tool, autoFree: true);
            await Until(runner, () => tool.View is not null && tool.View.ReadyForPlay); tool.Session.Pause();
            World old = tool.Session.World; ulong hash = tool.Session.Hash(); var roots = Chunks(tool.View);
            System.IO.File.WriteAllText(path, "{"); await Until(runner, () => tool.RejectedChanges > 0);
            AssertThat(tool.Session.World).IsEqual(old); AssertThat(tool.Session.Hash()).IsEqual(hash); AssertThat(tool.LastError.Length).IsGreater(0);
            LevelDocument changed = LevelLoader.Load(original.ToJson(), content).Document!; new SetHeight(10, 35, 1).Apply(changed);
            new AddTrigger(JsonNode.Parse("""{"id":"disk_reload","event":{"type":"level_start"},"actions":[{"type":"message","text":"Disk reload"}]}""")!.AsObject()).Apply(changed);
            string source = LevelSerializer.Write(changed); string expectedHash = LevelLoader.Load(source, content).Document!.SourceHash;
            var timer = Stopwatch.StartNew(); System.IO.File.WriteAllText(path, source);
            await Until(runner, () => tool.Session.ReloadRevision == 1 && tool.View.ReadyForPlay && tool.LastReloadMilliseconds > 0); timer.Stop();
            AssertThat(tool.Session.State).IsEqual(SessionState.Paused); AssertThat(tool.Session.World.Mission.Message).IsEqual("Disk reload");
            AssertThat(tool.Document.SourceHash).IsEqual(expectedHash); AssertThat(tool.LastError).IsEqual(""); AssertThat(tool.Session.Recorder is null).IsTrue();
            foreach (var root in roots) { AssertThat(tool.View.GetNode(root.Key).GetInstanceId() != root.Value).IsEqual(root.Key == "Chunk_0_2"); }
            GD.Print($"M9_HOT_RELOAD_TEST wallMs={timer.Elapsed.TotalMilliseconds:F3} applyMs={tool.LastReloadMilliseconds:F3} renderer={RenderingServer.GetCurrentRenderingMethod()}");
            if (DisplayServer.GetName() != "headless") { AssertThat(timer.Elapsed.TotalMilliseconds).IsLess(500); }
            World retained = tool.Session.World; System.IO.File.WriteAllText(path, source); await runner.SimulateFrames(12);
            AssertThat(tool.Session.World).IsEqual(retained); AssertThat(tool.Session.ReloadRevision).IsEqual(1L);
            new Resize(changed.Width + 16, changed.Height).Apply(changed); Camera3D camera = tool.View.Camera;
            System.IO.File.WriteAllText(path, LevelSerializer.Write(changed)); await Until(runner, () => tool.Session.ReloadRevision == 2 && tool.View.ReadyForPlay);
            AssertThat(GodotObject.IsInstanceValid(camera)).IsFalse(); AssertThat(tool.View.ChunkCount).IsEqual(9);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static Dictionary<string, ulong> Chunks(WorldView view) => view.GetChildren().OfType<Node3D>()
        .Where(node => node.Name.ToString().StartsWith("Chunk_", StringComparison.Ordinal)).ToDictionary(node => node.Name.ToString(), node => node.GetInstanceId());
    private static async Task Until(ISceneRunner runner, Func<bool> predicate)
    { for (int i = 0; i < 180 && !predicate(); i++) { await runner.SimulateFrames(1); } AssertThat(predicate()).IsTrue(); }
}
