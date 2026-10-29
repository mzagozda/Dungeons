using Dungeons.Core.Sim;
using Dungeons.Game.Rendering;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace Dungeons.Game.Tests;

[TestSuite, RequireGodotRuntime]
public sealed class PresentationEffectsTests
{
    [TestCase, GodotExceptionMonitor]
    public async Task ParticleOverflowExpiryBillboardsAndReactionsDoNotTouchCore()
    {
        var content = ContentBridge.LoadRegistry(); var level = ContentBridge.LoadBuiltins(content)[0]; var session = new GameSession(level, 1);
        var view = new WorldView(); using ISceneRunner runner = ISceneRunner.Load(view, autoFree: true); view.Build(level, content, session);
        for (int frame = 0; frame < 120 && !view.ReadyForPlay; frame++) { await runner.AwaitIdleFrame(); }
        AssertThat(view.ReadyForPlay).IsTrue(); view.SetProcess(false); view.CombatOverlay.SetProcess(false);
        ulong hash = session.Hash(); int ambient = view.Particles.LiveCount; Entity hero = session.World.Players[0]; Node3D wrapper = view.GetNode<Node3D>("Entity_" + hero.Id), pose = wrapper.GetNode<Node3D>("VisualPose");
        Vector3 original = wrapper.Position;
        view.ConsumeEvents(new[] { new SimEvent(session.Tick, SimEventKind.DamageApplied, hero.Id, hero.Pos, amount: 25, y: hero.Y) }); view.Interpolate(1);
        AssertThat(pose.Scale.IsEqualApprox(new(1.1f, .9f, 1.1f))).IsTrue(); AssertThat(wrapper.Position).IsEqual(original);
        AssertThat(view.Particles.LiveCount).IsEqual(ambient + 12);
        view._Process(.05); view.Interpolate(1); AssertThat(pose.Scale.IsEqualApprox(new(1.05f, .95f, 1.05f))).IsTrue();
        view.ReduceMotion = true; view.Interpolate(1); AssertThat(pose.Scale).IsEqual(Vector3.One);
        view._Process(.06); view.Interpolate(1); AssertThat(pose.Scale).IsEqual(Vector3.One);
        for (int i = 0; i < 20; i++) { view.Particles.Burst(new(hero.Pos.X, hero.Y, hero.Pos.Z), 1, true, 200); view.Particles.Burst(new(hero.Pos.X, hero.Y, hero.Pos.Z), 1, false, 200); }
        AssertThat(view.Particles.LiveCount).IsEqual(WorldParticles.Capacity); int children = view.Particles.GetChildCount();
        view.Particles.Advance(1.1, view.Camera);
        GD.Print($"EFFECTS_BACKEND display={DisplayServer.GetName()} renderer={RenderingServer.GetCurrentRenderingMethod()} particles={view.Particles.LiveCount}");
        foreach (Node child in view.Particles.GetChildren())
        {
            var batch = ((MultiMeshInstance3D)child).Multimesh; AssertThat(batch.VisibleInstanceCount).IsEqual(1000);
            // Dummy rendering stores no per-instance transforms. Native M5Preview
            // checks the uploaded billboard bases and sizes on the real backend.
            if (DisplayServer.GetName() != "headless")
            {
                Transform3D transform = batch.GetInstanceTransform(0); AssertThat(transform.Basis.Scale.X >= .099f && transform.Basis.Scale.X <= .251f).IsTrue();
                AssertThat(MathF.Abs(transform.Basis.Z.Normalized().Dot(view.Camera.GlobalBasis.Z)) > .999f).IsTrue();
            }
        }
        view.Particles.Advance(1.61, view.Camera); AssertThat(view.Particles.LiveCount).IsEqual(0); AssertThat(view.Particles.GetChildCount()).IsEqual(children);
        AssertThat(session.Hash()).IsEqual(hash);
    }

    [TestCase, GodotExceptionMonitor]
    public async Task AuthoredShakeMovesOnlyTheCameraAndReduceMotionClearsIt()
    {
        var content = ContentBridge.LoadRegistry(); var level = ContentBridge.LoadBuiltins(content)[0]; var session = new GameSession(level, 1);
        var view = new WorldView(); using ISceneRunner runner = ISceneRunner.Load(view, autoFree: true); view.Build(level, content, session);
        for (int frame = 0; frame < 120 && !view.ReadyForPlay; frame++) { await runner.AwaitIdleFrame(); }
        view.SetProcess(false); view._Process(0); Vector3 baseline = view.Camera.Position; ulong hash = session.Hash();
        view.ConsumeEvents(new[] { new SimEvent(session.Tick, SimEventKind.CameraShake, 0, default, value: 10, durationTicks: 60) }); view._Process(0);
        float distance = view.Camera.Position.DistanceTo(baseline); AssertThat(distance > 0 && distance <= .25001f).IsTrue();
        view.ReduceMotion = true; view._Process(0); AssertThat(view.Camera.Position.IsEqualApprox(baseline)).IsTrue();
        view.ReduceMotion = false; view._Process(0); AssertThat(view.Camera.Position.IsEqualApprox(baseline)).IsTrue();
        AssertThat(session.Hash()).IsEqual(hash);
    }
}
