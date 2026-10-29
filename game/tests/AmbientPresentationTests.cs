using System.Text.Json.Nodes;
using Dungeons.Core.Level;
using Dungeons.Core.Sim;
using Dungeons.Game.Rendering;
using Dungeons.Game.UI;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace Dungeons.Game.Tests;

[TestSuite, RequireGodotRuntime]
public sealed class AmbientPresentationTests
{
    [TestCase, GodotExceptionMonitor]
    public async Task AuthoredStreamsLockedEmissionAndHudReadSnapshotsWithoutChangingCore()
    {
        var content = ContentBridge.LoadRegistry(); LevelDocument level = Fixture(content);
        var session = new GameSession(level, 1, startOverride: new(new(5, 1.5f)));
        var root = new Node3D(); using ISceneRunner runner = ISceneRunner.Load(root, autoFree: true);
        var view = new WorldView(); root.AddChild(view); view.Build(level, content, session);
        var hud = new Hud(); root.AddChild(hud); hud.Build(session, view);
        for (int frame = 0; frame < 120 && !view.ReadyForPlay; frame++) { await runner.AwaitIdleFrame(); }
        AssertThat(view.ReadyForPlay).IsTrue(); view.SetProcess(false); hud.SetProcess(false);
        AssertThat(view.AmbientEmitterCount).IsEqual(2); AssertThat(session.World.Mission.ExitLocked).IsTrue();
        ulong hash = session.Hash(); for (int frame = 0; frame < 12; frame++) { view._Process(.1); } hud._Process(0);
        AssertThat(hud.ExitLockVisible).IsTrue(); AssertThat(session.Hash()).IsEqual(hash);
        Entity exit = session.World.Entities.Single(entity => entity.Kind == EntityKind.Exit);
        if (DisplayServer.GetName() != "headless")
        {
            AssertThat(view.Particles.LiveCount).IsGreater(0);
            Color custom = ExitCustom(view, exit); AssertThat(custom.R > .06f && custom.R < .1f).IsTrue();
            await runner.SimulateFrames(2); Save(root, "locked");
        }
        for (int tick = 0; tick < 6; tick++) { session.Step(default(PlayerInput)); }
        AssertThat(session.World.Mission.ExitLocked).IsFalse(); hash = session.Hash();
        view._Process(.05); view._Process(.05); hud._Process(0);
        AssertThat(hud.ExitLockVisible).IsFalse(); AssertThat(session.Hash()).IsEqual(hash);
        if (DisplayServer.GetName() != "headless")
        {
            Color custom = ExitCustom(view, exit); AssertThat(custom.R).IsGreater(.99f); AssertThat(custom.A).IsGreater(.34f);
            await runner.SimulateFrames(2); Save(root, "unlocked");
        }
        for (int tick = 6; tick < 12; tick++) { session.Step(default(PlayerInput)); }
        view.Interpolate(1); view._Process(.1); AssertThat(view.AmbientEmitterCount).IsEqual(1);
        AssertThat(session.World.Entities.Any(entity => entity.LevelId == "torch")).IsFalse();
        GD.Print($"AMBIENT_PRESENTATION=PASS renderer={RenderingServer.GetCurrentRenderingMethod()} particles={view.Particles.LiveCount} emitters={view.AmbientEmitterCount}");
    }

    [TestCase, GodotExceptionMonitor]
    public void ContinuousStreamsShareTheExistingParticleCapAndExpireWithoutNewNodes()
    {
        var particles = new WorldParticles(); using ISceneRunner runner = ISceneRunner.Load(particles, autoFree: true);
        var camera = new Camera3D(); particles.AddChild(camera); int children = particles.GetChildCount();
        for (int i = 0; i < 4000; i++) { particles.Stream(Vector3.Zero, 1, (i & 1) == 0 ? AmbientParticleKind.TorchFlame : AmbientParticleKind.BeaconStream); }
        particles.Burst(Vector3.Zero, 1, true, 1000); AssertThat(particles.LiveCount).IsEqual(WorldParticles.Capacity);
        particles.Advance(1.1, camera); AssertThat(particles.GetChildCount()).IsEqual(children);
        particles.Advance(3, camera); AssertThat(particles.LiveCount).IsEqual(0);
    }

    private static Color ExitCustom(WorldView view, Entity exit)
    {
        foreach (MultiMeshInstance3D batch in view.GetChildren().OfType<MultiMeshInstance3D>())
        {
            for (int i = 0; i < batch.Multimesh.InstanceCount; i++)
            { if (batch.Multimesh.GetInstanceTransform(i).Origin.IsEqualApprox(new(exit.Pos.X, exit.Y, exit.Pos.Z))) { return batch.Multimesh.GetInstanceCustomData(i); } }
        }
        throw new InvalidOperationException("The actual exit instance was not uploaded.");
    }
    private static void Save(Node root, string state)
    {
        string folder = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "../artifacts/m5/ambient")); Directory.CreateDirectory(folder);
        using Image image = root.GetViewport().GetTexture().GetImage();
        AssertThat(image.SavePng(Path.Combine(folder, state + "-" + RenderingServer.GetCurrentRenderingMethod() + ".png"))).IsEqual(Error.Ok);
    }
    private static LevelDocument Fixture(Dungeons.Core.Content.ContentRegistry content)
    {
        JsonObject source = new()
        {
            ["formatVersion"] = 1, ["id"] = "test.ambient", ["name"] = "Ambient", ["author"] = "Test", ["version"] = "1.0.0",
            ["size"] = new JsonObject { ["width"] = 8, ["height"] = 8 }, ["palette"] = new JsonObject { ["."] = "core:grass" },
            ["layers"] = new JsonObject { ["floor"] = new JsonArray(Enumerable.Range(0, 8).Select(_ => (JsonNode?)JsonValue.Create("........")).ToArray()),
                ["heights"] = new JsonArray(Enumerable.Range(0, 8).Select(_ => (JsonNode?)JsonValue.Create("00000000")).ToArray()) },
            ["entities"] = JsonNode.Parse("""[{"id":"spawn","type":"core:player_spawn","x":1.5,"z":6.5},{"id":"exit","type":"core:exit","x":6.5,"z":1.5},{"id":"torch","type":"core:torch","x":3.5,"z":3.5}]"""),
            ["objectives"] = JsonNode.Parse("""[{"id":"wait","text":"Wait","type":"survive","seconds":0.1,"initialState":"active"}]"""),
            ["completion"] = JsonNode.Parse("""{"type":"exit","exit":"exit","requires":["wait"]}"""),
            ["triggers"] = JsonNode.Parse("""[{"id":"remove_torch","event":{"type":"timer","seconds":0.2},"actions":[{"type":"remove_entity","id":"torch"}]}]""")
        };
        LevelLoadResult loaded = LevelLoader.Load(source.ToJsonString(), content);
        if (!loaded.Success) { throw new InvalidDataException(string.Join('\n', loaded.Diagnostics)); } return loaded.Document!;
    }
}
