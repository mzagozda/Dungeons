using Dungeons.Core.Math;
using Dungeons.Core.Util;

namespace Dungeons.Core.Sim;

public sealed partial class World
{
    private Cell[] _baseTerrain = [];
    private ulong _baseTerrainHash;
    private readonly List<GateFootprint> _gates = new(GameConstants.MaxEntities);
    private readonly Vec2[] _gatePushPositions = new Vec2[GameConstants.MaxEntities];
    private readonly struct GateFootprint(Entity entity, GateArea area)
    {
        internal readonly Entity Entity = entity;
        internal readonly GateArea Area = area;
        internal int X => Area.X;
        internal int Z => Area.Z;
        internal int Width => Area.Width;
        internal int Depth => Area.Depth;
        internal bool Contains(int x, int z) => Area.Contains(x, z);
    }
    /// <summary>Current respawn anchor, initially the player spawn.</summary>
    public Vec2 CheckpointPosition { get; private set; }
    /// <summary>Current respawn facing.</summary>
    public Vec2 CheckpointFacing { get; private set; } = Vec2.North;
    /// <summary>Activated checkpoint runtime id; zero means the initial spawn.</summary>
    public int CheckpointId { get; private set; }
    /// <summary>Highest accepted authored checkpoint order.</summary>
    public int CheckpointOrder { get; private set; }
    private void InitializeObjects()
    {
        _baseTerrainHash = Grid.HashPrefix.Value;
        _baseTerrain = new Cell[Grid.Count]; for (int i = 0; i < Grid.Count; i++) { _baseTerrain[i] = Grid[i]; }
        if (_players.Count > 0) { CheckpointPosition = _players[0].Pos; CheckpointFacing = _players[0].Facing; }
        foreach (Entity entity in Active)
        {
            if (entity.Kind != EntityKind.Gate) { continue; }
            GateFootprint gate = Footprint(entity);
            int x = gate.X, z = gate.Z;
            if (!Grid.Contains(x, z) || !Grid.Contains(x + gate.Width - 1, z + gate.Depth - 1)) { throw new ArgumentException("Gate footprint exceeds the level bounds."); }
            _gates.Add(gate); entity.Solid = entity.ObjectData.Gate != GatePhase.Open;
        }
        foreach (GateFootprint gate in _gates) { UpdateGateCells(gate); }
        foreach (GateFootprint gate in _gates)
        {
            if (gate.Entity.ObjectData.Gate == GatePhase.Open) { continue; }
            foreach (Entity actor in Active)
            {
                if (actor.Kind is EntityKind.Player or EntityKind.Enemy && gate.Area.Overlaps(actor.Pos, actor.Radius))
                { throw new ArgumentException("Actor overlaps an initially closed gate footprint."); }
            }
        }
    }
    private static GateFootprint Footprint(Entity entity)
    {
        ObjectRules rules = entity.ObjectRules; return new(entity, new(entity.Pos, rules.Width, rules.AlongX));
    }
    private bool GateFits(Vec2 position, ObjectRules rules)
    {
        return new GateArea(position, rules.Width, rules.AlongX).InBounds(Grid);
    }
    private void RegisterObject(Entity entity)
    {
        if (entity.Kind != EntityKind.Gate) { return; }
        GateFootprint gate = Footprint(entity); _gates.Add(gate);
        // Apply a closed template through the same transactional displacement as an action.
        bool closed = entity.ObjectData.Gate == GatePhase.Closed;
        entity.ObjectData.Gate = GatePhase.Open; entity.Solid = false;
        if (closed && SetGate(entity, false)) { entity.ObjectData.Gate = GatePhase.Closed; entity.ObjectData.StateTicks = 0; }
    }
    private void UnregisterObject(Entity entity)
    {
        if (entity.Kind != EntityKind.Gate) { return; }
        for (int i = 0; i < _gates.Count; i++)
        {
            GateFootprint gate = _gates[i]; if (gate.Entity != entity) { continue; }
            _gates.RemoveAt(i); UpdateGateCells(gate); return;
        }
    }
    private void UpdateGateCells(GateFootprint changed)
    {
        for (int z = changed.Z; z < changed.Z + changed.Depth; z++)
        {
            for (int x = changed.X; x < changed.X + changed.Width; x++)
            {
                bool blocked = false;
                foreach (GateFootprint gate in _gates)
                { if (gate.Entity.Alive && gate.Entity.ObjectData.Gate is GatePhase.Closed or GatePhase.Closing && gate.Contains(x, z)) { blocked = true; break; } }
                Cell original = _baseTerrain[z * Grid.Width + x];
                Grid.SetRuntimeCell(x, z, blocked ? new(original.Height, original.Walkable, true, true, original.BlocksVision, original.StairsDir, original.MoveCost) : original);
            }
        }
    }
    internal bool SetGate(Entity entity, bool open)
    {
        if (entity.Kind != EntityKind.Gate || !entity.Alive) { return false; }
        bool traversable = entity.ObjectData.Gate is GatePhase.Open or GatePhase.Opening; if (traversable == open) { return false; }
        GateFootprint footprint = Footprint(entity); WorldObjectState previous = entity.ObjectData;
        entity.ObjectData.Gate = open ? GatePhase.Opening : GatePhase.Closing;
        UpdateGateCells(footprint);
        if (!open)
        {
            bool safe = true;
            for (int i = 0; i < Active.Count; i++)
            {
                Entity actor = Active[i]; _gatePushPositions[i] = actor.Pos;
                if (!actor.Alive || actor.Kind is not (EntityKind.Player or EntityKind.Enemy or EntityKind.Pickup)) { continue; }
                if (!TryNearestSafePosition(actor.Pos, actor.Radius, out _gatePushPositions[i])) { safe = false; break; }
            }
            for (int p = 0; p < _players.Count && safe; p++)
            { safe = TryNearestSafePosition(CheckpointPosition, _players[p].Radius, out _); }
            if (!safe)
            {
                entity.ObjectData = previous; UpdateGateCells(footprint);
                Log.Log(LogLevel.Warning, "Sim", "Gate closing ignored because no safe actor or respawn position exists."); return false;
            }
            for (int i = 0; i < Active.Count; i++)
            {
                Entity actor = Active[i]; if (actor.Pos == _gatePushPositions[i]) { continue; }
                actor.Pos = actor.PrevPos = _gatePushPositions[i]; actor.Y = actor.PrevY = Grid.HeightAt(actor.Pos); actor.Vel = actor.Knockback = Vec2.Zero;
            }
            // A closing gate creates blocking terrain after the projectile stage.
            // Resolve existing shots now, after all displacement can commit, rather
            // than leaving live arrows inside the new solid volume for a tick.
            for (int i = 0; i < Active.Count; i++)
            {
                Entity shot = Active[i];
                if (!shot.Alive || shot.Kind != EntityKind.Projectile || shot.LifetimeTicks == 0 || !footprint.Area.Overlaps(shot.Pos, shot.Radius)) { continue; }
                shot.Alive = false; shot.LifetimeTicks = 0;
                var cell = new IntVec2(System.Math.Clamp((int)MathF.Floor(shot.Pos.X), footprint.X, footprint.X + footprint.Width - 1),
                    System.Math.Clamp((int)MathF.Floor(shot.Pos.Z), footprint.Z, footprint.Z + footprint.Depth - 1));
                Emit(new(Tick, SimEventKind.ProjectileHitTerrain, shot.Id, shot.Pos, direction: shot.Facing, cell: cell, y: shot.Y, text: shot.Def.Id));
            }
        }
        entity.ObjectData.StateTicks = 45; entity.ObjectData.ChangedTick = Tick; entity.Solid = !open; entity.ObjectData.Interacted = false;
        Emit(new(Tick, SimEventKind.GateStateChanged, entity.Id, entity.Pos, amount: (int)entity.ObjectData.Gate, y: entity.Y, text: entity.Def.Id)); return true;
    }
    internal bool OpenChest(Entity chest)
    {
        if (chest.Kind != EntityKind.Chest || !chest.Alive || chest.ObjectData.Opened) { return false; }
        chest.ObjectData.Opened = true; chest.ObjectData.ChangedTick = Tick;
        if (chest.ObjectRules.Loot.Length > 0) { DropLoot(chest.ObjectRules.Loot, chest.Pos); }
        Emit(new(Tick, SimEventKind.ChestOpened, chest.Id, chest.Pos, y: chest.Y, text: chest.Def.Id)); return true;
    }
    internal bool ActivateCheckpoint(Entity checkpoint)
    {
        if (checkpoint.Kind != EntityKind.Checkpoint || !checkpoint.Alive || checkpoint.Id == CheckpointId || checkpoint.ObjectRules.Order < CheckpointOrder) { return false; }
        CheckpointId = checkpoint.Id; CheckpointOrder = checkpoint.ObjectRules.Order; CheckpointPosition = checkpoint.Pos; CheckpointFacing = checkpoint.Facing;
        checkpoint.ObjectData.Activated = true; checkpoint.ObjectData.ChangedTick = Tick;
        Emit(new(Tick, SimEventKind.CheckpointActivated, checkpoint.Id, checkpoint.Pos, amount: CheckpointOrder, y: checkpoint.Y, text: checkpoint.Def.Id)); return true;
    }
    internal bool CircleFits(Vec2 position, float radius)
    {
        IntVec2 centre = IntVec2.FromPosition(position); if (!Grid.IsWalkable(centre.X, centre.Z)) { return false; }
        for (int z = (int)MathF.Floor(position.Z - radius); z <= (int)MathF.Floor(position.Z + radius); z++)
        {
            for (int x = (int)MathF.Floor(position.X - radius); x <= (int)MathF.Floor(position.X + radius); x++)
            {
                if (Grid.CanStep(centre, new(x, z))) { continue; }
                float dx = position.X - MathUtil.Clamp(position.X, x, x + 1), dz = position.Z - MathUtil.Clamp(position.Z, z, z + 1);
                if (dx * dx + dz * dz < radius * radius - GameConstants.CollisionTangencyEpsilon) { return false; }
            }
        }
        return true;
    }
    internal Vec2 NearestSafePosition(Vec2 desired, float radius, Vec2 avoid = default, float avoidRadius = 0)
    {
        if (!TryNearestSafePosition(desired, radius, out Vec2 best, avoid, avoidRadius))
        { Log.Log(LogLevel.Warning, "Sim", "No safe position exists for an object displacement."); }
        return best;
    }
    internal bool TryNearestSafePosition(Vec2 desired, float radius, out Vec2 best, Vec2 avoid = default, float avoidRadius = 0)
    {
        best = desired;
        if (Vec2.DistanceSquared(desired, avoid) >= avoidRadius * avoidRadius && CircleFits(desired, radius)) { return true; }
        float distance = float.MaxValue;
        for (int z = 0; z < Grid.Height; z++)
        {
            for (int x = 0; x < Grid.Width; x++)
            {
                Vec2 point = new(x + .5f, z + .5f); float candidate = Vec2.DistanceSquared(point, desired);
                if (candidate >= distance || Vec2.DistanceSquared(point, avoid) < avoidRadius * avoidRadius || !CircleFits(point, radius)) { continue; }
                best = point; distance = candidate;
            }
        }
        return distance != float.MaxValue;
    }
    internal void HashObjects(ref Fnv1a hash)
    { hash.Add(_baseTerrainHash); hash.Add(CheckpointId); hash.Add(CheckpointOrder); hash.Add(MathUtil.Quantize(CheckpointPosition.X)); hash.Add(MathUtil.Quantize(CheckpointPosition.Z)); hash.Add(MathUtil.Quantize(CheckpointFacing.X)); hash.Add(MathUtil.Quantize(CheckpointFacing.Z)); }
}

