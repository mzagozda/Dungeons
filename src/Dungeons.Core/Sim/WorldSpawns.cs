using Dungeons.Core.Content;
using Dungeons.Core.Math;
using Dungeons.Core.Util;

namespace Dungeons.Core.Sim;

public sealed partial class World
{
    private readonly Entity[] _entityPool = new Entity[GameConstants.MaxEntities];
    private readonly IntVec2[][] _enemyPaths = new IntVec2[GameConstants.MaxMonsters][];
    private readonly SpawnRequest[] _spawns = new SpawnRequest[GameConstants.MaxEntities];
    private readonly EntityDef[] _pickups = new EntityDef[3];
    private int _freeEntities, _freeEnemyPaths, _spawnHead, _spawnCount;
    private static readonly EntityDef PoolPlaceholder = new("core:pool_slot", EntityKind.Prop, new(1, 0), 0);
    private static readonly EntityDef EmeraldDefault = new("core:emerald_pile", EntityKind.Pickup, new(1, 0), .2f, pickup: new(PickupKind.Emerald));
    private static readonly EntityDef FoodDefault = new("core:food", EntityKind.Pickup, new(1, 0), .2f, pickup: new(PickupKind.Food));
    private static readonly EntityDef ArrowsDefault = new("core:arrow_bundle", EntityKind.Pickup, new(1, 0), .2f, pickup: new(PickupKind.Arrows, 10));
    private readonly struct SpawnRequest(EntityDef definition, Vec2 position, Vec2 facing, IReadOnlyList<string> tags, SpawnSettings? settings, int ownerId)
    {
        internal EntityDef Definition { get; } = definition;
        internal Vec2 Position { get; } = position;
        internal Vec2 Facing { get; } = facing;
        internal IReadOnlyList<string> Tags { get; } = tags;
        internal SpawnSettings? Settings { get; } = settings;
        internal int OwnerId { get; } = ownerId;
    }
    private void InitializeSpawnStorage()
    {
        InitializeStatistics();
        for (int i = 0; i < _entityPool.Length; i++) { _entityPool[i] = new Entity(0, PoolPlaceholder, Vec2.Zero, null, 0, 0) { PooledEntity = true }; }
        for (int i = 0; i < _enemyPaths.Length; i++) { _enemyPaths[i] = new IntVec2[GameConstants.MaxPathWaypoints]; }
        _freeEntities = _entityPool.Length; _freeEnemyPaths = _enemyPaths.Length;
        _pickups[0] = EmeraldDefault; _pickups[1] = FoodDefault; _pickups[2] = ArrowsDefault;
        if (_content is null) { return; }
        var chosen = new bool[3];
        foreach (EntityDef definition in _content.Entities.Values)
        {
            if (definition.Kind != EntityKind.Pickup || definition.Pickup is not PickupSpec pickup) { continue; }
            int index = (int)pickup.Kind;
            if (!chosen[index] || string.CompareOrdinal(definition.Id, _pickups[index].Id) < 0) { _pickups[index] = definition; chosen[index] = true; }
        }
    }
    /// <summary>Valid queued monsters waiting for live/entity capacity.</summary>
    public int PendingMonsterSpawns => _spawnCount;
    /// <summary>Current live monsters, including emerging enemies.</summary>
    public int LiveMonsterCount
    {
        get { int count = 0; for (int i = 0; i < Active.Count; i++) { if (Active[i].Kind == EntityKind.Enemy && Active[i].Alive && Active[i].LifetimeTicks != 0) { count++; } } return count; }
    }
    internal Entity? SpawnEntity(EntityDef definition, Vec2 position, Vec2 facing, IReadOnlyList<string>? tags = null, bool emerging = false, SpawnSettings? settings = null, int ownerId = 0)
    {
        if (definition.Kind is EntityKind.Player or EntityKind.Projectile) { throw new ArgumentException("Use player slots or the projectile factory.", nameof(definition)); }
        IntVec2 cell = IntVec2.FromPosition(position);
        if (!position.IsFinite || !facing.IsFinite || !Grid.Contains(cell.X, cell.Z) || definition.Kind != EntityKind.Pickup && definition.RequiresGround && !Grid.IsWalkable(cell.X, cell.Z)) { return null; }
        if (definition.Kind == EntityKind.Gate && !GateFits(position, settings?.Rules ?? definition.DefaultObjectRules)) { return null; }
        if (_freeEntities == 0 || Active.Count == GameConstants.MaxEntities || NextEntityId == int.MaxValue || definition.Kind == EntityKind.Enemy && (_freeEnemyPaths == 0 || LiveMonsterCount >= Difficulty.MaxMonsters)) { return null; }
        Entity entity = _entityPool[--_freeEntities]; entity.Reset(NextEntityId++, definition, position, settings?.LevelId, Tick, Grid.HeightAt(position));
        Difficulty.Apply(entity);
        entity.Facing = facing.Normalized; if (entity.Facing == Vec2.Zero) { entity.Facing = Vec2.North; } entity.SetSpawnTags(tags ?? Array.Empty<string>());
        if (settings is not null) { entity.ApplySpawnSettings(settings); }
        entity.SpawnOwnerId = ownerId;
        if (definition.Kind == EntityKind.Enemy)
        {
            entity.PathBuffer = _enemyPaths[--_freeEnemyPaths];
            entity.AiData.WanderTicks = Randoms.Ai.NextInt(180, 361);
            if (emerging) { entity.AiData.Mode = EnemyMode.Emerging; entity.AiData.StateTicks = 30; entity.HealthData.InvulnTicks = 30; }
        }
        Active.Add(entity); RegisterObject(entity); Mission.RegisterSpawnedObject(this, entity, settings); Mission.Register(entity); Emit(new(Tick, SimEventKind.EntitySpawned, entity.Id, entity.Pos, direction: entity.Facing, y: entity.Y)); return entity;
    }
    internal void ReleaseEnemyPath(Entity entity)
    {
        if (!entity.PooledEntity || entity.Kind != EntityKind.Enemy || entity.PathBuffer.Length == 0) { return; }
        _enemyPaths[_freeEnemyPaths++] = entity.PathBuffer; entity.PathBuffer = Array.Empty<IntVec2>(); entity.PathCount = 0;
        entity.AiData.HasRoute = false; entity.AiData.Waypoint = 0;
    }
    // The caller supplies load-time immutable tag storage, avoiding a per-spawn copy.
    internal bool RequestMonsterSpawn(EntityDef definition, Vec2 position, Vec2 facing, IReadOnlyList<string>? tags = null, SpawnSettings? settings = null, int ownerId = 0)
    {
        if (definition.Kind != EntityKind.Enemy) { throw new ArgumentException("Monster definition required.", nameof(definition)); }
        IntVec2 cell = IntVec2.FromPosition(position);
        if (!position.IsFinite || !facing.IsFinite || !Grid.IsWalkable(cell.X, cell.Z)) { return false; }
        facing = facing.Normalized; if (facing == Vec2.Zero) { facing = Vec2.North; }
        tags = settings?.Tags ?? tags ?? Array.Empty<string>();
        if (_spawnCount == 0 && SpawnEntity(definition, position, facing, tags, true, settings, ownerId) is not null) { return true; }
        if (_spawnCount == _spawns.Length) { throw new InvalidOperationException("Deferred monster spawn capacity exceeded."); }
        _spawns[(_spawnHead + _spawnCount) % _spawns.Length] = new(definition, position, facing, tags, settings, ownerId); _spawnCount++;
        Emit(new(Tick, SimEventKind.SpawnDeferred, 0, position, amount: _spawnCount)); return true;
    }
    internal void ServeDeferredSpawns()
    {
        while (_spawnCount > 0)
        {
            SpawnRequest request = _spawns[_spawnHead];
            // Gates may have closed since this request entered the FIFO. Revalidate without
            // another random draw, retaining the request if the level has no safe ground.
            if (!TryNearestSafePosition(request.Position, request.Definition.Radius, out Vec2 point) || SpawnEntity(request.Definition, point, request.Facing, request.Tags, true, request.Settings, request.OwnerId) is null) { break; }
            _spawns[_spawnHead] = default; _spawnHead = (_spawnHead + 1) % _spawns.Length; _spawnCount--;
        }
    }
    internal bool HasPendingTag(string tag)
        => CountPendingTag(tag) > 0;
    internal int CountOwnedMonsters(int ownerId)
    {
        int count = 0;
        for (int i = 0; i < Active.Count; i++) { Entity entity = Active[i]; if (entity.SpawnOwnerId == ownerId && entity.Alive && entity.Kind == EntityKind.Enemy && entity.LifetimeTicks != 0) { count++; } }
        for (int i = 0; i < _spawnCount; i++) { if (_spawns[(_spawnHead + i) % _spawns.Length].OwnerId == ownerId) { count++; } }
        return count;
    }
    internal int CountPendingTag(string tag)
    {
        int count = 0;
        for (int i = 0; i < _spawnCount; i++)
        { SpawnRequest request = _spawns[(_spawnHead + i) % _spawns.Length]; for (int j = 0; j < request.Tags.Count; j++) { if (request.Tags[j] == tag) { count++; break; } } }
        return count;
    }
    internal void HashSpawns(ref Fnv1a hash)
    {
        hash.Add(_spawnCount);
        for (int i = 0; i < _spawnCount; i++)
        {
            SpawnRequest request = _spawns[(_spawnHead + i) % _spawns.Length];
            hash.Add(request.Definition.Id.Length); hash.Add(request.Definition.Id); hash.Add(request.Definition.SimulationHash);
            hash.Add(MathUtil.Quantize(request.Position.X)); hash.Add(MathUtil.Quantize(request.Position.Z)); hash.Add(MathUtil.Quantize(request.Facing.X)); hash.Add(MathUtil.Quantize(request.Facing.Z));
            hash.Add(request.Tags.Count); for (int j = 0; j < request.Tags.Count; j++) { hash.Add(request.Tags[j].Length); hash.Add(request.Tags[j]); }
            hash.Add(request.Settings is not null); request.Settings?.Hash(ref hash);
            hash.Add(request.OwnerId);
        }
    }
    internal void DropLoot(string tableId, Vec2 position)
    {
        if (_content is null || !_content.Loot.TryGetValue(tableId, out LootTable? table)) { throw new ContentMissingException(tableId); }
        for (int i = 0; i < table.Rows.Count; i++)
        {
            LootRow row = table.Rows[i]; if (!Randoms.Loot.Chance(row.Chance)) { continue; }
            int count = Randoms.Loot.NextInt(row.Min, row.Max + 1);
            PickupKind kind = row.Item switch { "emerald" => PickupKind.Emerald, "food" => PickupKind.Food, "arrows" => PickupKind.Arrows, _ => throw new InvalidOperationException("Unsupported loot kind.") };
            for (int n = 0; n < count; n++)
            {
                Vec2 scatter = new(.3f, 0);
                for (int attempt = 0; attempt < 32; attempt++)
                {
                    Vec2 sample = new(Randoms.Loot.Range(-.9f, .9f), Randoms.Loot.Range(-.9f, .9f));
                    if (sample.LengthSquared is < .09f or > .81f) { continue; } scatter = sample; break;
                }
                EntityDef definition = _pickups[(int)kind];
                if (!TryNearestSafePosition(position, definition.Radius, out Vec2 origin))
                { Log.Log(LogLevel.Warning, "Sim", "No safe ground exists for loot."); continue; }
                // Keep the same random draws and unconstrained point on open ground.
                // Resolve the whole pickup circle against walls, cliffs and map edges.
                Vec2 point = Collision.Resolve(Grid, origin, position + scatter, definition.Radius);
                Entity? pickup = SpawnEntity(definition, point, Vec2.North);
                if (pickup is null) { Log.Log(LogLevel.Warning, "Sim", "Loot entity capacity exceeded."); continue; }
                pickup.PickupArmTicks = 24; if (kind == PickupKind.Emerald) { pickup.PickupAmount = 1; }
            }
        }
    }
}
