using Dungeons.Core.Level;
using Dungeons.Core.Sim;
using Dungeons.Game.Input;
using Dungeons.Game.Rendering;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace Dungeons.Game.Tests;

[TestSuite, RequireGodotRuntime]
public sealed class HeroHostTests
{
    [TestCase, GodotExceptionMonitor]
    public async Task InputLatchTest()
    {
        var content = ContentBridge.LoadRegistry(); LevelDocument level = ContentBridge.LoadBuiltins(content)[0];
        var session = new GameSession(level, 1); var view = new WorldView(); using ISceneRunner runner = ISceneRunner.Load(view, autoFree: true);
        view.Build(level, content, session); await Ready(runner, view);
        InputAdapter.EnsureBindings(); var adapter = new InputAdapter();
        adapter.HandleEvent(new InputEventKey { PhysicalKeycode = Key.J, Pressed = true });
        adapter.Sample(view.Camera, session.World);
        adapter.HandleEvent(new InputEventKey { PhysicalKeycode = Key.J, Pressed = false });
        // Four 240-Hz render samples precede one 60-Hz simulation step.
        for (int frame = 0; frame < 3; frame++) { adapter.Sample(view.Camera, session.World); }
        PlayerInput command = adapter.ConsumeTickInput(); AssertThat(command.Pressed.HasFlag(InputButtons.Attack)).IsTrue();
        AssertThat(adapter.ConsumeTickInput().Pressed).IsEqual(InputButtons.None);
        session.Step(command); AssertThat(session.World.Players[0].Action.Phase).IsEqual(AttackPhase.Windup);
        AssertThat(new InputEventJoypadButton { ButtonIndex = JoyButton.X, Pressed = true }.IsActionPressed("attack")).IsTrue();
        AssertThat(new InputEventJoypadButton { ButtonIndex = JoyButton.Y, Pressed = true }.IsActionPressed("potion")).IsTrue();
        adapter.HandleEvent(new InputEventKey { PhysicalKeycode = Key.K, Pressed = true }); adapter.Clear();
        AssertThat(adapter.ConsumeTickInput().Pressed).IsEqual(InputButtons.None);
    }

    [TestCase, GodotExceptionMonitor]
    public async Task ArrowViewsReuseNodesAndRollAnimationDoesNotOverwriteCoreTransform()
    {
        var content = ContentBridge.LoadRegistry(); LevelDocument level = ContentBridge.LoadBuiltins(content)[0];
        var session = new GameSession(level, 1); var view = new WorldView(); using ISceneRunner runner = ISceneRunner.Load(view, autoFree: true);
        view.Build(level, content, session); await Ready(runner, view); view.SetProcess(false);
        Fire(session); view.Interpolate(1);
        Entity first = session.World.Entities.Single(e => e.Kind == EntityKind.Projectile); int firstId = first.Id;
        Node3D firstNode = view.GetNode<Node3D>("Entity_" + firstId); ulong instanceId = firstNode.GetInstanceId();
        for (int tick = 0; tick < 120; tick++) { session.Step(default(PlayerInput)); }
        view.Interpolate(1); AssertThat(firstNode.Visible).IsFalse(); AssertThat(session.World.FindEntity(firstId) is null).IsTrue();
        Fire(session); view.Interpolate(1);
        Entity second = session.World.Entities.Single(e => e.Kind == EntityKind.Projectile);
        AssertThat(second.Id > firstId).IsTrue(); AssertThat(view.GetNode<Node3D>("Entity_" + second.Id).GetInstanceId()).IsEqual(instanceId);
        Entity hero = session.World.Players[0];
        session.Step(new PlayerInput(default, pressed: InputButtons.Roll)); view.Interpolate(1);
        Node3D wrapper = view.GetNode<Node3D>("Entity_" + hero.Id); AnimationPlayer animation = Animation(wrapper)!;
        AnimationTree tree = wrapper.GetNode<AnimationTree>("MotionTree");
        var playback = tree.Get("parameters/states/playback").As<AnimationNodeStateMachinePlayback>();
        AssertThat(playback.GetCurrentNode().ToString()).IsEqual("roll"); tree.Advance(.1);
        string bonePath = animation.GetAnimation("roll").TrackGetPath(0).ToString().Split(':')[0];
        Node3D rig = animation.GetNode(animation.RootNode).GetNode<Node3D>(bonePath);
        AssertThat(MathF.Abs(rig.Rotation.X) > .5f).IsTrue();
        AssertThat(wrapper.Rotation.X).IsEqual(0f);
        for (int tick = 0; tick < 30; tick++) { session.Step(default(PlayerInput)); }
        view.Interpolate(1); AssertThat(playback.GetCurrentNode().ToString()).IsEqual("idle"); tree.Advance(.08);
        AssertThat(MathF.Abs(rig.Rotation.X) < .0001f).IsTrue();
    }

    [TestCase, GodotExceptionMonitor]
    public async Task HudBindsAmmoCooldownsAndActiveScheme()
    {
        var content = ContentBridge.LoadRegistry(); var game = new GameplayScreen(); using ISceneRunner runner = ISceneRunner.Load(game, autoFree: true);
        game.Build(ContentBridge.LoadBuiltins(content)[0], content, 1); await Ready(runner, game.View); game.SetProcess(false);
        Fire(game.Session); game.Hud._Process(0);
        AssertThat(game.Hud.ActionText.Contains("29", StringComparison.Ordinal)).IsTrue();
        game.Session.Step(new PlayerInput(default, pressed: InputButtons.Roll | InputButtons.Potion)); game.Hud._Process(0);
        AssertThat(game.Hud.ActionText.Contains("30s", StringComparison.Ordinal)).IsTrue();
        AssertThat(game.Hud.ActionText.Contains("Rolling", StringComparison.Ordinal)).IsTrue();
        game.Hud.UsesGamepad = true; game.Hud._Process(0); AssertThat(game.Hud.ActionText.Contains("[RT]", StringComparison.Ordinal)).IsTrue();
    }

    private static void Fire(GameSession session)
    { session.Step(new PlayerInput(default, new(1, 0), true, InputButtons.Ranged)); for (int tick = 0; tick < 6; tick++) { session.Step(default(PlayerInput)); } }
    private static async Task Ready(ISceneRunner runner, WorldView view)
    { for (int frame = 0; frame < 120 && !view.ReadyForPlay; frame++) { await runner.AwaitIdleFrame(); } AssertThat(view.ReadyForPlay).IsTrue(); await runner.SimulateFrames(2); }
    private static AnimationPlayer? Animation(Node node)
    { if (node is AnimationPlayer player) { return player; } foreach (Node child in node.GetChildren()) { AnimationPlayer? found = Animation(child); if (found is not null) { return found; } } return null; }
}
