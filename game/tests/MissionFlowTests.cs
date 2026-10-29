using System.Text.Json.Nodes;
using Dungeons.Core.Level;
using Dungeons.Core.Sim;
using Dungeons.Game.Persistence;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace Dungeons.Game.Tests;

[TestSuite, RequireGodotRuntime]
public sealed class MissionFlowTests
{
    [TestCase, GodotExceptionMonitor]
    public async Task FrozenResultsRetryContinueAndProfileBinding()
    {
        var content = ContentBridge.LoadRegistry();
        foreach (string action in new[] { "complete_level", "fail_level" })
        {
            JsonObject source = ContentBridge.LoadBuiltins(content)[0].ToJsonObject();
            source["objectives"] = new JsonArray(); source["completion"]!["requires"] = new JsonArray();
            source["triggers"] = JsonNode.Parse($$"""[{"id":"end","event":{"type":"timer","seconds":0.2},"actions":[{"type":"give","item":"emerald","amount":7},{"type":"{{action}}"}]}]""");
            LevelLoadResult loaded = LevelLoader.Load(source.ToJsonString(), content); AssertThat(loaded.Success).IsTrue();
            string directory = System.IO.Path.GetFullPath(System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"), "../artifacts/m6/flow-tests", Guid.NewGuid().ToString("N")));
            var store = new RunStore(directory); RunTicket ticket = store.Begin(loaded.Document!.Id, overrideSeed: 99);
            var game = new GameplayScreen(); using ISceneRunner runner = ISceneRunner.Load(game, autoFree: true); game.Build(loaded.Document!, content, 99, store, ticket); game.SetProcess(false);
            await Ready(runner, game); int retry = 0, quit = 0; game.RestartRequested += () => retry++; game.QuitRequested += () => quit++;
            for (int tick = 0; tick < 12; tick++) { game.Session.Step(default(PlayerInput)); } game._Process(0);
            AssertThat(game.ResultsVisible).IsTrue(); AssertThat(game.ResultsText.Contains("Seed", StringComparison.Ordinal)).IsTrue(); AssertThat(game.ResultsText.Contains('\n')).IsTrue();
            AssertThat(store.Profile.TotalEmeralds).IsEqual(7L); game._Process(0); AssertThat(store.Profile.TotalEmeralds).IsEqual(7L);
            Button primary = Buttons(game).Single(button => button.Name == "PrimaryAction"); primary.EmitSignal(BaseButton.SignalName.Pressed);
            AssertThat(retry).IsEqual(action == "fail_level" ? 1 : 0); AssertThat(quit).IsEqual(action == "complete_level" ? 1 : 0);
            AssertThat(game.Session.Tick).IsEqual(12); AssertThat(store.Profile.Levels[loaded.Document!.Id].Attempts).IsEqual(1);
        }
    }

    [TestCase, GodotExceptionMonitor]
    public async Task DeathAndLifecycleOverlaysFollowCoreSnapshots()
    {
        var content = ContentBridge.LoadRegistry(); JsonObject source = ContentBridge.LoadBuiltins(content)[0].ToJsonObject();
        JsonNode enemy = source["entities"]!.AsArray().Single(entity => entity!["id"]!.GetValue<string>() == "z_bank_1")!; enemy["x"] = 5.5; enemy["z"] = 36.5;
        LevelLoadResult loaded = LevelLoader.Load(source.ToJsonString(), content); AssertThat(loaded.Success).IsTrue();
        var game = new GameplayScreen(); using ISceneRunner runner = ISceneRunner.Load(game, autoFree: true); game.Build(loaded.Document!, content, 1); game.SetProcess(false); await Ready(runner, game);
        game._Notification((int)Node.NotificationApplicationPaused); int tick = game.Session.Tick; game.Session.Step(default(PlayerInput)); AssertThat(game.Session.Tick).IsEqual(tick); AssertThat(game.Session.State).IsEqual(SessionState.Paused);
        await runner.SimulateFrames(2); // Let the deferred pause overlay join its owner.
        game._Notification((int)Node.NotificationApplicationResumed); AssertThat(game.Session.State).IsEqual(SessionState.Paused);
        game._UnhandledInput(new InputEventAction { Action = "pause", Pressed = true }); AssertThat(game.Session.State).IsEqual(SessionState.Running);
        for (int i = 0; i < 2400 && game.Session.World.Players[0].Alive; i++) { game.Session.Step(default(PlayerInput)); game.Session.DrainEvents(); }
        AssertThat(game.Session.World.Players[0].Alive).IsFalse(); game._Process(0); AssertThat(game.DeathVisible).IsTrue();
        game._UnhandledInput(new InputEventAction { Action = "pause", Pressed = true }); AssertThat(game.Session.State).IsEqual(SessionState.Running);
        game.View.SetProcess(false);
        Node3D pose = game.View.GetNode("Entity_" + game.Session.World.Players[0].Id).GetNode<Node3D>("VisualPose");
        for (int i = 0; i < 90; i++) { game.Session.Step(default(PlayerInput)); } game.View.Interpolate(1);
        AssertThat(MathF.Abs(pose.Position.Y) < .00001f).IsTrue(); int particles = game.View.Particles.LiveCount;
        for (int i = 0; i < 15; i++) { game.Session.Step(default(PlayerInput)); }
        ulong hash = game.Session.Hash(); game.View.Interpolate(1);
        AssertThat(MathF.Abs(pose.Position.Y + 1) < .00001f).IsTrue(); AssertThat(game.View.Particles.LiveCount).IsEqual(particles + 24);
        game.View.Interpolate(1); AssertThat(game.View.Particles.LiveCount).IsEqual(particles + 24); AssertThat(game.Session.Hash()).IsEqual(hash);
        for (int i = 0; i < 15; i++) { game.Session.Step(default(PlayerInput)); } game._Process(0);
        AssertThat(game.Session.World.Players[0].Alive).IsTrue(); AssertThat(game.DeathVisible).IsFalse(); AssertThat(game.ResultsVisible).IsFalse();
        AssertThat(pose.Position).IsEqual(Vector3.Zero);
        await runner.SimulateFrames(2); // Drain overlays queued for deletion by resume.
    }
    private static IEnumerable<Button> Buttons(Node root)
    { if (root is Button button) { yield return button; } foreach (Node child in root.GetChildren()) { foreach (Button nested in Buttons(child)) { yield return nested; } } }
    private static async Task Ready(ISceneRunner runner, GameplayScreen game)
    { for (int frame = 0; frame < 120 && !game.View.ReadyForPlay; frame++) { await runner.AwaitIdleFrame(); } AssertThat(game.View.ReadyForPlay).IsTrue(); await runner.SimulateFrames(2); }
}
