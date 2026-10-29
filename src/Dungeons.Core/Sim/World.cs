using Dungeons.Core.Level;
using Dungeons.Core.Math;
using Dungeons.Core.Util;
using Dungeons.Core.Content;

namespace Dungeons.Core.Sim;

/// <summary>Read-only runtime world surface; systems own all mutation.</summary>
public sealed partial class World
{
    internal readonly List<Entity> Active = new(GameConstants.MaxEntities);
    internal readonly RandomStreams Randoms;
    internal readonly ILogger Log;
    internal readonly SeparationSystem Separation;
    private readonly List<Entity> _players = new(GameConstants.MaxPlayers);
    private readonly SimEvent[] _events = new SimEvent[GameConstants.MaxEvents];
    private readonly PathRequest[] _requests = new PathRequest[GameConstants.MaxEntities];
    private readonly AStar _pathfinder;
    private readonly ContentRegistry? _content;
    private readonly Entity[] _projectilePool = new Entity[GameConstants.MaxProjectiles];
    private int _freeProjectiles;
    private static readonly EntityDef DefaultArrow = new("core:arrow_hero", EntityKind.Projectile, new(1, 0), .15f, projectile: new(10, 14), faction: Faction.Hero);
    private int _eventCount, _requestHead, _requestCount;
    private bool _overflowLogged;
    private readonly struct PathRequest(int entityId, IntVec2 goal)
    {
        public int EntityId { get; } = entityId;
        public IntVec2 Goal { get; } = goal;
    }

