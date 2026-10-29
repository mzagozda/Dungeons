using System.Text.Json.Nodes;
using Dungeons.Core.Content;
using Dungeons.Core.Math;
using Dungeons.Core.Util;

namespace Dungeons.Core.Sim;

/// <summary>Authored mission objective lifecycle.</summary>
public enum ObjectiveState
{
    /// <summary>Not yet presented.</summary>
    Hidden,
    /// <summary>Visible and accepting progress.</summary>
    Active,
    /// <summary>Successfully satisfied.</summary>
    Complete,
    /// <summary>Explicit failure.</summary>
    Failed,
}

/// <summary>Read-only authored goal and runtime progress.</summary>
public sealed class RuntimeObjective
{
    internal RuntimeObjective(string id, string text, ObjectiveState state) { Id = id; Text = text; State = state; }
    /// <summary>Authored id.</summary>
    public string Id { get; }
    /// <summary>Authored text or localisation key.</summary>
    public string Text { get; }
    /// <summary>Current lifecycle state.</summary>
    public ObjectiveState State { get; internal set; }
    /// <summary>Latest activation or survive restart tick.</summary>
    public int ActivatedTick { get; internal set; }
    internal string Kind = "flag", Target = "";
    internal int Tag = -1, Flag = -1, Minimum = 1, Duration, Version;
    internal bool AwaitingRespawn;
}

/// <summary>Frozen mission-end counters; collecting them never mutates a profile.</summary>
public readonly struct MissionResults(int ticks, int emeralds, int kills, int deaths, int xp)
{
    /// <summary>Elapsed simulation ticks.</summary>
    public int Ticks { get; } = ticks;
    /// <summary>Total collected emeralds.</summary>
    public int Emeralds { get; } = emeralds;
    /// <summary>Enemies defeated.</summary>
    public int Kills { get; } = kills;
    /// <summary>Hero deaths.</summary>
    public int Deaths { get; } = deaths;
    /// <summary>Experience earned.</summary>
    public int Xp { get; } = xp;
}

/// <summary>Read-only declarative spawner progress.</summary>
public readonly struct SpawnerState(int entityId, int remaining, int nextTick, bool active)
{
    /// <summary>Spawner's runtime entity id.</summary>
    public int EntityId { get; } = entityId;
    /// <summary>Monsters not yet requested.</summary>
    public int Remaining { get; } = remaining;
    /// <summary>Earliest next spawn tick.</summary>
    public int NextTick { get; } = nextTick;
    /// <summary>Whether activation occurred.</summary>
    public bool Active { get; } = active;
}

