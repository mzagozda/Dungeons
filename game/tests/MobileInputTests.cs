using Dungeons.Core.Sim;
using Dungeons.Game.Input;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace Dungeons.Game.Tests;

[TestSuite, RequireGodotRuntime]
public sealed class MobileInputTests
{
    [TestCase, GodotExceptionMonitor]
    public async Task FocusLossWhileBuildingDefersOnlyPauseUiAndEarlyResumeCancelsIt()
    {
        var content = ContentBridge.LoadRegistry(); var game = new GameplayScreen(); using ISceneRunner runner = ISceneRunner.Load(game, autoFree: true);
        bool injected = false;
        game.ChildEnteredTree += child =>
        {
            if (!injected && child is Dungeons.Game.Rendering.WorldView)
            { injected = true; game._Notification((int)Node.NotificationApplicationFocusOut); }
        };
        game.Build(ContentBridge.LoadBuiltins(content)[0], content, 42, touchPreview: true); game.SetProcess(false);
        AssertThat(injected).IsTrue(); AssertThat(game.Session.State).IsEqual(SessionState.Paused); AssertThat(game.Session.Tick).IsEqual(0);
        game._Notification((int)Node.NotificationWMGoBackRequest); await runner.SimulateFrames(2);
        AssertThat(game.Session.State).IsEqual(SessionState.Running);
        AssertThat(game.FindChildren("*", "Button", true, false).OfType<Button>().Any(button => button.Text == game.Tr("RESUME"))).IsFalse();
        await Ready(runner, game);
        game._Notification((int)Node.NotificationApplicationFocusOut); AssertThat(game.Session.State).IsEqual(SessionState.Paused);
        int tick = game.Session.Tick; game._Process(1); AssertThat(game.Session.Tick).IsEqual(tick); await runner.SimulateFrames(2);
        Button resume = game.FindChildren("*", "Button", true, false).OfType<Button>().Single(button => button.Text == game.Tr("RESUME"));
        AssertThat(resume.IsInsideTree()).IsTrue(); resume.EmitSignal(BaseButton.SignalName.Pressed); AssertThat(game.Session.State).IsEqual(SessionState.Running);
    }
    [TestCase, GodotExceptionMonitor]
    public async Task ScreenTouchEventsDriveMovementAndTapWithoutEmulatedMouseAttack()
    {
        var content = ContentBridge.LoadRegistry(); var game = new GameplayScreen(); using ISceneRunner runner = ISceneRunner.Load(game, autoFree: true);
        game.Build(ContentBridge.LoadBuiltins(content)[0], content, 42, touchPreview: true); game.SetProcess(false); await Ready(runner, game);
        TouchControls touch = game.Touch!; touch.ConfigurePreview(new(880, 400), new(24, 0, 24, 16));
        TouchTarget attack = Target(touch, InputButtons.Attack);
        var initial = game.Session.World.Players[0].Pos;
        Press(touch, 21, new(200, 200)); Drag(touch, 21, new(240, 200)); Press(touch, 22, attack.Centre);
        Godot.Input.ActionPress("attack");
        try { game._Process(GameConstants.Dt); } finally { Godot.Input.ActionRelease("attack"); }
        AssertThat(game.Session.World.Players[0].Pos != initial).IsTrue(); AssertThat(game.Session.World.Players[0].Action.Phase).IsEqual(AttackPhase.Idle);
        Release(touch, 22, attack.Centre); game._Process(GameConstants.Dt);
        AssertThat(game.Session.World.Players[0].Action.Phase).IsEqual(AttackPhase.Windup); AssertThat(touch.State.JoystickActive).IsTrue();
        game._Notification((int)Node.NotificationApplicationFocusOut); AssertThat(game.Session.State).IsEqual(SessionState.Paused); AssertThat(touch.State.JoystickActive).IsFalse();
        AssertThat(touch.State.Sample(1).Held).IsEqual(InputButtons.None);
        await PausedUiReady(runner, game);
    }

    [TestCase, GodotExceptionMonitor]
    public async Task TouchPauseAndSafeInsetsBindToTheGameplayScreen()
    {
        var content = ContentBridge.LoadRegistry(); var game = new GameplayScreen(); using ISceneRunner runner = ISceneRunner.Load(game, autoFree: true);
        game.Build(ContentBridge.LoadBuiltins(content)[0], content, 42, touchPreview: true); game.SetProcess(false); await Ready(runner, game);
        TouchControls touch = game.Touch!; touch.ConfigurePreview(new(880, 400), new(24, 8, 24, 16));
        AssertThat(game.Hud.GetNode<Control>("HudRoot").OffsetLeft > 0).IsTrue();
        Press(touch, 3, new(200, 200)); Drag(touch, 3, new(240, 200));
        Press(touch, 4, touch.PauseCentre); Release(touch, 4, touch.PauseCentre);
        AssertThat(game.Session.State).IsEqual(SessionState.Paused); AssertThat(touch.State.JoystickActive).IsFalse(); AssertThat(touch.Visible).IsFalse();
        int tick = game.Session.Tick; game._Process(1); AssertThat(game.Session.Tick).IsEqual(tick);
        game._Notification((int)Node.NotificationWMGoBackRequest); AssertThat(game.Session.State).IsEqual(SessionState.Running);
        game._Notification((int)Node.NotificationWMGoBackRequest); AssertThat(game.Session.State).IsEqual(SessionState.Paused);
    }

