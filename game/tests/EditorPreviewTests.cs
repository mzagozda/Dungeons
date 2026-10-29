using System.Diagnostics;
using System.Text.Json.Nodes;
using Dungeons.Core.Level;
using Dungeons.Core.Level.Commands;
using Dungeons.Core.Math;
using Dungeons.Core.Sim;
using Dungeons.Game.Rendering;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace Dungeons.Game.Tests;

[TestSuite, RequireGodotRuntime]
public sealed class EditorPreviewTests
{
    [TestCase, GodotExceptionMonitor]
    public void EveryAuthoredPaletteIconLoadsThroughActualEngineResourcesAt32Pixels()
    {
        var content = ContentBridge.LoadRegistry();
        foreach (var definition in content.Tiles.Values.Select(tile => tile.Data).Concat(content.Entities.Values.Select(entity => entity.Data)))
        {
            AssertThat(definition.EditorChar.HasValue).IsTrue();
            Texture2D texture = GD.Load<Texture2D>("res://" + definition.EditorIcon) ?? throw new InvalidDataException("Missing palette icon: " + definition.EditorIcon);
            AssertThat(texture.GetWidth()).IsEqual(32); AssertThat(texture.GetHeight()).IsEqual(32);
        }
    }

    [TestCase, GodotExceptionMonitor]
    public async Task ThumbnailCaptureUsesAnIsolatedWorldAndLeavesGameplayAndViewportUnchanged()
    {
        var content = ContentBridge.LoadRegistry(); LevelDocument level = ContentBridge.LoadBuiltins(content)[0];
        if (DisplayServer.GetName() == "headless")
        {
            bool rejected = false; try { await WorldView.CapturePreview(level); } catch (NotSupportedException) { rejected = true; }
            AssertThat(rejected).IsTrue(); return;
        }
        var session = new GameSession(level, 42); var view = new WorldView();
        using ISceneRunner runner = ISceneRunner.Load(view, autoFree: true); view.Build(level, content, session); await Ready(runner, view);
        view.ApplyResolution(.75f);
        Window root = view.GetTree().Root; Camera3D camera = view.Camera;
        Transform3D transform = camera.GlobalTransform; float scale = view.GetViewport().Scaling3DScale;
        ulong hash = session.Hash(); string source = level.SourceHash; int children = root.GetChildCount();
        foreach ((int width, int height) in new[] { (320, 180), (180, 320) })
        {
            bool paused = view.GetTree().Paused; view.GetTree().Paused = width == 180; byte[] png;
            try { png = await WorldView.CapturePreview(level, width, height); AssertThat(view.GetTree().Paused).IsEqual(width == 180); }
            finally { view.GetTree().Paused = paused; }
            using var image = new Image(); AssertThat(image.LoadPngFromBuffer(png)).IsEqual(Error.Ok);
            AssertThat(image.GetWidth()).IsEqual(width); AssertThat(image.GetHeight()).IsEqual(height);
            var colors = new HashSet<Color>();
            for (int y = 0; y < height; y += 3) { for (int x = 0; x < width; x += 3) { colors.Add(image.GetPixel(x, y)); } }
            AssertThat(colors.Count).IsGreater(32);
            AssertThat(root.GetChildCount()).IsEqual(children); AssertThat(view.GetViewport().GetCamera3D()).IsEqual(camera);
            AssertThat(camera.GlobalTransform).IsEqual(transform); AssertThat(view.GetViewport().Scaling3DScale).IsEqual(scale);
            AssertThat(session.Hash()).IsEqual(hash); AssertThat(session.Tick).IsEqual(0); AssertThat(level.SourceHash).IsEqual(source);
            GD.Print($"M9_THUMBNAIL_TEST width={width} height={height} colors={colors.Count} bytes={png.Length} renderer={RenderingServer.GetCurrentRenderingMethod()}");
        }
    }