public sealed partial class GameSession
{
    private void InteractionSystem(ReadOnlySpan<PlayerInput> inputs)
    {
        for (int i = 0; i < World.Active.Count; i++)
        {
            Entity entity = World.Active[i]; if (!entity.Alive) { continue; }
            if (entity.Kind == EntityKind.Gate && entity.ObjectData.StateTicks > 0 && --entity.ObjectData.StateTicks == 0)
            { entity.ObjectData.Gate = entity.ObjectData.Gate == GatePhase.Opening ? GatePhase.Open : GatePhase.Closed; }
            if (entity.Kind == EntityKind.Gate && entity.ObjectRules.Interactable)
            {
                for (int p = 0; p < World.Players.Count; p++)
                {
                    Entity hero = World.Players[p]; if (!hero.Alive || (inputs[p].Pressed & InputButtons.Interact) == 0 || Vec2.DistanceSquared(hero.Pos, entity.Pos) > 2.25f) { continue; }
                    bool open = entity.ObjectData.Gate is GatePhase.Closed or GatePhase.Closing;
                    if (World.SetGate(entity, open)) { entity.ObjectData.Interacted = open; } break;
                }
            }
            if (entity.Kind is not (EntityKind.Chest or EntityKind.Checkpoint)) { continue; }
            bool nearby = false;
            for (int p = 0; p < World.Players.Count; p++)
            { Entity hero = World.Players[p]; if (hero.Alive && Vec2.DistanceSquared(hero.Pos, entity.Pos) <= entity.ObjectRules.Radius * entity.ObjectRules.Radius) { nearby = true; break; } }
            if (entity.Kind == EntityKind.Checkpoint) { if (nearby) { World.ActivateCheckpoint(entity); } continue; }
            if (entity.ObjectData.Opened) { continue; }
            if (!nearby) { entity.ObjectData.ProximityTicks = 0; }
            else if (++entity.ObjectData.ProximityTicks >= 20) { World.OpenChest(entity); }
        }
    }
    private void LivesSystem()
    {
        for (int p = 0; p < World.Players.Count; p++)
        {
            Entity hero = World.Players[p]; if (hero.Alive || hero.ActionData.DeathTicks > 0) { continue; }
            if (World.Lives == 0)
            { World.Mission.End(World, true); State = SessionState.Failed; return; }
            hero.Alive = true; hero.Solid = hero.Def.Solid; hero.HealthData.Current = hero.HealthData.Max; hero.HealthData.InvulnTicks = 60;
            hero.InventoryData.Arrows = System.Math.Max(hero.InventoryData.Arrows, 10); hero.ActionData = default; hero.Knockback = hero.Vel = Vec2.Zero; hero.FootstepTicks = 0;
            hero.Pos = hero.PrevPos = World.NearestSafePosition(World.CheckpointPosition, hero.Radius); hero.Y = hero.PrevY = World.Grid.HeightAt(hero.Pos); hero.Facing = World.CheckpointFacing;
            for (int i = 0; i < World.Active.Count; i++)
            {
                Entity enemy = World.Active[i]; if (!enemy.Alive || enemy.Kind != EntityKind.Enemy) { continue; }
                enemy.ActionData = default; enemy.Vel = enemy.Knockback = Vec2.Zero; enemy.FootstepTicks = 0; enemy.AiData.TargetId = 0; enemy.AiData.LastSeenTick = enemy.AiData.LastAlertTick = -1;
                SetEnemyMode(enemy, EnemyMode.Idle); enemy.AiData.HasRoute = false; enemy.PathCount = 0;
                if (Vec2.DistanceSquared(enemy.Pos, hero.Pos) < 9)
                {
                    Vec2 away = (enemy.Pos - hero.Pos).Normalized; if (away == Vec2.Zero) { away = new(1, 0); }
                    enemy.Pos = enemy.PrevPos = World.NearestSafePosition(hero.Pos + away * 3.01f, enemy.Radius, hero.Pos, 3);
                    enemy.Y = enemy.PrevY = World.Grid.HeightAt(enemy.Pos);
                }
            }
            World.Emit(new(World.Tick, SimEventKind.PlayerRespawned, hero.Id, hero.Pos, amount: World.Lives));
            World.Mission.Respawned(World, hero);
        }
    }
}
