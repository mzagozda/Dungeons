using Dungeons.Core.Level;
using Dungeons.Core.Math;
using Dungeons.Core.Util;
using Dungeons.Core.Content;

namespace Dungeons.Core.Sim;

/// <summary>High-level session lifetime.</summary>
public enum SessionState
{
    /// <summary>Accepts fixed simulation steps.</summary>
    Running,
    /// <summary>Does not advance ticks.</summary>
    Paused,
    /// <summary>Mission completed.</summary>
    Won,
    /// <summary>Mission failed.</summary>
    Failed,
}

/// <summary>Fixed ordered system stages (§7.4). Later milestones fill reserved stages in place.</summary>
public enum SystemStage
{
    /// <summary>Previous transforms and countdowns.</summary>
    Timer,
    /// <summary>Player commands.</summary>
    PlayerControl,
    /// <summary>Perception, strategy transitions and navigation steering.</summary>
    Ai,
    /// <summary>Oldest-first A* budget.</summary>
    Pathfinding,
    /// <summary>Advance actions, check melee arcs and launch shots.</summary>
    Combat,
    /// <summary>Swept projectile integration and impacts.</summary>
    Projectile,
    /// <summary>Terrain movement.</summary>
    Movement,
    /// <summary>Circle separation.</summary>
    Separation,
    /// <summary>Stairs feet height.</summary>
    TerrainHeight,
    /// <summary>Pickup attraction and collection.</summary>
    Pickup,
    /// <summary>Reserved world objects stage (M6).</summary>
    Interaction,
    /// <summary>Stable queued damage, knockback and death resolution.</summary>
    DamageResolution,
    /// <summary>Reserved level actions stage (M6).</summary>
    Trigger,
    /// <summary>Reserved mission goals stage (M6).</summary>
    Objective,
    /// <summary>Reserved respawn stage (M6).</summary>
    Lives,
    /// <summary>Stable end-of-tick compaction.</summary>
    Removal,
}

/// <summary>Host-independent deterministic simulation. No rendering, file access or wall-clock state.</summary>
public sealed partial class GameSession
{
    private readonly PlayerInput[] _singleInput = new PlayerInput[1];
    private ContentRegistry? _contentOverride;
    private static readonly SystemStage[] _systemOrder = [SystemStage.Timer, SystemStage.PlayerControl, SystemStage.Ai, SystemStage.Pathfinding,
        SystemStage.Combat, SystemStage.Projectile, SystemStage.Movement, SystemStage.Separation, SystemStage.TerrainHeight, SystemStage.Pickup,
        SystemStage.Interaction, SystemStage.DamageResolution, SystemStage.Trigger, SystemStage.Objective, SystemStage.Lives, SystemStage.Removal];

