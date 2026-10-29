using Dungeons.Core.Content;
using Dungeons.Core.Math;
using Dungeons.Core.Level;
using Dungeons.Core.Util;
using System.Text.Json;

namespace Dungeons.Core.Sim;

/// <summary>Simulation families, including future v1 components.</summary>
public enum EntityKind
{
    /// <summary>Controllable hero.</summary>
    Player,
    /// <summary>Monster.</summary>
    Enemy,
    /// <summary>Moving shot.</summary>
    Projectile,
    /// <summary>Collectible.</summary>
    Pickup,
    /// <summary>World prop.</summary>
    Prop,
    /// <summary>Gate footprint.</summary>
    Gate,
    /// <summary>Loot chest.</summary>
    Chest,
    /// <summary>Respawn banner.</summary>
    Checkpoint,
    /// <summary>Mission exit.</summary>
    Exit,
    /// <summary>Spawn location.</summary>
    SpawnMarker,
    /// <summary>Presentation light.</summary>
    Light,
    /// <summary>Authored logic point.</summary>
    Marker,
}

/// <summary>Read-only health snapshot returned to presentation.</summary>
public struct Health
{
    /// <summary>Maximum health.</summary>
    public int Max { get; internal set; }
    /// <summary>Current health.</summary>
    public int Current { get; internal set; }
    /// <summary>Armour at spawn.</summary>
    public int Armor { get; internal set; }
    /// <summary>Remaining invulnerability.</summary>
    public int InvulnTicks { get; internal set; }
}

/// <summary>Named counters support artifacts/souls without changing system fields later.</summary>
public struct Inventory
{
    /// <summary>Collected emeralds.</summary>
    public int Emeralds { get; internal set; }
    /// <summary>Available arrows.</summary>
    public int Arrows { get; internal set; }
    /// <summary>Reserved souls counter.</summary>
    public int Souls { get; internal set; }
}

