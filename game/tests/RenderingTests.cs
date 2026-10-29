using Dungeons.Core.Level;
using Dungeons.Core.Sim;
using Dungeons.Game.Rendering;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace Dungeons.Game.Tests;

/// <summary>Real engine scene/resource integration, separate from CPU mesher tests.</summary>
[TestSuite, RequireGodotRuntime]
public sealed class RenderingTests
{
    [TestCase, GodotExceptionMonitor]
    public async Task RenderCadenceInterpolationTest()
    {
        var content = ContentBridge.LoadRegistry(); LevelDocument level = ContentBridge.LoadBuiltins(content)[0];
        foreach (int hz in new[] { 60, 120, 144 })
        {
            var session = new GameSession(level, 1); var view = new WorldView(); using ISceneRunner runner = ISceneRunner.Load(view, autoFree: true);
            view.Build(level, content, session); await Ready(runner, view); view.SetProcess(false);
            Node3D hero = view.GetNode<Node3D>("Entity_" + session.World.Players[0].Id);
            Vector3 previousHero = hero.Position, previousCamera = view.Camera.Position; double accumulator = 0; float dt = 1f / hz;
            for (int frame = 0; frame < hz; frame++)
            {
                accumulator += dt;
                while (accumulator >= GameConstants.Dt) { session.Step(new PlayerInput(Dungeons.Core.Math.Vec2.North)); accumulator -= GameConstants.Dt; }
                view.Interpolate((float)(accumulator / GameConstants.Dt)); view._Process(dt);
                if (frame >= 5)
                {
                    float expected = session.World.Players[0].Stats.Speed * dt;
                    AssertThat(MathF.Abs(hero.Position.DistanceTo(previousHero) - expected) < 0.00003f).IsTrue();
                    AssertThat(view.Camera.Position.DistanceTo(previousCamera) <= expected + 0.00003f).IsTrue();
                }
                previousHero = hero.Position; previousCamera = view.Camera.Position;
            }
        }
    }
    [TestCase, GodotExceptionMonitor]
    public async Task AuthoredEnvironmentBindingTest()
    {
        var content = ContentBridge.LoadRegistry(); var source = ContentBridge.LoadBuiltins(content)[0].ToJsonObject();
        source["environment"]!["sun"]!["energy"] = 0.25f; source["environment"]!["sun"]!["color"] = "#ff5a3c";
        source["environment"]!["fog"]!["color"] = "#76b8bb";
        source["environment"]!["camera"]!["bounds"] = System.Text.Json.Nodes.JsonNode.Parse("""{"x":10,"z":10,"w":4,"h":4}""");
        LevelLoadResult loaded = LevelLoader.Load(source.ToJsonString(), content); AssertThat(loaded.Success).IsTrue();
        var view = new WorldView(); using ISceneRunner runner = ISceneRunner.Load(view, autoFree: true); view.Build(loaded.Document!, content, new(loaded.Document!, 1)); await Ready(runner, view);
        DirectionalLight3D sun = view.GetNode<DirectionalLight3D>("Sun"); AssertThat(sun.LightEnergy).IsEqual(0.25f); AssertThat(sun.LightColor).IsEqual(new Color("#ff5a3c"));
        WorldEnvironment environment = view.GetChildren().OfType<WorldEnvironment>().Single(); AssertThat(environment.Environment.FogLightColor).IsEqual(new Color("#76b8bb"));
        Vector3 focus = view.Camera.Position - view.Camera.GlobalBasis.Z * loaded.Document!.Environment.CameraDistance;
        AssertThat(focus.DistanceTo(new(10, 0, 14)) < 0.001f).IsTrue();
    }
    [TestCase, GodotExceptionMonitor]
    public async Task SceneWiringTest()
    {
        var content = ContentBridge.LoadRegistry(); LevelDocument level = ContentBridge.LoadBuiltins(content)[0];
        var game = new GameplayScreen(); using ISceneRunner runner = ISceneRunner.Load(game, autoFree: true); game.Build(level, content, 1);
        await runner.SimulateFrames(120, 17);
        AssertThat(game.View.ChunkCount).IsEqual(6); AssertThat(game.View.WaterMeshCount).IsGreater(0);
        AssertThat(game.View.HasNode("Entity_" + game.Session.World.Players[0].Id)).IsTrue();
        AssertThat(game.Hud.HealthText).IsEqual("100/100"); AssertThat(game.Hud.ObjectiveText).IsEqual("Cross the river");
        AssertThat(game.Hud.ObjectiveText).IsEqual(game.Session.World.Mission.PrimaryText);
        AssertThat(game.Session.World.Seed).IsEqual(1UL);
        runner.SimulateKeyPressed(Key.F3).SimulateKeyPressed(Key.F4).SimulateKeyPressed(Key.F5).SimulateKeyPressed(Key.F6).SimulateKeyPressed(Key.F7);
        await runner.SimulateFrames(8);
        Node3D debug = game.GetNode<Node3D>("DebugGeometry"); AssertThat(debug.Visible).IsTrue();
        AssertThat(debug.GetChildren().OfType<MeshInstance3D>().Single().Mesh.GetSurfaceCount()).IsEqual(1);
    }
    [TestCase, GodotExceptionMonitor]
    public void LevelCatalogTest()
    {
        var content = ContentBridge.LoadRegistry(); LevelDocument level = ContentBridge.LoadBuiltins(content)[0];
        string directory = ProjectSettings.GlobalizePath("user://catalog-test-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(directory);
        try
        {
            var source = level.ToJsonObject(); source["id"] = "test.catalog_copy"; source["name"] = "User copy";
            PackageLoadResult package = LevelPackage.Create(new[] { ("level.json", (ReadOnlyMemory<byte>)System.Text.Encoding.UTF8.GetBytes(source.ToJsonString())) });
            using (var zip = System.IO.File.Create(System.IO.Path.Combine(directory, "copy.dlvl"))) { package.Package!.Write(zip); }
            System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "broken.dlvl"), "invalid zip");
            var catalog = CatalogBridge.Scan(content, directory);
            AssertThat(catalog.Any(e => e.Id == level.Id && e.IsPlayable && e.Source == LevelSource.BuiltIn)).IsTrue();
            AssertThat(catalog.Any(e => e.Id == "test.catalog_copy" && e.IsPlayable && e.Source == LevelSource.User)).IsTrue();
            AssertThat(catalog.Any(e => !e.IsPlayable && e.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))).IsTrue();
        }
        finally
        {
            // Delete only our two exact temporary files, then the now-empty test directory.
            System.IO.File.Delete(System.IO.Path.Combine(directory, "copy.dlvl")); System.IO.File.Delete(System.IO.Path.Combine(directory, "broken.dlvl")); System.IO.Directory.Delete(directory);
        }
    }
    [TestCase, GodotExceptionMonitor]
    public async Task RenderSceneFoundationTest()
    {
        var content = ContentBridge.LoadRegistry(); LevelDocument level = ContentBridge.LoadBuiltins(content)[0]; var session = new GameSession(level, 1);
        var view = new WorldView(); using ISceneRunner runner = ISceneRunner.Load(view, autoFree: true); view.Build(level, content, session);
        await Ready(runner, view);
        AssertThat(view.ChunkCount).IsEqual(6); AssertThat(view.WaterMeshCount).IsGreater(0);
        AssertThat(session.World.Players.Count).IsEqual(1);
        AssertThat(view.HasNode("Entity_" + session.World.Players[0].Id)).IsTrue();
        AssertThat(session.World.Players[0].Health.Current).IsEqual(100);
        AssertThat(MaterialAudit.Check(view)).IsGreater(10);
    }

    [TestCase, GodotExceptionMonitor]
    public async Task EditorStartOverrideFeedsTheRealViewWithoutMutatingItsDocument()
    {
        var content = ContentBridge.LoadRegistry(); LevelDocument level = ContentBridge.LoadBuiltins(content)[0]; string before = level.SourceHash;
        var origin = new StartOverride(new(16.5f, 34.5f)); var session = GameSession.Create(level, content, 5, record: true, replayCapacity: 1, startOverride: origin);
        var view = new WorldView(); using ISceneRunner runner = ISceneRunner.Load(view, autoFree: true); view.Build(level, content, session); await Ready(runner, view); view.Interpolate(1);
        Entity hero = session.World.Players[0]; Node3D actor = view.GetNode<Node3D>("Entity_" + hero.Id);
        AssertThat(actor.Position).IsEqual(new Vector3(origin.Pos.X, hero.Y, origin.Pos.Z)); AssertThat(session.World.CameraFocus).IsEqual(origin.Pos); AssertThat(level.SourceHash).IsEqual(before);
        ReplaySnapshot replay = session.Recorder!.Capture(); AssertThat(ReplayVerifier.Verify(GameSession.Create(level, content, 5, startOverride: replay.StartOverride), replay).Success).IsTrue();
    }

    [TestCase, GodotExceptionMonitor]
    public async Task AxisSanityTest()
    {
        var content = ContentBridge.LoadRegistry(); LevelDocument level = ContentBridge.LoadBuiltins(content)[0]; var session = new GameSession(level, 1);
        var view = new WorldView(); using ISceneRunner runner = ISceneRunner.Load(view, autoFree: true); view.Build(level, content, session); await Ready(runner, view);
        Camera3D camera = view.Camera;
        Vector2 northwest = camera.UnprojectPosition(Vector3.Zero), southeast = camera.UnprojectPosition(new(level.Width - 1, 0, level.Height - 1));
        AssertThat(northwest.X < southeast.X).IsTrue(); AssertThat(northwest.Y < southeast.Y).IsTrue();
        Entity hero = session.World.Players[0]; Vector2 before = camera.UnprojectPosition(new(hero.Pos.X, hero.Y, hero.Pos.Z));
        Vector3 forward = -camera.GlobalBasis.Z; var up = new Dungeons.Core.Math.Vec2(forward.X, forward.Z).Normalized;
        session.Step(new PlayerInput(up)); view.Interpolate(1);
        Vector2 after = camera.UnprojectPosition(new(hero.Pos.X, hero.Y, hero.Pos.Z));
        AssertThat(after.Y < before.Y).IsTrue();
    }

    [TestCase, GodotExceptionMonitor]
    public void MaterialFilterTest()
    {
        var models = new ModelLibrary();
        Node3D hero = models.Actor("models/characters/hero.glb", true), chest = models.Actor("models/props/chest.glb");
        try
        {
            AssertThat(MaterialAudit.Check(hero)).IsGreater(0); AssertThat(MaterialAudit.Check(chest)).IsGreater(0);
            CheckFilters(hero, "character");
            CheckFilters(chest, "dither_fade");
        }
        finally { hero.Free(); chest.Free(); }
        // A guard that accepts every material cannot protect future imports.
        var bad = new MeshInstance3D { Mesh = new BoxMesh { Material = new StandardMaterial3D { TextureFilter = BaseMaterial3D.TextureFilterEnum.Linear } } };
        try
        {
            bool rejected = false; try { MaterialAudit.Check(bad); } catch (InvalidDataException) { rejected = true; }
            AssertThat(rejected).IsTrue();
        }
        finally { bad.Free(); }
    }

    [TestCase, GodotExceptionMonitor]
    public async Task LightBudgetTest()
    {
        var content = ContentBridge.LoadRegistry(); LevelDocument level = ContentBridge.LoadBuiltins(content)[0];
        int added = 0;
        for (int z = 0; z < level.Height && added < 20; z++)
        {
            for (int x = 0; x < level.Width && added < 20; x++)
            {
                if (!level.GetCell(x, z).Walkable) { continue; }
                level.AddEntity(new(content.Entities["core:torch"], new(x + 0.5f, z + 0.5f))); added++;
            }
        }
        var session = new GameSession(level, 1); var view = new WorldView(); using ISceneRunner runner = ISceneRunner.Load(view, autoFree: true); view.Build(level, content, session);
        await Ready(runner, view); await runner.SimulateFrames(25);
        int expectedLights = GraphicsRuntime.Instance!.CompatibilityRenderer ? 4 : 8;
        AssertThat(added).IsEqual(20); AssertThat(view.EnabledLightCount).IsEqual(expectedLights);
        AssertThat(view.GetChildren().OfType<OmniLight3D>().Count(n => n.Visible)).IsEqual(expectedLights);
    }
    [TestCase, GodotExceptionMonitor]
    public async Task TriggerCreatedModelsAndLightsRenderAndRemovedBatchesDisappear()
    {
        var content = ContentBridge.LoadRegistry(); LevelDocument level = M6Preview.ObjectFixture(content);
        var session = new GameSession(level, 42); var view = new WorldView(); using ISceneRunner runner = ISceneRunner.Load(view, autoFree: true);
        view.Build(level, content, session); await Ready(runner, view); view.SetProcess(false);
        int removedLight = session.World.Entities.Single(e => e.LevelId == "torch_bridge").Id;
        int removedProp = session.World.Entities.Single(e => e.LevelId == "prop_crate_1").Id;
        AssertThat(view.GetNode<OmniLight3D>("Light_" + removedLight).Visible).IsTrue();
        for (int tick = 0; tick < 6; tick++) { session.Step(default(PlayerInput)); }
        view.Interpolate(1); view._Process(0); await runner.SimulateFrames(2);
        AssertThat(view.HasNode("Light_" + removedLight)).IsFalse(); AssertThat(view.HasNode("Entity_" + removedProp)).IsFalse();
        Entity[] spawned = session.World.Entities.Where(e => e.SpawnTick == 6).ToArray(); AssertThat(spawned.Length).IsEqual(3);
        foreach (Entity entity in spawned) { AssertThat(view.GetNode<Node3D>("Entity_" + entity.Id).Visible).IsTrue(); }
        Entity torch = spawned.Single(e => e.Kind == EntityKind.Light);
        AssertThat(view.GetNode<OmniLight3D>("Light_" + torch.Id).Visible).IsTrue(); AssertThat(view.EnabledLightCount).IsEqual(1);
        // Dummy rendering returns identity transforms; native M6 objects captures check GPU batches.
    }

    private static async Task Ready(ISceneRunner runner, WorldView view)
    {
        for (int frame = 0; frame < 120 && !view.ReadyForPlay; frame++) { await runner.AwaitIdleFrame(); }
        AssertThat(view.ReadyForPlay).IsTrue(); await runner.SimulateFrames(2);
    }
    private static void CheckFilters(Node node, string shader)
    {
        if (node is MeshInstance3D instance)
        {
            for (int i = 0; i < instance.Mesh.GetSurfaceCount(); i++)
            { AssertThat((instance.GetSurfaceOverrideMaterial(i) as ShaderMaterial)!.Shader.ResourcePath).IsEqual("res://shaders/" + shader + ".gdshader"); }
        }
        foreach (Node child in node.GetChildren()) { CheckFilters(child, shader); }
    }
}