    internal World(LevelDocument level, ulong seed, ILogger log, DifficultyProfile difficulty, ContentRegistry? content = null, StartOverride? startOverride = null)
    {
        if (content is not null && level.Content is not null && content.Hash != level.ContentHash) { throw new ArgumentException("Level and supplied registry identities differ.", nameof(content)); }
        LevelId = level.Id; SourceHash = level.SourceHash; ContentHash = (content ?? level.Content)?.Hash ?? level.ContentHash; Grid = level.BakeGrid(); Seed = seed; Log = log; Difficulty = difficulty;
        if (startOverride is not null && (startOverride.Pos.X >= Grid.Width || startOverride.Pos.Z >= Grid.Height)) { throw new ArgumentException("Start override is outside the level.", nameof(startOverride)); }
        Randoms = new RandomStreams(seed); _pathfinder = new AStar(Grid);
        Mission = new MissionState(level.MissionSource, content ?? level.Content, difficulty);
        Entities = Active.AsReadOnly(); Players = _players.AsReadOnly();
        Separation = new SeparationSystem(Grid.Width, Grid.Height);
        _content = content ?? level.Content;
        _surfaceIds = BakeSurfaceIds(level, _content);
        InitializeSpawnStorage();
        for (int i = 0; i < _projectilePool.Length; i++) { _projectilePool[i] = new Entity(0, DefaultArrow, Vec2.Zero, null, 0, 0) { PooledProjectile = true }; }
        _freeProjectiles = _projectilePool.Length;
        int initialMonsters = 0;
        for (int i = 0; i < level.Entities.Count; i++)
        {
            EntityPlacement placement = level.Entities[i];
            if (placement.Definition.Kind == EntityKind.Player && _players.Count == 0 && startOverride is not null)
            { placement = new(placement.Definition, startOverride.Pos, placement.Id, placement.Facing, placement.Tags, placement.Properties); }
            IntVec2 cell = IntVec2.FromPosition(placement.Position);
            if (Active.Count == GameConstants.MaxEntities) { throw new ArgumentException("Entity capacity exceeded.", nameof(level)); }
            if (!Grid.Contains(cell.X, cell.Z) || placement.Definition.RequiresGround && !Grid.IsWalkable(cell.X, cell.Z)) { throw new ArgumentException("Entity is outside bounds or off required ground.", nameof(level)); }
            if (placement.Definition.Kind == EntityKind.Enemy && ++initialMonsters > difficulty.MaxMonsters)
            { RequestMonsterSpawn(placement.Definition, placement.Position, placement.Facing, placement.Tags, new(placement)); continue; }
            var entity = new Entity(NextEntityId++, placement, 0, Grid.HeightAt(placement.Position));
            difficulty.Apply(entity);
            if (entity.Def.Brain is not null) { entity.AiData.WanderTicks = Randoms.Ai.NextInt(180, 361); }
            Active.Add(entity);
            if (entity.Kind == EntityKind.Player)
            {
                if (_players.Count == GameConstants.MaxPlayers) { throw new ArgumentException("Player capacity exceeded.", nameof(level)); }
                _players.Add(entity);
            }
            Emit(new SimEvent(0, SimEventKind.EntitySpawned, entity.Id, entity.Pos, y: entity.Y, text: entity.Def.Id));
        }
        // Authored player_spawn is a marker, not a mutable hero prototype. Instantiate the
        // v1 hero here so every host/editor uses exactly the same initial world state.
        if (_players.Count == 0 && _content is not null && _content.Entities.TryGetValue("core:hero", out Dungeons.Core.Content.EntityDef? hero))
        {
            for (int i = 0; i < level.Entities.Count; i++)
            {
                EntityPlacement marker = level.Entities[i]; if (marker.Definition.Id != "core:player_spawn") { continue; }
                if (Active.Count == GameConstants.MaxEntities) { throw new ArgumentException("No capacity for the spawn-marker hero.", nameof(level)); }
                Vec2 position = startOverride?.Pos ?? marker.Position;
                var placement = new EntityPlacement(hero, position, null, marker.Facing, Array.Empty<string>(), Dungeons.Core.Content.DefinitionData.Empty.Json);
                var player = new Entity(NextEntityId++, placement, 0, Grid.HeightAt(position)); Active.Add(player); _players.Add(player);
                Emit(new SimEvent(0, SimEventKind.EntitySpawned, player.Id, player.Pos, y: player.Y, text: player.Def.Id)); break;
            }
        }
        InitializeObjects();
        if (startOverride is not null) { ApplyStartOverride(startOverride); }
    }
    /// <summary>Authored level identity.</summary>
    public string LevelId { get; }
    /// <summary>Mission seed.</summary>
    public ulong Seed { get; }
    /// <summary>Immutable spawn-time tier settings.</summary>
    public DifficultyProfile Difficulty { get; }
    /// <summary>Exact authored source identity, or empty for code-only scenarios.</summary>
    public string SourceHash { get; }
    /// <summary>Bound content identity, or empty for code-only scenarios.</summary>
    public string ContentHash { get; }
    /// <summary>Completed simulation ticks.</summary>
    public int Tick { get; internal set; }
    /// <summary>Next unused id, included in deterministic state.</summary>
    public int NextEntityId { get; internal set; } = 1;
    /// <summary>Runtime terrain snapshot shared by collision and navigation.</summary>
    public NavGrid Grid { get; }
    /// <summary>Entities in ascending runtime-id order.</summary>
    public IReadOnlyList<Entity> Entities { get; }
    /// <summary>Alive/dead hero slots; no singleton player reference.</summary>
    public IReadOnlyList<Entity> Players { get; }
    /// <summary>Current mission lives.</summary>
    public int Lives { get; internal set; } = GameConstants.InitialLives;
    /// <summary>Enemy deaths in this mission.</summary>
    public int Kills { get; internal set; }
    /// <summary>Player deaths in this mission.</summary>
    public int Deaths { get; internal set; }
    /// <summary>Read-only authored mission/objective snapshot.</summary>
    public MissionState Mission { get; }
    /// <summary>Unserviced oldest-first navigation requests.</summary>
    public int PendingPathRequests => _requestCount;
    /// <summary>Camera focus centroid of alive player entities.</summary>
    public Vec2 CameraFocus
    {
        get
        {
            Vec2 sum = default; int count = 0;
            for (int i = 0; i < _players.Count; i++) { if (_players[i].Alive) { sum += _players[i].Pos; count++; } }
            return count == 0 ? Vec2.Zero : sum / count;
        }
    }
    /// <summary>Finds an active entity without exposing a mutable collection or allocating.</summary>
    public Entity? FindEntity(int id)
    {
        int low = 0, high = Active.Count - 1;
        while (low <= high)
        {
            int middle = (low + high) / 2;
            if (Active[middle].Id == id) { return Active[middle]; }
            if (Active[middle].Id < id) { low = middle + 1; } else { high = middle - 1; }
        }
        return null;
    }
    internal Entity? FindAuthoredEntity(string id)
    { for (int i = 0; i < Active.Count; i++) { if (Active[i].LevelId == id && Active[i].LifetimeTicks != 0) { return Active[i]; } } return null; }
    internal void Despawn(Entity entity)
    {
        if (entity.LifetimeTicks == 0) { return; }
        UnregisterObject(entity);
        Mission.Removed(entity); entity.Alive = entity.Solid = false; entity.LifetimeTicks = 0;
        if (entity.Kind == EntityKind.Enemy) { ReleaseEnemyPath(entity); }
    }
    internal void Emit(in SimEvent ev)
    {
        if (_eventCount == _events.Length)
        {
            if (!_overflowLogged) { Log.Log(LogLevel.Warning, "Sim", "Unconsumed simulation events dropped at capacity."); _overflowLogged = true; }
            return;
        }
        _events[_eventCount++] = ev;
    }
    internal EntityDef ProjectileDefinition(string id)
    {
        if (_content is not null && _content.Entities.TryGetValue(id, out EntityDef? definition) && definition.Projectile is not null) { return definition; }
        if (id == DefaultArrow.Id) { return DefaultArrow; }
        throw new InvalidOperationException("Unresolved projectile definition.");
    }
    internal Entity? SpawnProjectile(Entity owner, EntityDef definition, Vec2 direction)
    {
        int projectiles = 0;
        for (int i = 0; i < Active.Count; i++) { if (Active[i].Kind == EntityKind.Projectile && Active[i].LifetimeTicks != 0) { projectiles++; } }
        if (projectiles >= GameConstants.MaxProjectiles || _freeProjectiles == 0 || Active.Count == GameConstants.MaxEntities || NextEntityId == int.MaxValue) { return null; }
        ProjectileSpec specification = definition.Projectile ?? throw new ArgumentException("Projectile specification required.", nameof(definition));
        Entity shot = _projectilePool[--_freeProjectiles];
        shot.Reset(NextEntityId++, definition, owner.Pos, null, Tick, owner.Y + specification.Height);
        shot.Facing = direction; shot.LifetimeTicks = specification.LifetimeTicks;
        shot.ProjectileData = new() { OwnerId = owner.Id, Faction = owner.Def.Faction, Velocity = direction * specification.Speed, DamageMultiplier = owner.Stats.DamageMultiplier };
        Active.Add(shot);
        Emit(new(Tick, SimEventKind.EntitySpawned, shot.Id, shot.Pos));
        Emit(new(Tick, SimEventKind.ProjectileSpawned, shot.Id, shot.Pos, owner.Id, direction: direction, y: shot.Y, text: owner.Def.Id));
        return shot;
    }
    internal ReadOnlySpan<SimEvent> DrainEvents()
    {
        int count = _eventCount; _eventCount = 0; _overflowLogged = false;
        return _events.AsSpan(0, count);
    }
    internal bool EnqueuePath(int entityId, IntVec2 goal)
    {
        Entity? entity = FindEntity(entityId);
        if (entity is null || entity.Kind is not (EntityKind.Player or EntityKind.Enemy) || _requestCount == _requests.Length) { return false; }
        _requests[(_requestHead + _requestCount) % _requests.Length] = new(entityId, goal);
        _requestCount++;
        return true;
    }
    internal void ServePaths()
    {
        int count = System.Math.Min(_requestCount, GameConstants.MaxPathRequestsPerTick);
        for (int i = 0; i < count; i++)
        {
            PathRequest request = _requests[_requestHead];
            _requestHead = (_requestHead + 1) % _requests.Length; _requestCount--;
            Entity? entity = FindEntity(request.EntityId);
            if (entity is null || !entity.Alive) { continue; }
            PathResult result = _pathfinder.FindPath(IntVec2.FromPosition(entity.Pos), request.Goal, entity.PathBuffer);
            entity.PathCount = result.Count; entity.PathReachedGoal = result.ReachedGoal;
            entity.PathTruncated = result.Truncated;
            entity.AiData.PathPending = false; entity.AiData.HasRoute = true; entity.AiData.Waypoint = 0; entity.AiData.RouteGoal = request.Goal;
        }
    }
    internal void HashRequests(ref Fnv1a hash)
    {
        hash.Add(_requestCount);
        for (int i = 0; i < _requestCount; i++)
        {
            PathRequest request = _requests[(_requestHead + i) % _requests.Length];
            hash.Add(request.EntityId); hash.Add(request.Goal.X); hash.Add(request.Goal.Z);
        }
    }
    internal void RemoveExpired()
    {
        // Stable compaction preserves ascending ids and keeps indices fixed during all earlier systems.
        int write = 0;
        for (int read = 0; read < Active.Count; read++)
        {
            Entity entity = Active[read];
            if (entity.LifetimeTicks == 0)
            {
                UnregisterObject(entity);
                Mission.Removed(entity);
                entity.Alive = false;
                Emit(new SimEvent(Tick, SimEventKind.EntityRemoved, entity.Id, entity.Pos));
                if (entity.PooledProjectile) { _projectilePool[_freeProjectiles++] = entity; }
                if (entity.PooledEntity) { ReleaseEnemyPath(entity); _entityPool[_freeEntities++] = entity; }
                continue;
            }
            Active[write++] = entity;
        }
        if (write < Active.Count) { Active.RemoveRange(write, Active.Count - write); }
    }
}