/// <summary>Compiled deterministic mission program; no JSON parsing or allocation in ticks.</summary>
public sealed partial class MissionState
{
    private readonly RuntimeObjective[] _objectives;
    private readonly Dictionary<string, int> _objectiveIds = new(StringComparer.Ordinal), _flagIds = new(StringComparer.Ordinal), _tagIds = new(StringComparer.Ordinal), _triggerIds = new(StringComparer.Ordinal);
    private readonly List<FlagSlot> _flags = [];
    private readonly List<TagSlot> _tags = [];
    private readonly RuntimeTrigger[] _triggers;
    private readonly RuntimeSpawner[] _spawners = new RuntimeSpawner[GameConstants.MaxEntities];
    private int _spawnerCount;
    private readonly ContentRegistry? _content;
    private readonly DifficultyProfile _difficulty;
    private readonly string _exit;
    private readonly int[] _requires;
    private readonly ulong _programHash;
    private int _lastDeaths, _lastLockedTick = -60;
    private bool _initialized;
    private sealed class FlagSlot(bool value) { internal bool Value = value; internal int Version; }
    private sealed class TagSlot(string name) { internal readonly string Name = name; internal int Spawned, Alive, Expected; }
    internal MissionState(JsonObject? source, ContentRegistry? content = null, DifficultyProfile? difficulty = null)
    {
        _content = content; _difficulty = difficulty ?? DifficultyProfile.Default;
        var identity = new Fnv1a(); identity.Add(source is null ? "{}" : JsonData.Stringify(source, canonical: true)); _programHash = identity.Value;
        if (source?["flags"] is JsonObject flags)
        { foreach (var flag in flags) { _flagIds.Add(flag.Key, _flags.Count); _flags.Add(new(JsonData.Boolean(flag.Value))); } }
        var objectives = new List<RuntimeObjective>();
        if (source?["objectives"] is JsonArray authored)
        {
            foreach (JsonNode? node in authored)
            {
                var objective = new RuntimeObjective(JsonData.Text(node!["id"]), JsonData.Localized(node["text"]), StateOf(JsonData.Text(node["initialState"], "hidden")))
                {
                    Kind = JsonData.Text(node["type"], "flag"), Target = JsonData.Text(node["target"]),
                    Tag = node["tag"] is null ? -1 : TagIndex(JsonData.Text(node["tag"])), Flag = node["flag"] is null ? -1 : _flagIds[JsonData.Text(node["flag"])],
                    Minimum = JsonData.Integer(node["minSpawned"], 1), Duration = Ticks(node["seconds"]),
                };
                _objectiveIds.Add(objective.Id, objectives.Count); objectives.Add(objective);
            }
        }
        _objectives = objectives.ToArray(); Objectives = Array.AsReadOnly(_objectives);
        _triggers = CompileTriggers(source);
        _exit = JsonData.Text(source?["completion"]?["exit"]);
        _requires = source?["completion"]?["requires"] is JsonArray requires ? requires.Select(id => _objectiveIds[JsonData.Text(id)]).ToArray() : [];
    }
    /// <summary>Objectives in authored HUD order.</summary>
    public IReadOnlyList<RuntimeObjective> Objectives { get; }
    /// <summary>First active objective's text.</summary>
    public string PrimaryText { get { foreach (RuntimeObjective objective in _objectives) { if (objective.State == ObjectiveState.Active) { return objective.Text; } } return ""; } }
    /// <summary>Current transient authored message.</summary>
    public string Message { get; private set; } = "";
    /// <summary>Message ticks left.</summary>
    public int MessageTicksRemaining { get; private set; }
    /// <summary>Whether completion requirements still lock the exit.</summary>
    public bool ExitLocked { get; private set; }
    /// <summary>Core's requested terminal outcome.</summary>
    public SessionState Outcome { get; private set; } = SessionState.Running;
    /// <summary>Frozen counters, present after a terminal outcome.</summary>
    public MissionResults? Results { get; private set; }
    /// <summary>Reads one authored flag.</summary>
    public bool Flag(string id) => _flagIds.TryGetValue(id, out int index) && _flags[index].Value;
    /// <summary>Returns a spawner snapshot without exposing mutable program storage.</summary>
    public SpawnerState Spawner(int entityId)
    { for (int i = 0; i < _spawnerCount; i++) { RuntimeSpawner spawner = _spawners[i]; if (spawner.EntityId == entityId) { return new(entityId, spawner.Remaining, spawner.NextTick, spawner.Active); } } return default; }
    internal void Initialize(World world)
    {
        if (_initialized) { throw new InvalidOperationException("Mission already initialized."); }
        _initialized = true; CompileSpawners(world);
        foreach (Entity entity in world.Entities) { Register(entity); }
        StepTriggers(world); if (Outcome == SessionState.Running) { StepObjectives(world); }
    }
    internal void Register(Entity entity)
    {
        if (entity.MissionRegistered || !entity.Alive) { return; }
        for (int i = 0; i < entity.Tags.Count; i++)
        { if (_tagIds.TryGetValue(entity.Tags[i], out int index)) { TagSlot slot = _tags[index]; slot.Spawned = Saturate(slot.Spawned, 1); slot.Alive++; entity.MissionRegistered = true; } }
    }
    internal void Removed(Entity entity)
    {
        if (!entity.MissionRegistered) { return; }
        for (int i = 0; i < entity.Tags.Count; i++) { if (_tagIds.TryGetValue(entity.Tags[i], out int index)) { _tags[index].Alive--; } }
        entity.MissionRegistered = false;
    }
    private int TagIndex(string name)
    { if (_tagIds.TryGetValue(name, out int index)) { return index; } index = _tags.Count; _tagIds.Add(name, index); _tags.Add(new(name)); return index; }
    private bool AllDead(World world, int index, int minimum, bool includeExpected = false)
    { TagSlot tag = _tags[index]; return tag.Spawned >= (includeExpected ? System.Math.Max(minimum, tag.Expected) : minimum) && tag.Alive == 0 && !world.HasPendingTag(tag.Name); }
    private static int Saturate(int value, int amount) => (int)System.Math.Min(int.MaxValue, (long)value + amount);
    private static int Ticks(JsonNode? value, double fallback = 0) => MathUtil.SecondsToTicks((float)JsonData.Number(value, fallback));
    private static ObjectiveState StateOf(string state) => state switch
    { "hidden" => ObjectiveState.Hidden, "active" => ObjectiveState.Active, "complete" => ObjectiveState.Complete, "failed" => ObjectiveState.Failed, _ => throw new ArgumentException("Unknown objective state.") };
    private bool SetObjective(World world, int index, ObjectiveState state)
    {
        RuntimeObjective objective = _objectives[index]; if (objective.State == state) { return false; }
        bool legal = objective.State == ObjectiveState.Hidden && state == ObjectiveState.Active || objective.State == ObjectiveState.Active && state is ObjectiveState.Complete or ObjectiveState.Failed;
        if (!legal) { world.Log.Log(LogLevel.Warning, "Sim", "Illegal objective transition ignored."); return false; }
        objective.State = state; objective.Version++; objective.AwaitingRespawn = false;
        if (state == ObjectiveState.Active) { objective.ActivatedTick = world.Tick; }
        world.Emit(new(world.Tick, SimEventKind.ObjectiveChanged, 0, default, sourceId: index, amount: (int)state)); return true;
    }
    internal void StepObjectives(World world)
    {
        bool died = world.Deaths != _lastDeaths; _lastDeaths = world.Deaths;
        foreach (RuntimeObjective objective in _objectives)
        {
            if (objective.State != ObjectiveState.Active) { continue; }
            if (died && objective.Kind == "survive") { objective.AwaitingRespawn = true; }
            bool complete = objective.Kind switch
            {
                "reach" => Reached(world, objective.Target),
                "interact" => world.FindAuthoredEntity(objective.Target) is Entity target && (target.WorldObject.Opened || target.Kind == EntityKind.Gate && target.WorldObject.Gate is GatePhase.Open or GatePhase.Opening),
                "kill_tag" => objective.Tag >= 0 && AllDead(world, objective.Tag, objective.Minimum, true),
                "flag" => objective.Flag >= 0 && _flags[objective.Flag].Value,
                "survive" => !objective.AwaitingRespawn && world.Tick - objective.ActivatedTick >= objective.Duration && AnyAlive(world),
                _ => false,
            };
            if (complete) { SetObjective(world, _objectiveIds[objective.Id], ObjectiveState.Complete); }
        }
        ExitLocked = false; foreach (int index in _requires) { if (_objectives[index].State != ObjectiveState.Complete) { ExitLocked = true; break; } }
        if (_exit.Length == 0 || !Reached(world, _exit) || Outcome != SessionState.Running) { return; }
        if (!ExitLocked) { End(world, false); }
        else if (world.Tick - _lastLockedTick >= 60)
        { _lastLockedTick = world.Tick; Entity? exit = world.FindAuthoredEntity(_exit); world.Emit(new(world.Tick, SimEventKind.ExitLocked, exit?.Id ?? 0, exit?.Pos ?? default)); }
    }
    internal void Respawned(World world, Entity hero)
    {
        if (!hero.MissionRegistered)
        { for (int i = 0; i < hero.Tags.Count; i++) { if (_tagIds.TryGetValue(hero.Tags[i], out int index)) { _tags[index].Alive++; hero.MissionRegistered = true; } } }
        foreach (RuntimeObjective objective in _objectives) { if (objective.State == ObjectiveState.Active && objective.Kind == "survive") { objective.ActivatedTick = world.Tick; objective.AwaitingRespawn = false; } }
    }
    private static bool AnyAlive(World world)
    { for (int p = 0; p < world.Players.Count; p++) { if (world.Players[p].Alive) { return true; } } return false; }
    private static bool Reached(World world, string id)
    {
        Entity? target = world.FindAuthoredEntity(id); if (target is not { Alive: true }) { return false; }
        for (int p = 0; p < world.Players.Count; p++) { Entity hero = world.Players[p]; if (hero.Alive && Vec2.DistanceSquared(hero.Pos, target.Pos) <= target.ObjectRules.Radius * target.ObjectRules.Radius) { return true; } }
        return false;
    }
    internal void End(World world, bool failed)
    {
        if (Outcome != SessionState.Running) { return; }
        Outcome = failed ? SessionState.Failed : SessionState.Won; int emeralds = 0, xp = 0;
        for (int p = 0; p < world.Players.Count; p++) { Entity hero = world.Players[p]; emeralds = Saturate(emeralds, hero.Inventory.Emeralds); xp = Saturate(xp, hero.Experience); }
        Results = new(world.Tick, emeralds, world.Kills, world.Deaths, xp);
        world.Emit(new(world.Tick, failed ? SimEventKind.MissionFailed : SimEventKind.MissionComplete, 0, default, results: Results.Value));
    }
    internal void Hash(ref Fnv1a hash)
    {
        hash.Add(_programHash); hash.Add(_initialized); hash.Add((int)Outcome); hash.Add(_lastDeaths); hash.Add(_lastLockedTick); hash.Add(ExitLocked);
        hash.Add(_objectives.Length); foreach (RuntimeObjective objective in _objectives) { hash.Add((int)objective.State); hash.Add(objective.ActivatedTick); hash.Add(objective.Version); hash.Add(objective.AwaitingRespawn); }
        hash.Add(Message.Length); hash.Add(Message); hash.Add(MessageTicksRemaining);
        hash.Add(_flags.Count); foreach (FlagSlot flag in _flags) { hash.Add(flag.Value); hash.Add(flag.Version); }
        hash.Add(_tags.Count); foreach (TagSlot tag in _tags) { hash.Add(tag.Name.Length); hash.Add(tag.Name); hash.Add(tag.Spawned); hash.Add(tag.Alive); hash.Add(tag.Expected); }
        foreach (RuntimeTrigger trigger in _triggers) { trigger.Hash(ref hash); }
        HashGateTriggers(ref hash);
        hash.Add(_spawnerCount);
        for (int i = 0; i < _spawnerCount; i++) { RuntimeSpawner spawner = _spawners[i]; hash.Add(spawner.EntityId); hash.Add(spawner.Template.Identity); hash.Add(spawner.Remaining); hash.Add(spawner.NextTick); hash.Add(spawner.Active); hash.Add(spawner.Finished); }
        hash.Add(Results.HasValue); if (Results is MissionResults result) { hash.Add(result.Ticks); hash.Add(result.Emeralds); hash.Add(result.Kills); hash.Add(result.Deaths); hash.Add(result.Xp); }
    }
}