    [TestCase, GodotExceptionMonitor]
    public async Task BowDragReleaseKeepsProjectedAimAcrossFastRenderFrames()
    {
        var content = ContentBridge.LoadRegistry(); var game = new GameplayScreen(); using ISceneRunner runner = ISceneRunner.Load(game, autoFree: true);
        game.Build(ContentBridge.LoadBuiltins(content)[0], content, 42, touchPreview: true); game.SetProcess(false); await Ready(runner, game);
        TouchControls touch = game.Touch!; touch.ConfigurePreview(new(880, 400), new(24, 0, 24, 16)); TouchTarget bow = Target(touch, InputButtons.Ranged);
        Press(touch, 0, new(200, 200)); Drag(touch, 0, new(240, 200)); Press(touch, 1, bow.Centre); Drag(touch, 1, bow.Centre + new Dungeons.Core.Math.Vec2(30, 0));
        game._Process(GameConstants.Dt); AssertThat(game.Session.World.Players[0].Action.Phase).IsEqual(AttackPhase.Idle);
        var aim = InputAdapter.Project(game.View.Camera, new(1, 0)).Normalized;
        Release(touch, 1, bow.Centre + new Dungeons.Core.Math.Vec2(30, 0));
        for (int frame = 0; frame < 4; frame++) { game._Process(GameConstants.Dt / 4); }
        var hero = game.Session.World.Players[0]; AssertThat(hero.Action.Ranged).IsTrue(); AssertThat(hero.Action.Phase).IsEqual(AttackPhase.Windup);
        AssertThat((hero.Facing - aim).Length < .0001f).IsTrue(); AssertThat(touch.State.JoystickActive).IsTrue();
        game._Notification((int)Node.NotificationApplicationFocusOut); AssertThat(touch.State.JoystickActive).IsFalse();
        await PausedUiReady(runner, game);
    }
    [TestCase, GodotExceptionMonitor]
    public async Task FourFingerTapTogglesDebugOnceAndConsumesActionReleases()
    {
        var content = ContentBridge.LoadRegistry(); var game = new GameplayScreen(); using ISceneRunner runner = ISceneRunner.Load(game, autoFree: true);
        game.Build(ContentBridge.LoadBuiltins(content)[0], content, 42, touchPreview: true); game.SetProcess(false); await Ready(runner, game);
        TouchControls touch = game.Touch!; touch.ConfigurePreview(new(880, 400), new(24, 0, 24, 16));
        Dungeons.Core.Math.Vec2[] points = [new(200, 200), Target(touch, InputButtons.Attack).Centre, Target(touch, InputButtons.Ranged).Centre, Target(touch, InputButtons.Roll).Centre];
        ulong hash = game.Session.Hash(); AssertThat(game.Hud.DebugVisible).IsFalse();
        for (int i = 0; i < points.Length; i++) { Press(touch, 30 + i, points[i]); }
        for (int i = 0; i < points.Length; i++)
        { Release(touch, 30 + i, points[i]); AssertThat(game.Hud.DebugVisible).IsEqual(i == 3); }
        AssertThat(touch.State.JoystickActive).IsFalse(); AssertThat(touch.State.Sample(1).Pressed).IsEqual(InputButtons.None);
        AssertThat(game.Session.Hash()).IsEqual(hash);
        for (int i = 0; i < points.Length; i++) { Press(touch, 40 + i, points[i]); }
        game._Notification((int)Node.NotificationApplicationFocusOut);
        for (int i = 0; i < points.Length; i++) { Release(touch, 40 + i, points[i]); }
        AssertThat(game.Hud.DebugVisible).IsTrue();
        await PausedUiReady(runner, game);
    }

    private static async Task PausedUiReady(ISceneRunner runner, GameplayScreen game)
    {
        // Focus notifications build the overlay outside the current tree traversal.
        // Observe that deferred work before the runner snapshots orphan nodes.
        await runner.SimulateFrames(2);
        AssertThat(game.FindChildren("*", "Button", true, false).OfType<Button>().Any(button => button.IsInsideTree() && button.Text == game.Tr("RESUME"))).IsTrue();
    }

    private static TouchTarget Target(TouchControls controls, InputButtons action)
    { foreach (TouchTarget target in controls.State.Layout.Targets) { if (target.Action == action) { return target; } } throw new ArgumentException(nameof(action)); }
    private static void Press(TouchControls controls, int id, Dungeons.Core.Math.Vec2 dp) => controls.GetViewport().PushInput(new InputEventScreenTouch { Index = id, Position = controls.DpToViewport(dp), Pressed = true }, true);
    private static void Release(TouchControls controls, int id, Dungeons.Core.Math.Vec2 dp) => controls.GetViewport().PushInput(new InputEventScreenTouch { Index = id, Position = controls.DpToViewport(dp), Pressed = false }, true);
    private static void Drag(TouchControls controls, int id, Dungeons.Core.Math.Vec2 dp) => controls.GetViewport().PushInput(new InputEventScreenDrag { Index = id, Position = controls.DpToViewport(dp) }, true);
    private static async Task Ready(ISceneRunner runner, GameplayScreen game)
    {
        for (int frame = 0; frame < 120 && !game.View.ReadyForPlay; frame++) { await runner.AwaitIdleFrame(); }
        AssertThat(game.View.ReadyForPlay).IsTrue(); await runner.SimulateFrames(2); game._Process(0);
    }
}
