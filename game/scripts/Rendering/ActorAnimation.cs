using System.Text.Json;
using Dungeons.Core.Content;
using Dungeons.Core.Sim;
using Godot;

namespace Dungeons.Game.Rendering;

/// <summary>Read-only graph shared by a prototype; every AnimationTree owns its playback/parameters.</summary>
public sealed class ActorAnimationProfile
{
    public static readonly string[] Names = ["idle", "walk", "attack_windup", "attack_active", "attack_recovery", "aim", "fire", "roll", "hit", "stagger", "death", "emerge"];
    public AnimationNodeBlendTree Root { get; } = new();
    public AnimationNodeStateMachine Machine { get; } = new();
    public float[] Lengths { get; } = new float[Names.Length];
    internal StringName[] SpeedPaths { get; } = Names.Select(name => new StringName("parameters/states/" + name + "/speed/scale")).ToArray();
    internal StringName[] SeekPaths { get; } = Names.Select(name => new StringName("parameters/states/" + name + "/seek/seek_request")).ToArray();
    public ActorAnimationProfile(EntityDef definition, AnimationPlayer player)
    {
        JsonElement clips = definition.Data.Json.GetProperty("render").GetProperty("animations");
        string Clip(string key, string fallback = "idle") => clips.TryGetProperty(key, out var name) && player.HasAnimation(name.GetString()!) ? name.GetString()! : clips.GetProperty(fallback).GetString()!;
        string attack = Clip("attack");
        float attackLength = (float)player.GetAnimation(attack).Length;
        var baseline = definition.Weapons.Melee?.Swings[0];
        float total = baseline is { } first ? first.Windup + first.Active + first.Recovery : 1;
        float windup = baseline is { } wind ? attackLength * wind.Windup / total : attackLength / 3;
        float active = baseline is { } hit ? attackLength * hit.Active / total : attackLength / 3;
        for (int i = 0; i < Names.Length; i++)
        {
            ActorPose pose = (ActorPose)i;
            string clip = pose is ActorPose.Windup or ActorPose.Active or ActorPose.Recovery ? attack : Clip(pose == ActorPose.Stagger ? "hit" : Names[i]);
            float offset = pose == ActorPose.Active ? windup : pose == ActorPose.Recovery ? windup + active : 0;
            float length = pose == ActorPose.Windup ? windup : pose == ActorPose.Active ? active : pose == ActorPose.Recovery ? attackLength - offset : (float)player.GetAnimation(clip).Length;
            Lengths[i] = length;
            var animation = new AnimationNodeAnimation { Animation = clip, UseCustomTimeline = true, TimelineLength = length, StartOffset = offset, StretchTimeScale = false, LoopMode = pose is ActorPose.Idle or ActorPose.Walk ? Animation.LoopModeEnum.Linear : Animation.LoopModeEnum.None };
            var state = new AnimationNodeBlendTree(); state.AddNode("clip", animation); state.AddNode("seek", new AnimationNodeTimeSeek { ExplicitElapse = false }); state.AddNode("speed", new AnimationNodeTimeScale());
            state.ConnectNode("seek", 0, "clip"); state.ConnectNode("speed", 0, "seek"); state.ConnectNode("output", 0, "speed"); Machine.AddNode(Names[i], state);
        }
        for (int from = 0; from < Names.Length; from++)
        { for (int to = 0; to < Names.Length; to++) { if (from != to) { Machine.AddTransition(Names[from], Names[to], new AnimationNodeStateMachineTransition { XfadeTime = ActorPosePolicy.TransitionSeconds }); } } }
        Root.AddNode("states", Machine); Root.AddNode("freeze", new AnimationNodeTimeScale()); Root.ConnectNode("freeze", 0, "states"); Root.ConnectNode("output", 0, "freeze");
    }
}

/// <summary>Manual AnimationTree driver. AnimationPlayer remains a clip library, never a second player.</summary>
public sealed class ActorAnimation
{
    private static readonly StringName PlaybackPath = "parameters/states/playback", FreezePath = "parameters/freeze/scale";
    private readonly ActorAnimationProfile _profile;
    private readonly AnimationNodeStateMachinePlayback _playback;
    private int _cycle = -1;
    private float _previousAge;
    private double _lastTime;
    public AnimationTree Tree { get; }
    public ActorPose Pose { get; private set; }
    public float Rate { get; private set; } = 1;
    public ActorAnimation(Node3D owner, AnimationPlayer player, ActorAnimationProfile profile)
    {
        _profile = profile; player.Stop(); player.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;
        Tree = new() { Name = "MotionTree", TreeRoot = profile.Root, CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual };
        owner.AddChild(Tree); Tree.AnimPlayer = Tree.GetPathTo(player); Tree.RootNode = Tree.GetPathTo(player.GetNode(player.RootNode));
        Tree.Active = true; _playback = Tree.Get(PlaybackPath).As<AnimationNodeStateMachinePlayback>(); Reset(0);
    }
    public void Reset(double time)
    {
        Pose = ActorPose.Idle; _cycle = -1; _previousAge = 0; _lastTime = time; Rate = 1;
        Tree.Active = true; Tree.Set(FreezePath, 1f); _playback.Start("idle"); Tree.Advance(0);
    }
    public void Deactivate() { Tree.Active = false; }
    public void Update(Entity entity, float alpha, bool hit, double time, double hitStopUntil)
    {
        double delta = System.Math.Max(0, time - _lastTime), frozen = System.Math.Clamp(hitStopUntil - _lastTime, 0, delta); _lastTime = time;
        bool stopped = time < hitStopUntil; Tree.Set(FreezePath, stopped ? 0f : 1f);
        if (stopped) { Tree.Advance(delta); return; }
        ActorPoseFrame frame = ActorPosePolicy.Select(entity, alpha, hit); int index = (int)frame.Pose;
        Rate = frame.Duration > 0 ? _profile.Lengths[index] / frame.Duration : frame.Rate;
        Tree.Set(_profile.SpeedPaths[index], Rate);
        bool restart = frame.Cycle != _cycle || frame.Age + GameConstants.Dt < _previousAge;
        bool entered = Pose != frame.Pose || restart;
        if (entered)
        {
            // A newer Core phase must not queue behind an unfinished visual fade.
            // Fast-forwarded snapshots and rapid interrupts can skip whole render intervals.
            if (Pose == frame.Pose || !_playback.GetFadingFromNode().IsEmpty) { _playback.Start(ActorAnimationProfile.Names[index]); }
            else { _playback.Travel(ActorAnimationProfile.Names[index]); }
            Pose = frame.Pose; _cycle = frame.Cycle;
            Tree.Advance(0); Tree.Set(_profile.SeekPaths[index], System.Math.Min(frame.Age * Rate, _profile.Lengths[index])); Tree.Advance(0);
        }
        // The seek already includes this snapshot's elapsed tick fraction.
        // Advancing it again would make every phase begin a render frame early.
        _previousAge = frame.Age; if (!entered) { Tree.Advance(delta - frozen); }
    }
}