    [TestCase, GodotExceptionMonitor]
    public async Task ThumbnailRejectsInvalidDimensionsAndCleansUpRejectedDocuments()
    {
        LevelDocument level = ContentBridge.LoadBuiltins(ContentBridge.LoadRegistry())[0];
        bool rejected = false; try { await WorldView.CapturePreview(level, 0, 180); } catch (ArgumentOutOfRangeException) { rejected = true; }
        AssertThat(rejected).IsTrue();
        if (DisplayServer.GetName() == "headless") { return; }
        var node = new Node(); using ISceneRunner runner = ISceneRunner.Load(node, autoFree: true); Window root = node.GetTree().Root;
        new RemoveEntity(level.Entities.First(placement => placement.Definition.Id == "core:player_spawn").Id!).Apply(level);
        AssertThat(level.HasErrors).IsTrue(); int children = root.GetChildCount(); rejected = false;
        try { await WorldView.CapturePreview(level); } catch (ArgumentException) { rejected = true; }
        AssertThat(rejected).IsTrue(); AssertThat(root.GetChildCount()).IsEqual(children);
    }

    [TestCase, GodotExceptionMonitor]
    public async Task AuthoredPreviewHasIdleActorsLightsAndCameraWithoutSimulationOrPools()
    {
        GD.Print($"M9_PREVIEW_BACKEND display={DisplayServer.GetName()} renderer={RenderingServer.GetCurrentRenderingMethod()}");
        var content = ContentBridge.LoadRegistry(); LevelDocument level = ContentBridge.LoadBuiltins(content)[0]; string source = level.SourceHash;
        var view = new WorldView(); using ISceneRunner runner = ISceneRunner.Load(view, autoFree: true); view.Build(level); await Ready(runner, view);
        AssertThat(view.IsPreview).IsTrue(); AssertThat(view.CanMeasureGraphics).IsFalse(); AssertThat(view.ChunkCount).IsEqual(6); AssertThat(view.WaterMeshCount).IsGreater(0);
        AssertThat(view.GetNodeOrNull("CombatOverlay") is null).IsTrue(); AssertThat(view.GetChildren().Any(node => node.Name.ToString().StartsWith("Prewarmed_", StringComparison.Ordinal))).IsFalse();
        var sessionField = typeof(WorldView).GetField("_session", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!; AssertThat(sessionField.GetValue(view) is null).IsTrue();
        EntityPlacement spawn = level.Entities.First(placement => placement.Definition.Id == "core:player_spawn"); Node3D hero = view.PreviewEntity(spawn.Id!)!;
        AssertThat(hero.Position).IsEqual(new Vector3(spawn.Position.X, level.BakeGrid().HeightAt(spawn.Position), spawn.Position.Z));
        foreach (AnimationTree tree in Descendants(view).OfType<AnimationTree>()) { AssertThat(tree.Active).IsFalse(); }
        AnimationPlayer idle = Descendants(hero).OfType<AnimationPlayer>().Single(); double position = idle.CurrentAnimationPosition;
        await runner.SimulateFrames(8); view.Interpolate(.5f); AssertThat(idle.IsPlaying()).IsFalse(); AssertThat(idle.CurrentAnimationPosition).IsEqual(position);
        AssertThat(level.SourceHash).IsEqual(source); AssertThat(view.EnabledLightCount).IsLessEqual(8);
        view.FocusPreview(new(16.5f, 0, 34.5f)); AssertThat(view.Camera.Position.IsFinite()).IsTrue();
        bool rejected = false; try { view.ConsumeEvents(ReadOnlySpan<SimEvent>.Empty); } catch (InvalidOperationException) { rejected = true; } AssertThat(rejected).IsTrue();
    }

    [TestCase, GodotExceptionMonitor]
    public async Task RegionalRemeshReplacesOnlyAffectedNodesAndCoalescesSupersededWork()
    {
        var content = ContentBridge.LoadRegistry(); LevelDocument level = ContentBridge.LoadBuiltins(content)[0]; var view = new WorldView();
        using ISceneRunner runner = ISceneRunner.Load(view, autoFree: true); view.Build(level); await Ready(runner, view);
        var before = ChunkIds(view); long revision = view.ChunkRevision(0, 2);
        new SetHeight(10, 35, 1).Apply(level); ChunkRect affected = ChunkRect.FromCells(new(10, 35, 1, 1), level.Width, level.Height);
        var timer = Stopwatch.StartNew(); view.ApplyChange(affected); timer.Stop(); double scheduleMs = timer.Elapsed.TotalMilliseconds;
        new SetHeight(10, 35, 2).Apply(level); view.ApplyChange(affected);
        AssertThat(view.PendingChunkCount).IsEqual(1); AssertThat(view.ReadyForPlay).IsFalse(); await Ready(runner, view);
        foreach (var pair in before) { bool replaced = view.GetNode<Node3D>(pair.Key).GetInstanceId() != pair.Value; AssertThat(replaced).IsEqual(pair.Key == "Chunk_0_2"); }
        AssertThat(view.ChunkRevision(0, 2)).IsEqual(revision + 2); AssertThat(view.DiscardedChunkBuilds).IsGreaterEqual(1);
        AssertThat(view.ChunkCount).IsEqual(6); AssertThat(view.PendingChunkCount).IsEqual(0);
        var timings = view.ChunkTimings(0, 2); GD.Print($"M9_CHUNK scheduleMs={scheduleMs:F3} buildMs={timings.Build:F3} commitMs={timings.Commit:F3}");
        AssertThat(scheduleMs).IsLess(16); AssertThat(timings.Build).IsLess(16); AssertThat(timings.Commit).IsLess(16);
        var mesh = (ArrayMesh)view.GetNode<MeshInstance3D>("Chunk_0_2/Terrain").Mesh;
        using var arrays = mesh.SurfaceGetArrays(0); AssertThat(arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array().Any(point => point.X >= 10 && point.X <= 11 && point.Z >= 35 && point.Z <= 36 && point.Y == 2)).IsTrue();
        var stable = ChunkIds(view); view.ApplyChange(new(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue)); AssertThat(view.ReadyForPlay).IsTrue(); AssertThat(ChunkIds(view).SequenceEqual(stable)).IsTrue();
    }

    [TestCase, GodotExceptionMonitor]
    public async Task EditsBeforeInitialCommitAndPreviewPlacementChangesUseTheNewestAuthoredState()
    {
        var content = ContentBridge.LoadRegistry(); LevelDocument level = ContentBridge.LoadBuiltins(content)[0]; var view = new WorldView();
        using ISceneRunner runner = ISceneRunner.Load(view, autoFree: true); view.Build(level);
        new SetHeight(10, 35, 1).Apply(level); view.ApplyChange(ChunkRect.FromCells(new(10, 35, 1, 1), level.Width, level.Height)); await Ready(runner, view);
        string id = level.Entities.First(entity => entity.Definition.Kind == EntityKind.Enemy).Id!; Node3D actor = view.PreviewEntity(id)!; ulong actorId = actor.GetInstanceId();
        new MoveEntity(id, new Vec2(10.5f, 35.5f)).Apply(level);
        new AddEntity(new JsonObject { ["id"] = "preview_light", ["type"] = "core:light", ["x"] = 11.5, ["z"] = 35.5 }).Apply(level);
        view.ApplyChange(ChunkRect.FromCells(new(10, 35, 2, 1), level.Width, level.Height)); await Ready(runner, view);
        AssertThat(view.PreviewEntity(id)!.GetInstanceId()).IsEqual(actorId); AssertThat(actor.Position).IsEqual(new Vector3(10.5f, 1, 35.5f));
        Node3D light = view.PreviewEntity("preview_light")!; AssertThat(light is not null).IsTrue(); AssertThat(view.GetChildren().OfType<OmniLight3D>().Count()).IsGreater(0);
        new RemoveEntity(id).Apply(level); new RemoveEntity("preview_light").Apply(level); view.ApplyChange(new(0, 0, 2, 3)); await Ready(runner, view);
        AssertThat(view.PreviewEntity(id) is null).IsTrue(); AssertThat(view.PreviewEntity("preview_light") is null).IsTrue(); AssertThat(GodotObject.IsInstanceValid(actor)).IsFalse();
    }

    [TestCase, GodotExceptionMonitor]
    public async Task RebuildingTheSameViewCanResizeAndSwitchBetweenPreviewAndGameplay()
    {
        var content = ContentBridge.LoadRegistry(); LevelDocument level = ContentBridge.LoadBuiltins(content)[0]; var view = new WorldView();
        using ISceneRunner runner = ISceneRunner.Load(view, autoFree: true); view.Build(level); await Ready(runner, view);
        var session = new GameSession(level, 1); view.Build(level, content, session); await Ready(runner, view); AssertThat(view.IsPreview).IsFalse(); AssertThat(view.CombatOverlay is not null).IsTrue();
        bool rejected = false; try { view.FocusPreview(Vector3.Zero); } catch (InvalidOperationException) { rejected = true; } AssertThat(rejected).IsTrue();
        new SetMetadata("name", "Changed source").Apply(level); rejected = false; try { view.ApplyChange(new(0, 0, 1, 1)); } catch (InvalidOperationException) { rejected = true; } AssertThat(rejected).IsTrue();
        view.Build(level); await Ready(runner, view); AssertThat(view.IsPreview).IsTrue(); AssertThat(view.GetNodeOrNull("CombatOverlay") is null).IsTrue();
        ulong cameraId = view.Camera.GetInstanceId(), chunkId = view.GetNode<Node3D>("Chunk_0_0").GetInstanceId();
        new SetEnvironment(new JsonObject { ["camera"] = new JsonObject { ["fovDeg"] = 40 }, ["sun"] = new JsonObject { ["energy"] = .5 } }).Apply(level);
        view.ApplyChange(default); AssertThat(view.Camera.Fov).IsEqual(40f); AssertThat(view.Camera.GetInstanceId()).IsEqual(cameraId);
        AssertThat(view.GetChildren().OfType<DirectionalLight3D>().Single().LightEnergy).IsEqual(.5f); AssertThat(view.GetNode<Node3D>("Chunk_0_0").GetInstanceId()).IsEqual(chunkId);
        new Resize(33, 49).Apply(level); rejected = false; try { view.ApplyChange(new(0, 0, 1, 1)); } catch (InvalidOperationException) { rejected = true; } AssertThat(rejected).IsTrue();
        view.Build(level); await Ready(runner, view); AssertThat(view.ChunkCount).IsEqual(12); AssertThat(view.GetChildren().OfType<Camera3D>().Count()).IsEqual(1); AssertThat(view.GetChildren().OfType<WorldEnvironment>().Count()).IsEqual(1);
        ulong current = view.GetNode<Node3D>("Chunk_0_0").GetInstanceId(); new SetTile(2, 2, "core:missing").Apply(level);
        rejected = false; try { view.Build(level); } catch (ArgumentException) { rejected = true; } AssertThat(rejected).IsTrue(); AssertThat(view.GetNode<Node3D>("Chunk_0_0").GetInstanceId()).IsEqual(current);
    }
    private static Dictionary<string, ulong> ChunkIds(WorldView view) => view.GetChildren().OfType<Node3D>().Where(node => node.Name.ToString().StartsWith("Chunk_", StringComparison.Ordinal)).ToDictionary(node => node.Name.ToString(), node => node.GetInstanceId());
    private static IEnumerable<Node> Descendants(Node root) { foreach (Node child in root.GetChildren()) { yield return child; foreach (Node nested in Descendants(child)) { yield return nested; } } }
    private static async Task Ready(ISceneRunner runner, WorldView view)
    { for (int frame = 0; frame < 240 && !view.ReadyForPlay; frame++) { await runner.AwaitIdleFrame(); } AssertThat(view.ReadyForPlay).IsTrue(); await runner.SimulateFrames(2); }
}
