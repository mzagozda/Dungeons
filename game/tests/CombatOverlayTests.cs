using Dungeons.Core.Sim;
using Dungeons.Game.Rendering;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace Dungeons.Game.Tests;

[TestSuite, RequireGodotRuntime]
public sealed class CombatOverlayTests
{
    [TestCase, GodotExceptionMonitor]
    public async Task PrewarmedPrototypePoolsHitStopAndTerrainArrowsStayPresentationOnly()
    {
        var content = ContentBridge.LoadRegistry(); var level = ContentBridge.LoadBuiltins(content)[0]; var session = new GameSession(level, 1);
        var view = new WorldView(); using ISceneRunner runner = ISceneRunner.Load(view, autoFree: true); view.Build(level, content, session);
        for (int frame = 0; frame < 120 && !view.ReadyForPlay; frame++) { await runner.AwaitIdleFrame(); }
        AssertThat(view.ReadyForPlay).IsTrue(); view.SetProcess(false); view.CombatOverlay.SetProcess(false);
        AssertThat(view.AvailableActorViews("core:zombie") + session.World.Entities.Count(e => e.Def.Id == "core:zombie")).IsEqual(16);
        AssertThat(view.AvailableActorViews("core:skeleton") + session.World.Entities.Count(e => e.Def.Id == "core:skeleton")).IsEqual(8);
        AssertThat(view.AvailableActorViews("core:arrow_hero")).IsEqual(64); AssertThat(view.AvailableActorViews("core:arrow_skeleton")).IsEqual(64);
        AssertThat(view.AvailableActorViews("core:emerald_pile") + session.World.Entities.Count(e => e.Def.Id == "core:emerald_pile")).IsEqual(64);
        Entity hero = session.World.Players[0], enemy = session.World.Entities.First(e => e.Kind == EntityKind.Enemy); ulong hash = session.Hash();
        view.ConsumeEvents(new[] { new SimEvent(session.Tick, SimEventKind.DamageApplied, enemy.Id, enemy.Pos, hero.Id, 18, meleeSwing: 3) }); view.Interpolate(1);
        AnimationTree source = view.GetNode("Entity_" + hero.Id).GetNode<AnimationTree>("MotionTree"), target = view.GetNode("Entity_" + enemy.Id).GetNode<AnimationTree>("MotionTree");
        AssertThat(source.Get("parameters/freeze/scale").AsSingle()).IsEqual(0f); AssertThat(target.Get("parameters/freeze/scale").AsSingle()).IsEqual(0f);
        AssertThat(view.GetNode("Entity_" + hero.Id).HasNode("ShadowBlob")).IsTrue();
        view._Process(.069); view.Interpolate(1); AssertThat(source.Get("parameters/freeze/scale").AsSingle()).IsEqual(0f);
        view._Process(.002); view.Interpolate(1); AssertThat(source.Get("parameters/freeze/scale").AsSingle() > 0).IsTrue(); AssertThat(target.Get("parameters/freeze/scale").AsSingle() > 0).IsTrue();
        view.ReduceMotion = true; view.ConsumeEvents(new[] { new SimEvent(session.Tick, SimEventKind.DamageApplied, enemy.Id, enemy.Pos, hero.Id, 12, meleeSwing: 1) }); view.Interpolate(1);
        AssertThat(source.Get("parameters/freeze/scale").AsSingle() > 0).IsTrue();
        for (int i = 0; i < 80; i++) { view.ConsumeEvents(new[] { new SimEvent(session.Tick, SimEventKind.ProjectileHitTerrain, 0, hero.Pos, direction: new(1, 0), y: 1.3f) }); }
        AssertThat(view.StuckArrowCount).IsEqual(64); AssertThat(view.GetNode<Node3D>("StuckArrow_0").Position.Y).IsEqual(1.3f);
        view._Process(1.99); AssertThat(view.StuckArrowCount).IsEqual(64); view._Process(.02); AssertThat(view.StuckArrowCount).IsEqual(0);
        AssertThat(session.Hash()).IsEqual(hash);
    }

    [TestCase, GodotExceptionMonitor]
    public async Task ActualMeleeEventsProjectBoundedCombatFeedbackWithoutChangingCore()
    {
        var content = ContentBridge.LoadRegistry();
        GameSession session = new ScenarioBuilder(20, 20).Entity(content.Entities["core:hero"], new(10, 10))
            .Entity(content.Entities["core:zombie"], new(11.3f, 10)).Build().Session;
        var root = new Node3D(); using ISceneRunner runner = ISceneRunner.Load(root, autoFree: true);
        var camera = new Camera3D { Position = new(10, 12, 18), Current = true }; root.AddChild(camera); camera.LookAt(new(10, 0, 10));
        var overlay = new CombatOverlay(); root.AddChild(overlay); overlay.Build(session, camera); overlay.SetProcess(false);
        await runner.SimulateFrames(2); Entity enemy = session.World.Entities.Single(e => e.Kind == EntityKind.Enemy);
        for (int tick = 0; tick < 8; tick++)
        {
            session.Step(tick == 0 ? new PlayerInput(default, new(1, 0), true, InputButtons.Attack) : default);
            overlay.ConsumeEvents(session.DrainEvents()); overlay.Advance(0, 1);
        }
        AssertThat(enemy.Health.Current < enemy.Health.Max).IsTrue(); AssertThat(overlay.VisibleBarCount).IsEqual(1);
        AssertThat(overlay.ActiveNumberCount).IsEqual(1);
        ulong before = WorldHasher.Compute(session.World);
        overlay.ConsumeEvents(new[] { new SimEvent(session.Tick, SimEventKind.EnemyAlerted, enemy.Id, enemy.Pos) }); overlay.Advance(0, 1);
        AssertThat(overlay.VisibleAlertCount).IsEqual(1); overlay.Advance(.31, 1); AssertThat(overlay.VisibleAlertCount).IsEqual(0);
        overlay.Advance(.4, 1); AssertThat(overlay.ActiveNumberCount).IsEqual(0); AssertThat(overlay.VisibleBarCount).IsEqual(1);
        overlay.Advance(2.3, 1); AssertThat(overlay.VisibleBarCount).IsEqual(0);
        int children = overlay.GetChildCount();
        for (int i = 0; i < 300; i++) { overlay.ConsumeEvents(new[] { new SimEvent(session.Tick, SimEventKind.DamageApplied, enemy.Id, enemy.Pos, amount: 8) }); }
        overlay.Advance(0, 1); AssertThat(overlay.ActiveNumberCount).IsEqual(128); AssertThat(overlay.GetChildCount()).IsEqual(children);
        overlay.DamageNumbersEnabled = false; overlay.Advance(0, 1); AssertThat(overlay.ActiveNumberCount).IsEqual(0);
        AssertThat(WorldHasher.Compute(session.World)).IsEqual(before);
    }
}
