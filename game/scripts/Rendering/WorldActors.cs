using System.Text.Json;
using Dungeons.Core.Content;
using Dungeons.Core.Sim;
using Godot;
using CoreVec2 = Dungeons.Core.Math.Vec2;

namespace Dungeons.Game.Rendering;

public partial class WorldView
{
    private readonly List<ActorBinding> _actors = [];
    private readonly Dictionary<int, ActorBinding> _actorById = [];
    private readonly Dictionary<string, Stack<ActorBinding>> _actorPool = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ActorAnimationProfile> _animationProfiles = new(StringComparer.Ordinal);
    private readonly List<(Node3D node, double expires)> _stuckArrows = [];
    private ShaderMaterial? _shadowMaterial;
    private int _nextStuckArrow;
    /// <summary>Accessibility switch for animation hit-stop and later camera motion.</summary>
    private bool _reduceMotion;
    public bool ReduceMotion { get => _reduceMotion; set { _reduceMotion = value; if (value) { _shake.Clear(); } } }
    /// <summary>Prewarmed reusable views still available for a prototype.</summary>
    public int AvailableActorViews(string definitionId) => _actorPool.TryGetValue(definitionId, out Stack<ActorBinding>? pool) ? pool.Count : 0;
    /// <summary>Active purely decorative terrain arrows.</summary>
    public int StuckArrowCount => _stuckArrows.Count(arrow => arrow.node.Visible);
    private sealed class ActorBinding(int id, string definitionId, string model, Node3D node)
    {
        internal int Id = id;
        internal readonly string Model = model;
        internal readonly string DefinitionId = definitionId;
        internal readonly Node3D Node = node;
        internal readonly Node3D VisualPose = node.GetNode<Node3D>("VisualPose");
        internal AnimationPlayer? Animation;
        internal ActorAnimation? Motion;
        internal readonly List<ShaderMaterial> Materials = [];
        internal readonly List<(Node3D node, Transform3D rest)> Rests = [];
        internal readonly List<Node3D> Sword = [], Bow = [];
        internal string Clip = "";
        internal float Flash;
        internal double HitStopUntil, FlashUntil;
        internal bool DeathDust;
        internal float Emission = 1;
    }
    private void InterpolateActors(float alpha)
    {
        SynchronizeActors();
        for (int i = 0; i < _actors.Count; i++)
        {
            ActorBinding actor = _actors[i]; Entity entity = _session.World.FindEntity(actor.Id)!;
            if (entity.Kind == EntityKind.Exit)
            {
                float emission = BeaconEmission(entity, _session.World.Mission.ExitLocked);
                if (emission != actor.Emission) { actor.Emission = emission; foreach (ShaderMaterial material in actor.Materials) { material.SetShaderParameter("emission_multiplier", emission); } }
            }
            CoreVec2 pos = CoreVec2.Lerp(entity.PrevPos, entity.Pos, alpha);
            float hover = entity.Kind == EntityKind.Pickup ? .12f + .05f * MathF.Sin((float)_visualTime * 3 + actor.Id) : 0;
            actor.Node.Position = new(pos.X, Mathf.Lerp(entity.PrevY, entity.Y, alpha) + hover, pos.Z);
            actor.Node.Rotation = new(0, entity.Kind == EntityKind.Pickup ? (float)_visualTime * Mathf.Tau : Mathf.RotateToward(actor.Node.Rotation.Y, Mathf.Atan2(-entity.Facing.X, -entity.Facing.Z), Mathf.DegToRad(720) * (float)GetProcessDeltaTime()), 0);
            Animate(actor, entity, alpha);
        }
    }
    private ActorBinding AddActor(Entity entity, string model)
    {
        ActorBinding actor;
        if (_actorPool.TryGetValue(entity.Def.Id, out Stack<ActorBinding>? pool) && pool.Count > 0) { actor = pool.Pop(); }
        else { actor = CreateActor(entity.Def, model); }
        actor.Id = entity.Id; actor.Node.Visible = true; actor.Clip = ""; actor.Flash = 0; actor.HitStopUntil = actor.FlashUntil = 0;
        actor.DeathDust = false; actor.Emission = entity.Kind == EntityKind.Exit ? BeaconEmission(entity, _session.World.Mission.ExitLocked) : 1;
        if (actor.Motion is null) { actor.Animation?.Stop(); } foreach (var pose in actor.Rests) { pose.node.Transform = pose.rest; }
        actor.Motion?.Reset(_visualTime);
        actor.Node.Name = "Entity_" + entity.Id; actor.Node.Position = new(entity.Pos.X, entity.Y, entity.Pos.Z);
        actor.Node.Rotation = new(0, Mathf.Atan2(-entity.Facing.X, -entity.Facing.Z), 0);
        foreach (ShaderMaterial material in actor.Materials) { material.SetShaderParameter("dither_alpha", 1f); }
        if (entity.Kind == EntityKind.Exit) { foreach (ShaderMaterial material in actor.Materials) { material.SetShaderParameter("emission_multiplier", actor.Emission); } }
        if (entity.Kind == EntityKind.Gate)
        {
            int width = entity.Properties.GetProperty("width").GetInt32(); bool alongX = entity.Properties.GetProperty("axis").GetString() == "x";
            _actorFades.Add(new(actor, (int)MathF.Floor(entity.Pos.X - (alongX ? width / 2f : 0)), (int)MathF.Floor(entity.Pos.Z - (alongX ? 0 : width / 2f)), alongX ? width : 1, alongX ? 1 : width));
        }
        _actors.Add(actor); _actorById.Add(entity.Id, actor); return actor;
    }
    private ActorBinding CreateActor(EntityDef definition, string model)
    {
            Node3D node = _models.Actor(model, definition.Kind is EntityKind.Player or EntityKind.Enemy or EntityKind.Pickup or EntityKind.Projectile);
            var actor = new ActorBinding(0, definition.Id, model, node); AddChild(node); Collect(node);
            if (definition.Data.Json.TryGetProperty("render", out JsonElement render) && render.TryGetProperty("scale", out JsonElement scale)) { node.Scale = Vector3.One * scale.GetSingle(); }
            if (definition.Kind is EntityKind.Player or EntityKind.Enemy)
            {
                if (actor.Animation is null) { throw new InvalidDataException("Character model has no animation library: " + model); }
                if (!_animationProfiles.TryGetValue(definition.Id, out ActorAnimationProfile? profile))
                { profile = new(definition, actor.Animation); _animationProfiles.Add(definition.Id, profile); }
                actor.Motion = new(node, actor.Animation, profile);
                _shadowMaterial ??= new() { Shader = GD.Load<Shader>("res://shaders/shadow_blob.gdshader") };
                node.AddChild(new MeshInstance3D { Name = "ShadowBlob", Position = new(0, .015f, 0), Rotation = new(-Mathf.Pi / 2, 0, 0),
                    Mesh = new QuadMesh { Size = new(.6f, .6f), Material = _shadowMaterial }, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
            }
            void Collect(Node child)
            {
                if (child != node && child is Node3D spatial) { actor.Rests.Add((spatial, spatial.Transform)); }
                if (child is Node3D weapon)
                { if (child.Name.ToString().StartsWith("sword_", StringComparison.Ordinal)) { actor.Sword.Add(weapon); } if (child.Name.ToString().StartsWith("bow_", StringComparison.Ordinal)) { actor.Bow.Add(weapon); } }
                if (child is AnimationPlayer animation) { actor.Animation = animation; }
                if (child is MeshInstance3D mesh)
                { for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++) { if (mesh.GetSurfaceOverrideMaterial(surface) is ShaderMaterial material && !actor.Materials.Contains(material)) { actor.Materials.Add(material); } } }
                foreach (Node nested in child.GetChildren()) { Collect(nested); }
            }
            return actor;
    }
    private void PrewarmActors(ContentRegistry content)
    {
        foreach (EntityDef definition in content.Entities.Values)
        {
            int count = definition.Brain?.Kind == BrainKind.MeleeChaser ? 16 : definition.Brain?.Kind == BrainKind.Archer ? 8 : definition.Kind == EntityKind.Projectile || definition.Pickup?.Kind == PickupKind.Emerald ? 64 : 0;
            if (count == 0 || !definition.Data.Json.TryGetProperty("render", out JsonElement render) || !render.TryGetProperty("model", out JsonElement model)) { continue; }
            if (!_actorPool.TryGetValue(definition.Id, out Stack<ActorBinding>? pool)) { pool = new(); _actorPool.Add(definition.Id, pool); }
            while (pool.Count < count)
            { ActorBinding actor = CreateActor(definition, model.GetString()!); actor.Node.Name = "Prewarmed_" + definition.Id.Replace(':', '_') + "_" + pool.Count; actor.Node.Visible = false; actor.Motion?.Deactivate(); if (actor.Motion is null) { actor.Animation?.Stop(); } pool.Push(actor); }
        }
        if (!content.Entities.TryGetValue("core:arrow_hero", out EntityDef? arrow) || !arrow.Data.Json.TryGetProperty("render", out JsonElement arrowRender)) { return; }
        string path = arrowRender.GetProperty("model").GetString()!;
        for (int i = 0; i < 64; i++) { Node3D node = _models.Actor(path, true); node.Name = "StuckArrow_" + i; node.Visible = false; AddChild(node); _stuckArrows.Add((node, 0)); }
    }
    private void SynchronizeActors()
    {
        bool lightsChanged = false;
        for (int i = _lights.Count - 1; i >= 0; i--)
        {
            var light = _lights[i]; if (_session.World.FindEntity(light.entityId) is { Alive: true }) { continue; }
            light.light.Visible = false; light.light.QueueFree(); _lightIds.Remove(light.entityId); _lights.RemoveAt(i); lightsChanged = true;
        }
        for (int i = _actors.Count - 1; i >= 0; i--)
        {
            ActorBinding actor = _actors[i]; if (_session.World.FindEntity(actor.Id) is not null) { continue; }
            actor.Node.Visible = false; actor.Node.Name = "Pooled_" + actor.Id; if (actor.Motion is null) { actor.Animation?.Stop(); }
            actor.Motion?.Deactivate();
            _actorById.Remove(actor.Id); _actors.RemoveAt(i);
            if (!_actorPool.TryGetValue(actor.DefinitionId, out Stack<ActorBinding>? pool)) { pool = new(); _actorPool.Add(actor.DefinitionId, pool); }
            pool.Push(actor);
        }
        for (int i = 0; i < _session.World.Entities.Count; i++)
        {
            Entity entity = _session.World.Entities[i];
            if (!entity.Alive && entity.Kind is not (EntityKind.Player or EntityKind.Enemy)) { continue; }
            if (entity.Kind == EntityKind.Light && !_lightIds.Contains(entity.Id)) { AddEntityLight(entity); lightsChanged = true; }
            AddAmbientEmitter(entity);
            if (_actorById.ContainsKey(entity.Id) || _batchedEntityIds.Contains(entity.Id) || !entity.Def.Data.Json.TryGetProperty("render", out JsonElement render) || !render.TryGetProperty("model", out JsonElement model)) { continue; }
            AddActor(entity, model.GetString()!);
        }
        if (lightsChanged) { CullLights(); }
    }
    /// <summary>Presentation reactions consume value events without writing to the simulation.</summary>
    public void ConsumeEvents(ReadOnlySpan<SimEvent> events)
    {
        if (_session is null) { throw new InvalidOperationException("A preview does not consume simulation events."); }
        CombatOverlay.ConsumeEvents(events);
        foreach (SimEvent ev in events)
        {
            if (ev.Kind == SimEventKind.DamageApplied)
            {
                if (_actorById.TryGetValue(ev.EntityId, out ActorBinding? actor)) { actor.FlashUntil = _visualTime + .1; if (!ReduceMotion && ev.MeleeSwing > 0) { actor.HitStopUntil = _visualTime + (ev.MeleeSwing == 3 ? .07 : .04); } }
                if (!ReduceMotion && ev.MeleeSwing > 0 && _actorById.TryGetValue(ev.SourceId, out ActorBinding? attacker)) { attacker.HitStopUntil = _visualTime + (ev.MeleeSwing == 3 ? .07 : .04); }
                Particles.Burst(new(ev.Position.X, ev.Y + .9f, ev.Position.Z), _visualTime, false);
                if (!ReduceMotion && ev.Amount >= 20)
                {
                    for (int slot = 0; slot < _session.World.Players.Count; slot++)
                    { if (_session.World.Players[slot].Id == ev.EntityId) { _shake.Trigger(_visualTime, .2f, .25f); break; } }
                }
            }
            if (ev.Kind == SimEventKind.CameraShake && !ReduceMotion) { _shake.Trigger(_visualTime, ev.Value, ev.DurationTicks * GameConstants.Dt); }
            if (ev.Kind == SimEventKind.GateStateChanged && ev.Amount == (int)GatePhase.Opening && !ReduceMotion) { _shake.Trigger(_visualTime, .08f, .2f); }
            if (ev.Kind == SimEventKind.ChestOpened) { Particles.Burst(new(ev.Position.X, ev.Y + .7f, ev.Position.Z), _visualTime, false, 24); }
            if (ev.Kind == SimEventKind.PickupCollected) { Particles.Burst(new(ev.Position.X, ev.Y + .3f, ev.Position.Z), _visualTime, false, 8); }
            if (ev.Kind == SimEventKind.ProjectileHitTerrain && _stuckArrows.Count > 0)
            {
                int index = _nextStuckArrow++ % _stuckArrows.Count; Node3D node = _stuckArrows[index].node;
                node.Visible = true; node.Position = new(ev.Position.X, ev.Y, ev.Position.Z); node.Rotation = new(0, Mathf.Atan2(-ev.Direction.X, -ev.Direction.Z), 0);
                _stuckArrows[index] = (node, _visualTime + 2);
            }
        }
    }
    private void Animate(ActorBinding actor, Entity entity, float alpha)
    {
        if (entity.Kind is EntityKind.Gate or EntityKind.Chest) { AnimateObject(actor, entity); return; }
        if (entity.Kind is not (EntityKind.Player or EntityKind.Enemy)) { return; }
        actor.Flash = MathF.Max(0, (float)(actor.FlashUntil - _visualTime));
        float sink = ActorPosePolicy.DeathSink(entity, alpha);
        actor.VisualPose.Scale = Vector3.One + new Vector3(.1f, -.1f, .1f) * (ReduceMotion ? 0 : actor.Flash * 10);
        actor.VisualPose.Position = Vector3.Down * (sink * 2);
        if (entity.Alive) { actor.DeathDust = false; }
        else if (sink > 0 && !actor.DeathDust) { actor.DeathDust = true; Particles.Burst(new(entity.Pos.X, entity.Y + .08f, entity.Pos.Z), _visualTime, true, 24); }
        foreach (ShaderMaterial material in actor.Materials) { material.SetShaderParameter("flash", actor.Flash * 10); }
        if (actor.Motion is null) { return; }
        ActionState action = entity.Action;
        bool bow = action.Ranged && action.Phase != AttackPhase.Idle;
        foreach (Node3D part in actor.Sword) { part.Visible = !bow; }
        foreach (Node3D part in actor.Bow) { part.Visible = bow; }
        actor.Motion.Update(entity, alpha, actor.Flash > 0, _visualTime, ReduceMotion ? 0 : actor.HitStopUntil);
    }
    private static void AnimateObject(ActorBinding actor, Entity entity)
    {
        AnimationPlayer? animation = actor.Animation; if (animation is null) { return; }
        WorldObjectState state = entity.WorldObject;
        string clip = entity.Kind == EntityKind.Chest ? state.Opened ? "open" : "close" : state.Gate is GatePhase.Open or GatePhase.Opening ? "open" : "close";
        if (actor.Clip == clip || !animation.HasAnimation(clip)) { return; }
        actor.Clip = clip; animation.SpeedScale = 1; animation.Play(clip, .03);
        if (entity.Kind == EntityKind.Gate && state.StateTicks == 0 || entity.Kind == EntityKind.Chest && !state.Opened)
        { animation.Seek(animation.GetAnimation(clip).Length, true); }
    }
}
