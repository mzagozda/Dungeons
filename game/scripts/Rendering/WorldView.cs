using System.Text.Json;
using Dungeons.Core.Content;
using Dungeons.Core.Level;
using Dungeons.Core.Sim;
using Godot;
using Environment = Godot.Environment;

namespace Dungeons.Game.Rendering;

/// <summary>Presentation-only world. Worker meshes are committed at no more than four chunks per frame.</summary>
public partial class WorldView : Node3D
{
    /// <summary>Immutable registry bound to the current view, shared with data-driven HUD reactions.</summary>
    public ContentRegistry Registry => _content ?? throw new InvalidOperationException("Build a view first.");
    private readonly List<(int entityId, OmniLight3D light, float energy, bool flicker)> _lights = [];
    private readonly HashSet<int> _lightIds = [], _batchedEntityIds = [];
    private readonly List<FadeInstance> _fades = [];
    private readonly List<StaticInstance> _staticInstances = [];
    private readonly List<ActorFade> _actorFades = [];
    private readonly ModelLibrary _models = new();
    private readonly int[] _closestLights = new int[8];
    private readonly float[] _lightDistances = new float[8];
    private readonly Dungeons.Core.Math.IntVec2[] _occluderCells = new Dungeons.Core.Math.IntVec2[6];
    private GameSession _session = null!;
    private LevelDocument _document = null!;
    private RenderMaterials _materials = null!;
    private int _frames;
    private Vector3 _focus;
    private Vector3 _renderFocus;
    private double _visualTime;
    private readonly CameraShakeState _shake = new();
    public WorldParticles Particles { get; private set; } = null!;
    private sealed class FadeInstance(MultiMesh mesh, int index, int x, int z, int chunk = -1, StaticInstance? placement = null)
    { internal MultiMesh Mesh = mesh; internal int Index = index, X = x, Z = z, Chunk = chunk; internal float Alpha = 1; internal readonly StaticInstance? Placement = placement; }
    private sealed class StaticInstance(int entityId, MultiMesh mesh, int index)
    { internal readonly int EntityId = entityId; internal readonly MultiMesh Mesh = mesh; internal readonly int Index = index; internal bool Hidden; internal float Emission = 1, Alpha = 1; }
    private sealed class ActorFade(ActorBinding actor, int x, int z, int width, int depth)
    { internal readonly ActorBinding Actor = actor; internal readonly int EntityId = actor.Id; internal readonly int X = x, Z = z, Width = width, Depth = depth; internal float Alpha = 1; }
    /// <summary>Active camera, also used by input projection.</summary>
    public Camera3D Camera { get; private set; } = null!;
    /// <summary>Presentation-only combat feedback layer.</summary>
    public CombatOverlay CombatOverlay { get; private set; } = null!;
    /// <summary>Committed terrain chunks.</summary>
    public int ChunkCount { get; private set; }
    /// <summary>Separate water mesh count.</summary>
    public int WaterMeshCount { get; private set; }
    /// <summary>All chunk resources are ready for the loading screen to close.</summary>
    public bool ReadyForPlay => _chunks.Length > 0 && _pendingChunks == 0;
    /// <summary>Number of enabled local lights, bounded to eight.</summary>
    public int EnabledLightCount { get; private set; }
    /// <summary>Creates a new scene from validated source and authoritative runtime entities.</summary>
    public void Build(LevelDocument document, ContentRegistry content, GameSession session)
    {
        if (session is null) { throw new ArgumentNullException(nameof(session)); }
        PrepareView(document, content, session);
        BuildRuntime(content, session);
    }
    private void BuildRuntime(ContentRegistry content, GameSession session)
    {
        CombatOverlay = new(); AddChild(CombatOverlay); CombatOverlay.Build(session, Camera);
        Particles = new(); AddChild(Particles);
        PrewarmActors(content);
        var batches = new Dictionary<(int x, int z, string model), List<(Vector3 pos, float angle, Entity? entity, float scale)>>() ;
        for (int i = 0; i < session.World.Entities.Count; i++)
        {
            Entity entity = session.World.Entities[i];
            if (!StaticProp(entity) || !entity.Def.Data.Json.TryGetProperty("render", out JsonElement render) || !render.TryGetProperty("model", out JsonElement model)) { continue; }
            var key = ((int)entity.Pos.X / 16, (int)entity.Pos.Z / 16, model.GetString()!);
            if (!batches.TryGetValue(key, out var positions)) { positions = []; batches.Add(key, positions); }
            positions.Add((new(entity.Pos.X, entity.Y, entity.Pos.Z), Mathf.Atan2(-entity.Facing.X, -entity.Facing.Z), entity, render.TryGetProperty("scale", out JsonElement scale) ? scale.GetSingle() : 1));
            _batchedEntityIds.Add(entity.Id);
            AddChild(new Node3D { Name = "Entity_" + entity.Id, Position = new(entity.Pos.X, entity.Y, entity.Pos.Z) });
        }
        int batchId = 0;
        foreach (var batch in batches)
        {
            ArrayMesh mesh = _models.StaticMesh(batch.Key.model);
            var multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, UseCustomData = true, Mesh = mesh, InstanceCount = batch.Value.Count };
            for (int i = 0; i < batch.Value.Count; i++)
            {
                var placement = batch.Value[i]; multimesh.SetInstanceTransform(i, new(new Basis(Vector3.Up, placement.angle).Scaled(Vector3.One * placement.scale), placement.pos));
                multimesh.SetInstanceCustomData(i, new Color(1, 1, 1, 1));
                multimesh.SetInstanceColor(i, Colors.White);
                StaticInstance? instance = null;
                if (placement.entity is not null)
                {
                    instance = new(placement.entity.Id, multimesh, i); _staticInstances.Add(instance);
                    if (placement.entity.Kind == EntityKind.Exit) { instance.Emission = BeaconEmission(placement.entity, session.World.Mission.ExitLocked); multimesh.SetInstanceCustomData(i, new(instance.Emission, 1, 1, 1)); }
                }
                if (mesh.GetAabb().Size.Y > 1.2f) { _fades.Add(new(multimesh, i, (int)placement.pos.X, (int)placement.pos.Z, placement: instance)); }
            }
            AddChild(new MultiMeshInstance3D { Name = "Models_" + batch.Key.x + "_" + batch.Key.z + "_" + batchId++, Multimesh = multimesh });
        }
        for (int i = 0; i < session.World.Entities.Count; i++)
        {
            Entity entity = session.World.Entities[i]; JsonElement def = entity.Def.Data.Json;
            if (!StaticProp(entity) && def.TryGetProperty("render", out JsonElement render) && render.TryGetProperty("model", out JsonElement model))
            {
                Node3D actor = AddActor(entity, model.GetString()!).Node;
                if (render.TryGetProperty("scale", out JsonElement scale)) { actor.Scale = Vector3.One * scale.GetSingle(); }
            }
            AddEntityLight(entity);
            AddAmbientEmitter(entity);
        }
        Dungeons.Core.Math.Vec2 focus = session.World.CameraFocus; _renderFocus = new(focus.X, session.World.Players.Count > 0 ? session.World.Players[0].Y : 0, focus.Z); _focus = _renderFocus; UpdateCamera(0); CullLights();
    }
    public override void _Process(double delta)
    {
        if (_document is null) { return; }
        int committed = CommitReadyChunks();
#if DEBUG
        if (committed > 0 && ReadyForPlay) { GD.Print("MATERIAL_AUDIT=PASS materials=" + MaterialAudit.Check(this)); }
#endif
        _visualTime += delta; _frames++; UpdateCamera((float)delta); UpdateOccluders((float)delta);
        if (_session is null)
        {
            if (_frames % 10 == 0) { CullLights(); }
            for (int i = 0; i < _lights.Count; i++) { var light = _lights[i]; light.light.LightEnergy = light.energy * (light.flicker ? 1 + .1f * MathF.Sin((float)_visualTime * Mathf.Tau * 8 + i) : 1); }
            return;
        }
        AdvanceAmbient(delta); Particles.Advance(_visualTime, Camera);
        for (int i = 0; i < _stuckArrows.Count; i++) { if (_stuckArrows[i].expires <= _visualTime) { _stuckArrows[i].node.Visible = false; } }
        if (_frames % 10 == 0) { CullLights(); }
        for (int i = 0; i < _staticInstances.Count; i++)
        {
            StaticInstance instance = _staticInstances[i];
            Entity? entity = _session.World.FindEntity(instance.EntityId);
            if (!instance.Hidden && entity is not { Alive: true })
            {
                instance.Mesh.SetInstanceTransform(instance.Index, new(Basis.Identity.Scaled(Vector3.Zero), Vector3.Zero)); instance.Hidden = true;
                GetNodeOrNull<Node3D>("Entity_" + instance.EntityId)?.QueueFree();
            }
            if (!instance.Hidden && entity is { Kind: EntityKind.Exit })
            {
                float emission = BeaconEmission(entity, _session.World.Mission.ExitLocked);
                if (emission != instance.Emission) { instance.Emission = emission; instance.Mesh.SetInstanceCustomData(instance.Index, new(emission, 1, 1, instance.Alpha)); }
            }
        }
        for (int i = 0; i < _lights.Count; i++) { var light = _lights[i]; light.light.LightEnergy = light.energy * (light.flicker ? 1 + 0.1f * MathF.Sin((float)_visualTime * Mathf.Tau * 8 + i) : 1); }
    }
    /// <summary>Interpolates entity transforms; never writes to Core.</summary>
    public void Interpolate(float alpha)
    {
        if (_session is null) { return; }
        InterpolateActors(alpha);
        CombatOverlay.Advance(0, alpha);
        Vector3 centroid = default; int alive = 0;
        for (int i = 0; i < _session.World.Players.Count; i++)
        {
            Entity hero = _session.World.Players[i]; if (!hero.Alive) { continue; }
            Dungeons.Core.Math.Vec2 position = Dungeons.Core.Math.Vec2.Lerp(hero.PrevPos, hero.Pos, alpha);
            centroid += new Vector3(position.X, Mathf.Lerp(hero.PrevY, hero.Y, alpha), position.Z); alive++;
        }
        if (alive > 0) { _renderFocus = centroid / alive; }
    }
    private static bool StaticProp(Entity entity) => entity.Kind is EntityKind.Prop or EntityKind.Light or EntityKind.Checkpoint or EntityKind.Exit;
    private void AddEntityLight(Entity entity) => AddEntityLight(entity.Id, entity.Kind, new(entity.Pos.X, entity.Y, entity.Pos.Z), entity.Properties);
    private void AddEntityLight(int id, EntityKind kind, Vector3 position, JsonElement props)
    {
        if (kind != EntityKind.Light || !_lightIds.Add(id)) { return; }
        var light = new OmniLight3D { Name = "Light_" + id, Position = position + Vector3.Up * 1.4f, ShadowEnabled = false,
            OmniRange = props.GetProperty("radius").GetSingle(), LightColor = new Color(props.GetProperty("color").GetString()!), LightEnergy = props.GetProperty("energy").GetSingle() };
        AddChild(light); _lights.Add((id, light, light.LightEnergy, props.GetProperty("flicker").GetBoolean()));
    }
    private void ApplyEnvironment(LevelEnvironment source)
    {
        JsonElement data = source.Json, skyData = data.GetProperty("sky"), sunData = data.GetProperty("sun"), ambient = data.GetProperty("ambient"), fog = data.GetProperty("fog"), glow = data.GetProperty("glow");
        Color ColorOf(JsonElement block, string key) => new(block.GetProperty(key).GetString()!);
        var skyMaterial = new ProceduralSkyMaterial { SkyTopColor = ColorOf(skyData, "top"), SkyHorizonColor = ColorOf(skyData, "horizon"), GroundBottomColor = ColorOf(skyData, "ground"), GroundHorizonColor = ColorOf(skyData, "horizon") };
        var environment = new Environment { BackgroundMode = Environment.BGMode.Sky, Sky = new Sky { SkyMaterial = skyMaterial }, AmbientLightSource = Environment.AmbientSource.Color,
            AmbientLightColor = ColorOf(ambient, "color"), AmbientLightEnergy = ambient.GetProperty("energy").GetSingle(), TonemapMode = Environment.ToneMapper.Aces,
            FogEnabled = fog.GetProperty("enabled").GetBoolean(), FogMode = Environment.FogModeEnum.Depth, FogLightColor = ColorOf(fog, "color"), FogDensity = fog.GetProperty("density").GetSingle(), FogDepthBegin = fog.GetProperty("startDistance").GetSingle(), FogDepthEnd = 70,
            GlowEnabled = glow.GetProperty("enabled").GetBoolean() && GraphicsRuntime.Instance?.CompatibilityRenderer != true, GlowIntensity = glow.GetProperty("intensity").GetSingle(), GlowHdrThreshold = glow.GetProperty("threshold").GetSingle(), GlowBlendMode = Environment.GlowBlendModeEnum.Softlight };
        WorldEnvironment? environmentNode = GetChildren().OfType<WorldEnvironment>().SingleOrDefault(); bool initial = environmentNode is null;
        if (environmentNode is null) { environmentNode = new() { Name = "WorldEnvironment" }; AddChild(environmentNode); }
        environmentNode.Environment = environment;
        _sun = GetChildren().OfType<DirectionalLight3D>().SingleOrDefault()!;
        if (_sun is null) { _sun = new() { Name = "Sun" }; AddChild(_sun); }
        _sun.RotationDegrees = new(sunData.GetProperty("pitchDeg").GetSingle(), -sunData.GetProperty("yawDeg").GetSingle(), 0);
        _sun.LightColor = ColorOf(sunData, "color"); _sun.LightEnergy = sunData.GetProperty("energy").GetSingle();
        _sun.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Orthogonal; _sun.DirectionalShadowMaxDistance = 45; _sun.ShadowBias = .03f; _sun.ShadowNormalBias = 1;
        Camera = GetChildren().OfType<Camera3D>().SingleOrDefault()!;
        if (Camera is null) { Camera = new() { Name = "WorldCamera", Current = true, Near = 1, Far = 120 }; AddChild(Camera); }
        Camera.Fov = source.CameraFov;
        _environment = environment; _authoredGlow = glow.GetProperty("enabled").GetBoolean(); _authoredShadows = sunData.GetProperty("shadows").GetBoolean();
        _environmentJson = source.Json.GetRawText();
        if (GraphicsRuntime.Instance is GraphicsRuntime graphics)
        {
            if (_session is not null && initial) { graphics.Attach(this); }
            else { ApplyGraphics(graphics.Options, graphics.Adaptive.Scale, graphics.CompatibilityRenderer); }
        }
        else
        {
            bool compatibility = RenderingServer.GetCurrentRenderingMethod() == "gl_compatibility", mobile = OS.HasFeature("mobile");
            ApplyGraphics(GraphicsOptions.ForPreset(compatibility ? QualityPreset.Low : mobile ? QualityPreset.Medium : QualityPreset.High, mobile), 1, compatibility);
        }
        _materials.Water.SetShaderParameter("color", new Color(data.GetProperty("waterColor").GetString()!, 0.7f));
        _materials.Foliage.SetShaderParameter("ambient_tint", ColorOf(ambient, "color") * ambient.GetProperty("energy").GetSingle());
    }
    private void UpdateCamera(float delta)
    {
        Vector3 target = _renderFocus;
        if (_session is not null)
        {
            for (int i = 0; i < _session.World.Players.Count; i++)
            { Entity hero = _session.World.Players[i]; if (hero.Alive) { target += new Vector3(hero.Facing.X, 0, hero.Facing.Z) * _document.Environment.CameraLookAhead; break; } }
        }
        JsonElement camera = _document.Environment.Json.GetProperty("camera");
        if (camera.TryGetProperty("bounds", out JsonElement bounds))
        {
            target.X = Mathf.Clamp(target.X, bounds.GetProperty("x").GetSingle(), bounds.GetProperty("x").GetSingle() + bounds.GetProperty("w").GetSingle());
            target.Z = Mathf.Clamp(target.Z, bounds.GetProperty("z").GetSingle(), bounds.GetProperty("z").GetSingle() + bounds.GetProperty("h").GetSingle());
        }
        else
        {
            float insetX = MathF.Min(4, _document.Width / 2f), insetZ = MathF.Min(4, _document.Height / 2f);
            target.X = Mathf.Clamp(target.X, insetX, _document.Width - insetX); target.Z = Mathf.Clamp(target.Z, insetZ, _document.Height - insetZ);
        }
        _focus = delta == 0 ? target : _focus.Lerp(target, 1 - MathF.Exp(-8 * delta));
        float yaw = Mathf.DegToRad(_document.Environment.CameraYaw), pitch = Mathf.DegToRad(-_document.Environment.CameraPitch);
        Vector3 direction = new(-MathF.Sin(yaw) * MathF.Cos(pitch), MathF.Sin(pitch), MathF.Cos(yaw) * MathF.Cos(pitch));
        Camera.Position = _focus + direction * _document.Environment.CameraDistance; Camera.LookAt(_focus);
        if (!ReduceMotion) { var offset = _shake.Offset(_visualTime); Camera.Position += Camera.GlobalBasis.X * offset.X + Camera.GlobalBasis.Y * offset.Y; }
    }
    private void CullLights()
    {
        Array.Fill(_closestLights, -1); Array.Fill(_lightDistances, float.PositiveInfinity);
        for (int i = 0; i < _lights.Count; i++)
        {
            float distance = _lights[i].light.Position.DistanceSquaredTo(_focus);
            for (int slot = 0; slot < _lightLimit; slot++)
            {
                if (distance >= _lightDistances[slot]) { continue; }
                for (int k = _lightLimit - 1; k > slot; k--) { _lightDistances[k] = _lightDistances[k - 1]; _closestLights[k] = _closestLights[k - 1]; }
                _lightDistances[slot] = distance; _closestLights[slot] = i; break;
            }
        }
        EnabledLightCount = 0;
        for (int i = 0; i < _lights.Count; i++) { bool enabled = Array.IndexOf(_closestLights, i) >= 0; _lights[i].light.Visible = enabled; if (enabled) { EnabledLightCount++; } }
    }
    private void UpdateOccluders(float delta)
    {
        if (_session is null || _session.World.Players.Count == 0) { return; }
        Entity hero = _session.World.Players[0]; Vector2 direction = new(Camera.Position.X - hero.Pos.X, Camera.Position.Z - hero.Pos.Z); direction = direction.Normalized();
        int x = (int)MathF.Floor(hero.Pos.X), z = (int)MathF.Floor(hero.Pos.Z), sx = Math.Sign(direction.X), sz = Math.Sign(direction.Y);
        float dx = direction.X == 0 ? float.PositiveInfinity : MathF.Abs(1 / direction.X), dz = direction.Y == 0 ? float.PositiveInfinity : MathF.Abs(1 / direction.Y);
        float tx = direction.X == 0 ? float.PositiveInfinity : (sx > 0 ? x + 1 - hero.Pos.X : hero.Pos.X - x) * dx;
        float tz = direction.Y == 0 ? float.PositiveInfinity : (sz > 0 ? z + 1 - hero.Pos.Z : hero.Pos.Z - z) * dz;
        for (int i = 0; i < 6; i++)
        { if (tx < tz) { x += sx; tx += dx; } else { z += sz; tz += dz; } _occluderCells[i] = new(x, z); }
        float amount = 0.65f * Mathf.Clamp(delta / 0.15f, 0, 1);
        for (int i = 0; i < _fades.Count; i++)
        {
            FadeInstance instance = _fades[i]; bool blocks = false;
            for (int c = 0; c < 6; c++) { if (_occluderCells[c].X == instance.X && _occluderCells[c].Z == instance.Z) { blocks = true; break; } }
            float alpha = Mathf.MoveToward(instance.Alpha, blocks ? 0.35f : 1, amount);
            if (alpha == instance.Alpha) { continue; } instance.Alpha = alpha;
            if (instance.Placement is StaticInstance placement) { placement.Alpha = alpha; }
            instance.Mesh.SetInstanceCustomData(instance.Index, new Color(instance.Placement?.Emission ?? 1, 1, 1, alpha));
        }
        for (int i = 0; i < _actorFades.Count; i++)
        {
            ActorFade actor = _actorFades[i];
            if (actor.Actor.Id != actor.EntityId || !actor.Actor.Node.Visible) { _actorFades.RemoveAt(i--); continue; }
            bool blocks = false;
            for (int c = 0; c < 6; c++) { var cell = _occluderCells[c]; if (cell.X >= actor.X && cell.X < actor.X + actor.Width && cell.Z >= actor.Z && cell.Z < actor.Z + actor.Depth) { blocks = true; break; } }
            float alpha = Mathf.MoveToward(actor.Alpha, blocks ? 0.35f : 1, amount);
            if (alpha == actor.Alpha) { continue; } actor.Alpha = alpha;
            for (int s = 0; s < actor.Actor.Materials.Count; s++) { actor.Actor.Materials[s].SetShaderParameter("dither_alpha", alpha); }
        }
    }
}