    /// <summary>Creates a snapshot from an editable level. Recording storage is bounded and optional.</summary>
    public GameSession(LevelDocument level, ulong seed = 1, ILogger? log = null, bool record = false, int replayCapacity = GameConstants.DefaultReplayTicks, DifficultyProfile? difficulty = null, ContentRegistry? content = null, StartOverride? startOverride = null)
    {
        if (level is null) { throw new ArgumentNullException(nameof(level)); }
        if (level.HasErrors) { throw new ArgumentException("The editing document has validation errors and cannot be played.", nameof(level)); }
        _contentOverride = content;
        StartOverride = startOverride;
        World = new World(level, seed, log ?? NullLogger.Instance, difficulty ?? DifficultyProfile.Default, content, startOverride);
        World.Mission.Initialize(World); ApplyMissionOutcome();
        if (record) { Recorder = new ReplayRecorder(seed, level.Id, Hash(), replayCapacity, World.Difficulty, startOverride); }
    }
    /// <summary>Creates a session with an explicit validated registry and optional future difficulty tier.</summary>
    public static GameSession Create(LevelDocument level, ContentRegistry content, ulong seed, DifficultyProfile? difficulty = null, ILogger? log = null, bool record = false, int replayCapacity = GameConstants.DefaultReplayTicks, StartOverride? startOverride = null)
    {
        if (content is null || !content.IsValid) { throw new ArgumentException("A valid registry is required.", nameof(content)); }
        return new(level, seed, log, record, replayCapacity, difficulty, content, startOverride);
    }
    /// <summary>Optional recorded editor origin; null preserves ordinary authored spawning.</summary>
    public StartOverride? StartOverride { get; private set; }
    /// <summary>Read-only world state surface.</summary>
    public World World { get; private set; }
    /// <summary>Completed tick count.</summary>
    public int Tick => World.Tick;
    /// <summary>Current session state.</summary>
    public SessionState State { get; private set; } = SessionState.Running;
    /// <summary>Optional preallocated input/hash recorder.</summary>
    public ReplayRecorder? Recorder { get; }
    /// <summary>Canonical stage order. Unimplemented stages are explicitly reserved, not claimed complete.</summary>
    public static ReadOnlySpan<SystemStage> SystemOrder => _systemOrder;
    /// <summary>Pauses fixed ticks.</summary>
    public void Pause() { if (State == SessionState.Running) { State = SessionState.Paused; } }
    /// <summary>Resumes a paused session.</summary>
    public void Resume() { if (State == SessionState.Paused) { State = SessionState.Running; } }
    // Only simulation systems may queue navigation. Host input is the recorded Step stream.
    internal bool RequestPath(int entityId, IntVec2 goal) => World.EnqueuePath(entityId, goal);
    /// <summary>Executes one fixed single-player tick.</summary>
    public void Step(in PlayerInput input)
    {
        _singleInput[0] = input;
        Step(_singleInput.AsSpan());
    }
    /// <summary>Internal multiplayer-shaped command stream, indexed by player slot.</summary>
    public void Step(ReadOnlySpan<PlayerInput> inputs)
    {
        if (State != SessionState.Running) { return; }
        if (inputs.Length != World.Players.Count) { throw new ArgumentException("One input per player slot is required.", nameof(inputs)); }
        World.Tick++;
        for (int stageIndex = 0; stageIndex < _systemOrder.Length; stageIndex++)
        {
            switch (_systemOrder[stageIndex])
            {
                case SystemStage.Timer: TimerSystem(); break;
                case SystemStage.PlayerControl: PlayerControlSystem(inputs); break;
                case SystemStage.Ai: AiSystem(); break;
                case SystemStage.Combat: CombatSystem(); break;
                case SystemStage.Projectile: ProjectileSystem(); break;
                case SystemStage.Pickup: PickupSystem(); break;
                case SystemStage.Interaction: InteractionSystem(inputs); break;
                case SystemStage.DamageResolution: DamageSystem(); break;
                case SystemStage.Pathfinding: World.ServePaths(); break;
                case SystemStage.Movement: MovementSystem(); break;
                case SystemStage.Separation: World.Separation.Step(World); break;
                case SystemStage.Trigger: World.Mission.StepTriggers(World); ApplyMissionOutcome(); break;
                case SystemStage.Objective: World.Mission.StepObjectives(World); ApplyMissionOutcome(); break;
                case SystemStage.Lives: LivesSystem(); break;
                case SystemStage.TerrainHeight:
                    for (int i = 0; i < World.Active.Count; i++)
                    {
                        Entity entity = World.Active[i];
                        if (entity.Kind != EntityKind.Projectile) { entity.Y = World.Grid.HeightAt(entity.Pos); }
                        if (entity.Kind is EntityKind.Player or EntityKind.Enemy) { AdvanceFootsteps(entity); }
                    }
                    break;
                case SystemStage.Removal: World.RemoveExpired(); break;
            }
            if (State is SessionState.Won or SessionState.Failed)
            { if (_systemOrder[stageIndex] != SystemStage.Removal) { World.RemoveExpired(); } break; }
        }
        Recorder?.Record(inputs, Hash());
    }
    private void ApplyMissionOutcome()
    { if (World.Mission.Outcome is SessionState.Won or SessionState.Failed) { State = World.Mission.Outcome; } }
    /// <summary>Consumes queued presentation events; returned storage is valid until the next step.</summary>
    public ReadOnlySpan<SimEvent> DrainEvents() => World.DrainEvents();
    /// <summary>Deterministic state fingerprint, independent of presentation drain frequency.</summary>
    public ulong Hash() => WorldHasher.Compute(World, State);
    private void TimerSystem()
    {
        for (int i = 0; i < World.Active.Count; i++)
        {
            Entity e = World.Active[i];
            e.PrevPos = e.Pos; e.PrevY = e.Y;
            if (e.HealthData.InvulnTicks > 0) { e.HealthData.InvulnTicks--; }
            if (e.LifetimeTicks > 0) { e.LifetimeTicks--; }
            AdvanceActionTimers(e);
            if (e.Kind == EntityKind.Enemy)
            {
                if (e.AiData.StateTicks > 0) { e.AiData.StateTicks--; }
                if (e.AiData.RepathTicks > 0) { e.AiData.RepathTicks--; }
                if (e.AiData.RetryTicks > 0) { e.AiData.RetryTicks--; }
                if (e.AiData.WanderTicks > 0) { e.AiData.WanderTicks--; }
            }
        }
    }
    private void PlayerControlSystem(ReadOnlySpan<PlayerInput> inputs)
    {
        for (int i = 0; i < World.Players.Count; i++)
        {
            Entity e = World.Players[i];
            e.Vel = Vec2.Zero;
            if (!e.Alive) { continue; }
            ControlActions(e, inputs[i]);
        }
    }
    private void MovementSystem()
    {
        for (int i = 0; i < World.Active.Count; i++)
        {
            Entity e = World.Active[i];
            if (!e.Alive || e.Kind is not (EntityKind.Player or EntityKind.Enemy)) { continue; }
            Vec2 velocity = e.Vel + e.Knockback;
            Vec2 requested = velocity.ClampLength(GameConstants.MaxSpeed);
            e.Pos = Collision.Move(World.Grid, e.Pos, ref velocity, e.Radius);
            e.Vel = new(velocity.X == 0 && requested.X != 0 ? 0 : e.Vel.X, velocity.Z == 0 && requested.Z != 0 ? 0 : e.Vel.Z);
            e.Knockback *= GameConstants.KnockbackDecay;
            if (e.Knockback.LengthSquared < GameConstants.KnockbackStop * GameConstants.KnockbackStop) { e.Knockback = Vec2.Zero; }
        }
    }
    private void AdvanceFootsteps(Entity entity)
    {
        // Collision has clipped voluntary velocity and separation/height are resolved.
        // Impulses, teleportation, rolls and a blocked move cannot count as walking.
        if (!entity.Alive || entity.ActionData.RollRemaining > 0 || Vec2.Dot(entity.Pos - entity.PrevPos, entity.Vel) <= 0)
        { entity.FootstepTicks = 0; return; }
        if (++entity.FootstepTicks < GameConstants.FootstepIntervalTicks) { return; }
        entity.FootstepTicks = 0; IntVec2 cell = IntVec2.FromPosition(entity.Pos);
        World.Emit(new(World.Tick, SimEventKind.Footstep, entity.Id, entity.Pos, cell: cell, y: entity.Y, text: entity.Def.Id, surfaceKind: World.SurfaceAt(cell)));
    }
}