/// <summary>Runtime entity with Core-only mutation; ids are never reused. Discard references after removal because storage is pooled.</summary>
public sealed class Entity
{
    internal Health HealthData;
    internal Inventory InventoryData;
    internal ActionState ActionData;
    internal ProjectileState ProjectileData;
    internal AiState AiData;
    internal WorldObjectState ObjectData;
    internal ObjectRules ObjectRules;
    internal bool PooledProjectile;
    internal bool PooledEntity;
    internal bool MissionRegistered;
    internal int SpawnOwnerId;
    internal IntVec2[] PathBuffer;
    internal int PathCount;
    internal byte FootstepTicks;
    internal Entity(int id, EntityPlacement placement, int spawnTick, float y) : this(id, placement.Definition, placement.Position, placement.Id, spawnTick, y)
    {
        Facing = placement.Facing; Tags = placement.Tags; Properties = placement.Properties;
        ObjectRules = new(Kind, Properties); ObjectData.Gate = ObjectRules.InitiallyOpen ? GatePhase.Open : GatePhase.Closed;
        if (Kind == EntityKind.Projectile && Def.Projectile is ProjectileSpec projectile)
        { Y = PrevY = y + projectile.Height; ProjectileData.Velocity = Facing * projectile.Speed; }
        PropertiesHash = RuntimeProperties.Hash(Kind, Properties);
        if (placement.Definition.Pickup?.Kind == PickupKind.Emerald && Properties.TryGetProperty("amount", out JsonElement amount) && amount.TryGetInt32(out int value)) { PickupAmount = value; }
        if (Def.Brain is not null && Properties.TryGetProperty("patrolRadius", out JsonElement patrol)) { AiData.PatrolRadius = patrol.GetSingle(); }
    }
    internal Entity(int id, EntityDef def, Vec2 position, string? levelId, int spawnTick, float y)
    { PathBuffer = def.Kind is EntityKind.Player or EntityKind.Enemy ? new IntVec2[GameConstants.MaxPathWaypoints] : Array.Empty<IntVec2>(); Reset(id, def, position, levelId, spawnTick, y); }
    internal void Reset(int id, EntityDef def, Vec2 position, string? levelId, int spawnTick, float y)
    {
        Id = id; Def = def; Kind = def.Kind; Stats = def.Stats; LevelId = levelId;
        Pos = PrevPos = position; Y = PrevY = y; Facing = Vec2.North; Alive = true;
        Radius = def.Radius; Solid = def.Solid; SpawnTick = spawnTick;
        HealthData = new Health { Max = Stats.Hp, Current = Stats.Hp, Armor = Stats.Armor };
        InventoryData = new Inventory { Arrows = Kind == EntityKind.Player ? def.Controls.ArrowsStart : 0 };
        PickupAmount = def.DefaultPickupAmount;
        Tags = Array.Empty<string>(); Properties = def.DefaultProperties; PropertiesHash = def.DefaultPropertiesHash;
        ObjectRules = def.DefaultObjectRules; ObjectData = new() { Gate = ObjectRules.InitiallyOpen ? GatePhase.Open : GatePhase.Closed, ChangedTick = -1 };
        ActionData = default; ProjectileData = default; Vel = Knockback = Vec2.Zero; LifetimeTicks = -1; Experience = PickupArmTicks = PathCount = 0; PathReachedGoal = false;
        PathTruncated = false;
        MissionRegistered = false; SpawnOwnerId = 0; FootstepTicks = 0;
        AiData = new() { SpawnPosition = position, ProgressPosition = position, WanderPosition = position, LastSeenTick = -1, LastAlertTick = -1, PatrolRadius = def.Brain?.PatrolRadius ?? 0 };
        if (Kind == EntityKind.Projectile && def.Projectile is ProjectileSpec projectile)
        { ProjectileData = new() { Faction = def.Faction, DamageMultiplier = Stats.DamageMultiplier, Velocity = Facing * projectile.Speed }; LifetimeTicks = projectile.LifetimeTicks; }
    }
    /// <summary>Stable ascending runtime identifier.</summary>
    public int Id { get; private set; }
    /// <summary>Optional authored instance id.</summary>
    public string? LevelId { get; private set; }
    /// <summary>Immutable source prototype.</summary>
    public EntityDef Def { get; private set; } = null!;
    /// <summary>Immutable authored group membership.</summary>
    public IReadOnlyList<string> Tags { get; private set; } = Array.Empty<string>();
    internal void SetSpawnTags(IReadOnlyList<string> tags) => Tags = tags;
    internal void ApplySpawnSettings(SpawnSettings settings)
    {
        Properties = settings.Properties; PropertiesHash = settings.PropertiesHash; Tags = settings.Tags;
        ObjectRules = settings.Rules; ObjectData.Gate = ObjectRules.InitiallyOpen ? GatePhase.Open : GatePhase.Closed;
        PickupAmount = settings.PickupAmount; AiData.PatrolRadius = settings.PatrolRadius;
    }
    /// <summary>Validated spawn properties; mutable gameplay components remain Core-owned.</summary>
    public JsonElement Properties { get; private set; } = DefinitionData.Empty.Json;
    internal ulong PropertiesHash { get; private set; }
    /// <summary>Runtime component family.</summary>
    public EntityKind Kind { get; private set; }
    /// <summary>Per-instance spawn statistics.</summary>
    public StatBlock Stats { get; internal set; }
    /// <summary>Current XZ position.</summary>
    public Vec2 Pos { get; internal set; }
    /// <summary>Position before the most recent tick, for rendering interpolation.</summary>
    public Vec2 PrevPos { get; internal set; }
    /// <summary>Current feet height.</summary>
    public float Y { get; internal set; }
    /// <summary>Feet height before the most recent tick.</summary>
    public float PrevY { get; internal set; }
    /// <summary>Unit facing, initially north.</summary>
    public Vec2 Facing { get; internal set; }
    /// <summary>Intended ground velocity after collision.</summary>
    public Vec2 Vel { get; internal set; }
    /// <summary>Decaying impulse.</summary>
    public Vec2 Knockback { get; internal set; }
    /// <summary>Collision circle radius.</summary>
    public float Radius { get; private set; }
    /// <summary>Static blocking flag.</summary>
    public bool Solid { get; internal set; }
    /// <summary>Whether the entity participates in living systems.</summary>
    public bool Alive { get; internal set; }
    /// <summary>Health component snapshot.</summary>
    public Health Health => HealthData;
    /// <summary>Named inventory component snapshot.</summary>
    public Inventory Inventory => InventoryData;
    /// <summary>Current combat, roll and cooldown state.</summary>
    public ActionState Action => ActionData;
    /// <summary>Launched projectile snapshot. References to removed entities must be discarded.</summary>
    public ProjectileState Projectile => ProjectileData;
    /// <summary>Enemy behaviour and navigation snapshot.</summary>
    public AiState Ai => AiData;
    /// <summary>World object lifecycle snapshot; only Core systems mutate it.</summary>
    public WorldObjectState WorldObject => ObjectData;
    /// <summary>Accumulated experience; it has no stat effect in v1.</summary>
    public int Experience { get; internal set; }
    /// <summary>Amount held by this collectible instance.</summary>
    public int PickupAmount { get; internal set; }
    /// <summary>Ticks before a scattered pickup can attract or collect.</summary>
    public int PickupArmTicks { get; internal set; }
    /// <summary>Remaining lifetime; -1 is permanent.</summary>
    public int LifetimeTicks { get; internal set; } = -1;
    /// <summary>Creation tick.</summary>
    public int SpawnTick { get; private set; }
    /// <summary>Most recently serviced navigation route, excluding the current cell.</summary>
    public ReadOnlySpan<IntVec2> Path => PathBuffer.AsSpan(0, PathCount);
    /// <summary>Whether the last navigation search reached its requested goal.</summary>
    public bool PathReachedGoal { get; internal set; }
    /// <summary>Whether the requested path extends beyond the retained waypoints.</summary>
    public bool PathTruncated { get; internal set; }
}
