using Dungeons.Core.Sim;
using Dungeons.Game.Rendering;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace Dungeons.Game.Tests;

[TestSuite, RequireGodotRuntime]
public sealed class ActorAnimationTests
{
    [TestCase, GodotExceptionMonitor]
    public async Task ComboSnapshotsSelectRealTreeSegmentsWithoutPlayingTheLibrary()
    {
        var content = ContentBridge.LoadRegistry(); var definition = content.Entities["core:hero"];
        var session = new ScenarioBuilder(20, 20).Entity(definition, new(5, 5)).Build().Session;
        var root = new Node3D(); using ISceneRunner runner = ISceneRunner.Load(root, autoFree: true);
        Node3D actor = new ModelLibrary().Actor(definition.Data.Json.GetProperty("render").GetProperty("model").GetString()!, true); root.AddChild(actor);
        AnimationPlayer library = FindPlayer(actor); var profile = new ActorAnimationProfile(definition, library); var motion = new ActorAnimation(actor, library, profile);
        await runner.SimulateFrames(1); var playback = motion.Tree.Get("parameters/states/playback").As<AnimationNodeStateMachinePlayback>();
        var observed = new HashSet<(int, ActorPose)>();
        for (int tick = 0; tick < 100; tick++)
        {
            ActionState previous = session.World.Players[0].Action;
            // Chain after observing a recovery snapshot; buffering in active frames
            // legitimately skips that state in Core.
            InputButtons press = previous.Phase is AttackPhase.Idle or AttackPhase.Recovery ? InputButtons.Attack : InputButtons.None;
            session.Step(new PlayerInput(default, new(1, 0), true, press)); ulong hash = session.Hash();
            Entity hero = session.World.Players[0]; motion.Update(hero, 1, false, tick * (double)GameConstants.Dt, 0);
            ActorPoseFrame frame = ActorPosePolicy.Select(hero, 1, false); observed.Add((hero.Action.Swing, frame.Pose));
            AssertThat(playback.GetCurrentNode().ToString()).IsEqual(ActorAnimationProfile.Names[(int)frame.Pose]);
            AssertThat(library.IsPlaying()).IsFalse(); AssertThat(session.Hash()).IsEqual(hash);
            if (frame.Duration > 0) { AssertThat(MathF.Abs(motion.Rate * frame.Duration - profile.Lengths[(int)frame.Pose]) < .00001f).IsTrue(); }
        }
        for (int combo = 1; combo <= 3; combo++) { foreach (ActorPose phase in new[] { ActorPose.Windup, ActorPose.Active, ActorPose.Recovery }) { AssertThat(observed.Contains((combo, phase))).IsTrue(); } }
        for (int i = 0; i < profile.Machine.GetTransitionCount(); i++) { AssertThat(profile.Machine.GetTransition(i).XfadeTime <= .08).IsTrue(); }
    }

    [TestCase, GodotExceptionMonitor]
    public async Task SharedGraphKeepsPlaybackIndependentAndPoolResetClearsAnInterruptedRoll()
    {
        var content = ContentBridge.LoadRegistry(); var definition = content.Entities["core:hero"];
        var session = new ScenarioBuilder(20, 20).Entity(definition, new(5, 5)).Build().Session;
        var idle = new ScenarioBuilder(20, 20).Entity(definition, new(5, 5)).Build().Session;
        var root = new Node3D(); using ISceneRunner runner = ISceneRunner.Load(root, autoFree: true); var models = new ModelLibrary();
        string path = definition.Data.Json.GetProperty("render").GetProperty("model").GetString()!;
        Node3D first = models.Actor(path, true), second = models.Actor(path, true); root.AddChild(first); root.AddChild(second);
        var profile = new ActorAnimationProfile(definition, FindPlayer(first)); var a = new ActorAnimation(first, FindPlayer(first), profile); var b = new ActorAnimation(second, FindPlayer(second), profile);
        await runner.SimulateFrames(1);
        session.Step(new PlayerInput(new(1, 0))); a.Update(session.World.Players[0], 1, false, .01, 0);
        session.Step(new PlayerInput(default, pressed: InputButtons.Roll)); a.Update(session.World.Players[0], 1, false, .02, 0);
        b.Update(idle.World.Players[0], 1, false, .02, 0);
        var ap = a.Tree.Get("parameters/states/playback").As<AnimationNodeStateMachinePlayback>(); var bp = b.Tree.Get("parameters/states/playback").As<AnimationNodeStateMachinePlayback>();
        AssertThat(ap.GetCurrentNode().ToString()).IsEqual("roll"); AssertThat(bp.GetCurrentNode().ToString()).IsEqual("idle");
        a.Tree.Advance(.1); float position = ap.GetCurrentPlayPosition();
        a.Update(session.World.Players[0], 1, false, .03, .1); AssertThat(ap.GetCurrentPlayPosition()).IsEqual(position);
        AssertThat(b.Tree.Get("parameters/freeze/scale").AsSingle()).IsEqual(1f);
        a.Deactivate(); AssertThat(a.Tree.Active).IsFalse(); a.Reset(2); a.Update(idle.World.Players[0], 1, false, 2, 0);
        AssertThat(a.Tree.Active).IsTrue(); AssertThat(ap.GetCurrentNode().ToString()).IsEqual("idle"); AssertThat(a.Tree.Get("parameters/freeze/scale").AsSingle()).IsEqual(1f);
        AnimationPlayer library = FindPlayer(first); Node3D rig = library.GetNode(library.RootNode).GetNode<Node3D>(library.GetAnimation("roll").TrackGetPath(0).ToString().Split(':')[0]);
        AssertThat(MathF.Abs(rig.Rotation.X) < .0001f).IsTrue();
    }

    private static AnimationPlayer FindPlayer(Node node)
    { if (node is AnimationPlayer player) { return player; } foreach (Node child in node.GetChildren()) { try { return FindPlayer(child); } catch (InvalidOperationException) { } } throw new InvalidOperationException("No animation library."); }
}
