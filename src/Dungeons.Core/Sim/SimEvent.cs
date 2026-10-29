namespace Dungeons.Core.Sim;

/// <summary>Event identifiers; host code treats unrecognised future values as no-ops.</summary>
public enum SimEventKind
{
    /// <summary>An entity entered the runtime world.</summary>
    EntitySpawned,
    /// <summary>An entity left the runtime world.</summary>
    EntityRemoved,
    /// <summary>An attack entered windup.</summary>
    AttackStarted,
    /// <summary>An attack entered its first active frame.</summary>
    AttackActive,
    /// <summary>A dodge began.</summary>
    RollStarted,
    /// <summary>A dodge completed.</summary>
    RollEnded,
    /// <summary>Health was restored by a potion.</summary>
    PotionUsed,
    /// <summary>Health was restored.</summary>
    Healed,
    /// <summary>A collectible was consumed.</summary>
    PickupCollected,
    /// <summary>A deferred hit changed health.</summary>
    DamageApplied,
    /// <summary>A hero lost a life.</summary>
    PlayerDied,
    /// <summary>A shot was launched.</summary>
    ProjectileSpawned,
    /// <summary>A shot struck blocking terrain.</summary>
    ProjectileHitTerrain,
    /// <summary>A shot struck a hostile entity.</summary>
    ProjectileHitEntity,
    /// <summary>An enemy acquired a target and began its alert telegraph.</summary>
    EnemyAlerted,
    /// <summary>An enemy changed behaviour mode; Amount carries EnemyMode.</summary>
    EnemyStateChanged,
    /// <summary>A valid monster spawn is waiting for the live/entity cap.</summary>
    SpawnDeferred,
    /// <summary>A chest granted its one-time loot.</summary>
    ChestOpened,
    /// <summary>A gate began opening or closing.</summary>
    GateStateChanged,
    /// <summary>A checkpoint became the respawn anchor.</summary>
    CheckpointActivated,
    /// <summary>A hero returned after the death animation.</summary>
    PlayerRespawned,
    /// <summary>The session exhausted its lives or explicitly failed.</summary>
    MissionFailed,
    /// <summary>The session satisfied its completion rule or was explicitly completed.</summary>
    MissionComplete,
    /// <summary>An authored objective changed lifecycle state.</summary>
    ObjectiveChanged,
    /// <summary>A transient authored message was shown.</summary>
    ShowMessage,
    /// <summary>An authored one-shot sound was requested.</summary>
    PlaySound,
    /// <summary>An authored music crossfade was requested.</summary>
    MusicChange,
    /// <summary>A presentation shake was requested.</summary>
    CameraShake,
    /// <summary>A mission flag changed value.</summary>
    FlagChanged,
    /// <summary>An action granted inventory counters.</summary>
    InventoryChanged,
    /// <summary>A hero approached an exit whose requirements are unmet.</summary>
    ExitLocked,
    /// <summary>A declarative spawner activated.</summary>
    SpawnerActivated,
    /// <summary>A declarative spawner requested its final monster.</summary>
    SpawnerFinished,
    /// <summary>A living actor completed fifteen consecutive voluntary moving ticks.</summary>
    Footstep,
}

/// <summary>Value-only presentation event. No mutable entity/host references cross the boundary.</summary>
public readonly struct SimEvent(int tick, SimEventKind kind, int entityId, Math.Vec2 position, int sourceId = 0, int amount = 0, Math.Vec2 direction = default, bool lethal = false, Math.IntVec2 cell = default, float y = 0, int meleeSwing = 0, string text = "", float value = 0, int durationTicks = 0, MissionResults results = default, string surfaceKind = "")
{
    /// <summary>Simulation tick.</summary>
    public int Tick { get; } = tick;
    /// <summary>Event kind.</summary>
    public SimEventKind Kind { get; } = kind;
    /// <summary>Stable runtime id.</summary>
    public int EntityId { get; } = entityId;
    /// <summary>World position.</summary>
    public Math.Vec2 Position { get; } = position;
    /// <summary>Source/owner id, when applicable.</summary>
    public int SourceId { get; } = sourceId;
    /// <summary>Damage/healing/collection amount or attack swing index.</summary>
    public int Amount { get; } = amount;
    /// <summary>Attack/roll direction or applied knockback impulse.</summary>
    public Math.Vec2 Direction { get; } = direction;
    /// <summary>Whether this damage killed its target.</summary>
    public bool Lethal { get; } = lethal;
    /// <summary>Terrain impact cell, when applicable.</summary>
    public Math.IntVec2 Cell { get; } = cell;
    /// <summary>Event's world-space height, retained after an entity is removed.</summary>
    public float Y { get; } = y;
    /// <summary>Hero melee swing that produced damage, or zero for other sources.</summary>
    public int MeleeSwing { get; } = meleeSwing;
    /// <summary>Precompiled authored text/content id. Audio-bearing actor/object events retain their definition id even after pooled storage is reused; no tick-time formatting.</summary>
    public string Text { get; } = text;
    /// <summary>Presentation effect magnitude; AttackStarted uses 1 for ranged, 0 for melee.</summary>
    public float Value { get; } = value;
    /// <summary>Presentation effect duration.</summary>
    public int DurationTicks { get; } = durationTicks;
    /// <summary>Frozen counters for terminal events.</summary>
    public MissionResults Results { get; } = results;
    /// <summary>Immutable ground tile id for a footstep, resolving composite tiles to their base floor; empty for code-only terrain.</summary>
    public string SurfaceKind { get; } = surfaceKind;
}
