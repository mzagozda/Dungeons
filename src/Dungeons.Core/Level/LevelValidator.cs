using System.Text.Json;
using System.Text.Json.Nodes;
using Dungeons.Core.Content;
using Dungeons.Core.Math;
using Dungeons.Core.Sim;
using Dungeons.Core.Util;

namespace Dungeons.Core.Level;

/// <summary>Structural and semantic validation against the production content/navigation rules.</summary>
public static class LevelValidator
{
    /// <summary>Validates an independently edited authored document without changing it.</summary>
    public static IReadOnlyList<Diagnostic> Validate(LevelDocument document, ContentRegistry content)
    {
        if (document is null) { throw new ArgumentNullException(nameof(document)); }
        if (content is null) { throw new ArgumentNullException(nameof(content)); }
        JsonObject source = document.Source ?? throw new InvalidOperationException("Validation needs an authored document.");
        var diagnostics = new List<Diagnostic>();
        try
        {
            if (!content.IsValid) { diagnostics.Add(new(DiagnosticSeverity.Error, DiagnosticCode.LVL0003, "Content registry contains errors.")); }
            else if (JsonData.Integer(source["formatVersion"], -1) != LevelMigrations.CurrentVersion) { diagnostics.Add(new(DiagnosticSeverity.Error, DiagnosticCode.LVL0002, "Unsupported formatVersion; this build supports v1.", "/formatVersion")); }
            else
            {
                LevelLoader.ValidateStructure(source, diagnostics);
                if (!diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
                {
                    // Source is already a parsed authoring tree. Validation reads it and
                    // bakes an independent working grid; no serialization, hashing,
                    // migration copy or environment construction is needed here.
                    var working = new LevelDocument(JsonData.Integer(source["size"]!["width"]), JsonData.Integer(source["size"]!["height"]), JsonData.Text(source["id"]));
                    BindAndValidate(working, source, content, diagnostics);
                }
            }
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException or OverflowException)
        { diagnostics.Add(new(DiagnosticSeverity.Error, DiagnosticCode.LVL0001, error.Message)); }
        if (document.Assets.Paths.Count > 0) { diagnostics.Add(MemoryAssetSource.UnsupportedWarning); }
        return diagnostics.AsReadOnly();
    }
    /// <summary>Validates an authored document using its bound content registry.</summary>
    public static IReadOnlyList<Diagnostic> Validate(LevelDocument document)
        => Validate(document, BoundContent(document));
    /// <summary>Checks terrain/properties/stairs in a clipped cell region and its one-cell dependency halo.
    /// Shared palette/index diagnostics are included. Mission references and global reachability use Validate.</summary>
    public static IReadOnlyList<Diagnostic> ValidateCells(LevelDocument document, CellRect rectangle)
        => ValidateCells(document, rectangle, BoundContent(document));
    /// <summary>Runs regional terrain checks with an explicit content registry, without changing the document.</summary>
    public static IReadOnlyList<Diagnostic> ValidateCells(LevelDocument document, CellRect rectangle, ContentRegistry content)
    {
        if (document is null) { throw new ArgumentNullException(nameof(document)); }
        if (content is null) { throw new ArgumentNullException(nameof(content)); }
        JsonObject source = document.Source ?? throw new InvalidOperationException("Validation needs an authored document.");
        var diagnostics = new List<Diagnostic>();
        if (!content.IsValid) { diagnostics.Add(new(DiagnosticSeverity.Error, DiagnosticCode.LVL0003, "Content registry contains errors.")); return diagnostics.AsReadOnly(); }
        try
        {
            int width = JsonData.Integer(source["size"]?["width"]), height = JsonData.Integer(source["size"]?["height"]);
            if (width is < 1 or > GameConstants.MaxLevelSize || height is < 1 or > GameConstants.MaxLevelSize)
            { diagnostics.Add(new(DiagnosticSeverity.Error, DiagnosticCode.LVL0003, "Invalid level dimensions.", "/size")); return diagnostics.AsReadOnly(); }
            CellRect clipped = rectangle.Clip(width, height); if (clipped.IsEmpty) { return diagnostics.AsReadOnly(); }
            new TerrainValidation(source, content, diagnostics, width, height, CellOverrideIndex.Get(document, source, width, height)).Validate(clipped);
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException or OverflowException)
        { diagnostics.Add(new(DiagnosticSeverity.Error, DiagnosticCode.LVL0001, error.Message)); }
        return diagnostics.AsReadOnly();
    }
    private static ContentRegistry BoundContent(LevelDocument document)
    {
        if (document is null) { throw new ArgumentNullException(nameof(document)); }
        return document.Content ?? throw new ArgumentException("A bound authored document is required.", nameof(document));
    }
    internal static void BindAndValidate(LevelDocument level, JsonObject root, ContentRegistry content, List<Diagnostic> diagnostics, bool editing = false)
    {
        var context = new Context(level, root, content, diagnostics, editing);
        context.Terrain();
        if (!editing && diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)) { return; }
        context.Entities(); context.Logic(); context.Reachability();
    }
    private sealed class Context(LevelDocument level, JsonObject root, ContentRegistry content, List<Diagnostic> diagnostics, bool editing)
    {
        private readonly Dictionary<string, JsonObject> _entities = new(StringComparer.Ordinal), _objectives = new(StringComparer.Ordinal), _triggers = new(StringComparer.Ordinal);
        private readonly HashSet<string> _tags = new(StringComparer.Ordinal);
        private readonly HashSet<string> _implicitTriggers = new(StringComparer.Ordinal);
        private readonly List<(JsonObject json, EntityDef def, Vec2 position, JsonObject props)> _placements = [];
        private NavGrid? _grid;
        private NavGrid Grid => _grid ??= level.BakeGrid();
        private Vec2? _spawn;

        internal void Terrain()
        {
            new TerrainValidation(root, content, diagnostics, level.Width, level.Height, CellOverrideIndex.Get(level, root, level.Width, level.Height)).Bind(level, editing);
        }

        internal void Entities()
        {
            JsonArray entities = root["entities"]!.AsArray(); NavGrid grid = Grid; int spawns = 0, monsters = 0, checkpoints = 0;
            for (int i = 0; i < entities.Count; i++)
            {
                JsonObject source = entities[i]!.AsObject(); string path = "/entities/" + JsonData.Index(i);
                string id = JsonData.Text(source["id"]), type = JsonData.Text(source["type"]);
                if (id.Length > 0 && !_entities.TryAdd(id, source)) { Error(DiagnosticCode.LVL1013, "Duplicate entity id.", path + "/id"); }
                if (!content.Entities.TryGetValue(type, out EntityDef? def)) { Error(DiagnosticCode.LVL1011, "Unknown entity type: " + type, path + "/type"); continue; }
                Vec2 position = new((float)JsonData.Number(source["x"]), (float)JsonData.Number(source["z"]));
                bool inside = position.IsFinite && position.X >= 0 && position.Z >= 0 && position.X < level.Width && position.Z < level.Height;
                bool ground = def.RequiresGround;
                IntVec2 cell = inside ? IntVec2.FromPosition(position) : default;
                if (!inside || ground && !grid.IsWalkable(cell.X, cell.Z)) { Error(DiagnosticCode.LVL1012, "Entity is outside bounds or off required walkable ground.", path, inside ? cell : null); }
                JsonObject props = def.Data.Properties.Normalize(source["props"] as JsonObject, diagnostics, path + "/props");
                if (inside && def.Kind == EntityKind.Gate)
                {
                    int width = System.Math.Clamp(JsonData.Integer(props["width"], 2), 1, 8); bool alongX = JsonData.Text(props["axis"], "x") == "x";
                    if (!new GateArea(position, width, alongX).InBounds(grid))
                    { Error(DiagnosticCode.LVL1012, "Gate footprint extends outside the level bounds.", path, cell); }
                }
                _placements.Add((source, def, position, props));
                if (type == "core:player_spawn") { spawns++; _spawn = inside ? position : null; }
                if (def.Kind == EntityKind.Enemy) { monsters++; }
                if (def.Kind == EntityKind.Checkpoint) { checkpoints++; }
                AddTags(source["tags"] as JsonArray);
                Vec2 facing = JsonData.Text(source["facing"], "south") switch { "north" => Vec2.North, "east" => new(1, 0), "west" => new(-1, 0), _ => new(0, 1) };
                if (source["facing"] is JsonArray vector)
                {
                    facing = new Vec2((float)JsonData.Number(vector[0]), (float)JsonData.Number(vector[1])).Normalized;
                    if (facing == Vec2.Zero) { Error(DiagnosticCode.LVL1011, "Facing vector must be nonzero.", path + "/facing"); }
                }
                using JsonDocument properties = JsonDocument.Parse(JsonData.Stringify(props));
                string[] tags = (source["tags"] as JsonArray)?.Select(tag => JsonData.Text(tag)).ToArray() ?? [];
                if (inside) { level.AddBakedEntity(new EntityPlacement(def, position, id.Length == 0 ? null : id, facing, Array.AsReadOnly(tags), properties.RootElement.Clone())); }
            }
            if (spawns != 1) { Error(DiagnosticCode.LVL1010, "Exactly one core:player_spawn is required.", "/entities"); _spawn = null; }
            if (checkpoints == 0) { Warn(DiagnosticCode.LVL1040, "Level has no checkpoint.", "/entities"); }
            if (monsters > GameConstants.MaxMonsters) { Warn(DiagnosticCode.LVL1050, "Authored monsters exceed the live cap; surplus spawns are deferred.", "/entities"); }
            foreach (var gate in _placements)
            {
                if (gate.def.Kind != EntityKind.Gate || JsonData.Text(gate.props["state"], "closed") == "open") { continue; }
                var area = new GateArea(gate.position, System.Math.Clamp(JsonData.Integer(gate.props["width"], 2), 1, 8), JsonData.Text(gate.props["axis"], "x") == "x");
                foreach (var actor in _placements)
                {
                    if (actor.def.Kind is not (EntityKind.Player or EntityKind.Enemy) && actor.def.Id != "core:player_spawn") { continue; }
                    float radius = actor.def.Id == "core:player_spawn" ? content.Entities.TryGetValue("core:hero", out EntityDef? hero) ? hero.Radius : GameConstants.DefaultHeroRadius : actor.def.Radius;
                    if (area.Overlaps(actor.position, radius)) { Error(DiagnosticCode.LVL1012, "Actor overlaps an initially closed gate footprint.", Pointer(actor.json), IntVec2.FromPosition(actor.position)); }
                }
            }
        }

        internal void Logic()
        {
            Index(root["objectives"] as JsonArray, _objectives, "/objectives"); Index(root["triggers"] as JsonArray, _triggers, "/triggers");
            foreach (var placement in _placements)
            {
                if (placement.def.Kind != EntityKind.Gate || JsonData.Text(placement.props["openWhen"]).Length == 0) { continue; }
                string implicitId = "__gate_" + JsonData.Text(placement.json["id"]); _implicitTriggers.Add(implicitId);
                if (_triggers.ContainsKey(implicitId)) { Error(DiagnosticCode.LVL1020, "Authored trigger id collides with a generated gate trigger.", Pointer(placement.json) + "/props/openWhen"); }
            }
            var activated = new HashSet<string>(StringComparer.Ordinal);
            foreach (var placement in _placements)
            {
                string id = JsonData.Text(placement.json["id"]), type = placement.def.Id;
                if (placement.def.Kind == EntityKind.SpawnMarker && placement.def.Data.Properties.Definitions.ContainsKey("entity"))
                {
                    string target = JsonData.Text(placement.props["entity"]), tag = JsonData.Text(placement.props["tag"]);
                    if (tag.Length > 0) { _tags.Add(tag); }
                    IntVec2 cell = IntVec2.FromPosition(placement.position);
                    if (!content.Entities.TryGetValue(target, out EntityDef? enemy) || enemy.Kind != EntityKind.Enemy || !Grid.IsWalkable(cell.X, cell.Z))
                    { Error(DiagnosticCode.LVL1026, "Spawner needs an enemy prototype and walkable marker.", Pointer(placement.json) + "/props/entity"); }
                }
            }
            // Produce the complete tag set before resolving forward references.
            foreach (JsonObject trigger in _triggers.Values)
            {
                foreach (JsonNode? node in trigger["actions"]!.AsArray())
                {
                    JsonObject action = node!.AsObject();
                    if (JsonData.Text(action["type"]) == "spawn")
                    {
                        AddTags(action["entity"]?["tags"] as JsonArray);
                        if (content.Entities.TryGetValue(JsonData.Text(action["entity"]?["type"]), out EntityDef? spawned) && spawned.Kind == EntityKind.SpawnMarker && spawned.DefaultProperties.TryGetProperty("entity", out _))
                        {
                            string tag = JsonData.Text(action["entity"]?["props"]?["tag"], spawned.DefaultProperties.TryGetProperty("tag", out JsonElement defaultTag) ? defaultTag.GetString()! : "");
                            if (tag.Length > 0) { _tags.Add(tag); }
                        }
                    }
                    if (JsonData.Text(action["type"]) == "objective" && JsonData.Text(action["state"]) == "active") { activated.Add(JsonData.Text(action["id"])); }
                }
            }
            foreach (var placement in _placements)
            {
                string path = Pointer(placement.json) + "/props";
                foreach (PropertyDefinition property in placement.def.Data.Properties.Definitions.Values)
                {
                    if (property.Type != "ref") { continue; }
                    string value = JsonData.Text(placement.props[property.Name]); if (value.Length == 0) { continue; }
                    if (property.Name == "openWhen") { if (!_objectives.ContainsKey(value)) { Error(DiagnosticCode.LVL1025, "Unknown gate objective.", path + "/openWhen"); } continue; }
                    if (placement.def.Kind == EntityKind.SpawnMarker && property.Name == "entity") { continue; }
                    if (!ReferenceExists(property.RefKind, value)) { Error(DiagnosticCode.LVL1020, "Unknown " + property.RefKind + " reference: " + value, path + "/" + property.Name); }
                }
            }
            foreach (KeyValuePair<string, JsonObject> entry in _triggers)
            {
                JsonObject trigger = entry.Value; string path = Pointer(trigger);
                JsonObject ev = trigger["event"]!.AsObject(); string eventType = JsonData.Text(ev["type"]);
                CheckSeconds(ev, path + "/event");
                switch (eventType)
                {
                    case "objective": Require(_objectives, ev, "id", path + "/event"); break;
                    case "interact": case "checkpoint":
                        Require(_entities, ev, "id", path + "/event");
                        CheckTargetKind(ev, "id", path + "/event", eventType == "checkpoint" ? [EntityKind.Checkpoint] : [EntityKind.Chest, EntityKind.Gate]); break;
                    case "flag": RequireFlag(ev, path + "/event"); break;
                    case "entities_dead": if (!_tags.Contains(JsonData.Text(ev["tag"]))) { Warn(DiagnosticCode.LVL1023, "Tag is never produced.", path + "/event/tag"); } break;
                }
                if (trigger["conditions"] is JsonArray conditions) { for (int c = 0; c < conditions.Count; c++) { Condition(conditions[c]!.AsObject(), path + "/conditions/" + JsonData.Index(c)); } }
                JsonArray actions = trigger["actions"]!.AsArray();
                for (int a = 0; a < actions.Count; a++)
                {
                    JsonObject action = actions[a]!.AsObject(); string actionPath = path + "/actions/" + JsonData.Index(a); string type = JsonData.Text(action["type"]);
                    CheckSeconds(action, actionPath);
                    switch (type)
                    {
                        case "objective": Require(_objectives, action, "id", actionPath); break;
                        case "spawn":
                            Require(_entities, action, "at", actionPath);
                            string prototype = JsonData.Text(action["entity"]?["type"]);
                            if (!content.Entities.TryGetValue(prototype, out EntityDef? def)) { Error(DiagnosticCode.LVL1020, "Unknown spawn prototype.", actionPath + "/entity/type"); }
                            else
                            {
                                JsonObject spawnProps = def.Data.Properties.Normalize(action["entity"]?["props"] as JsonObject, diagnostics, actionPath + "/entity/props");
                                if (def.Kind == EntityKind.SpawnMarker && spawnProps["entity"] is not null && (!content.Entities.TryGetValue(JsonData.Text(spawnProps["entity"]), out EntityDef? monster) || monster.Kind != EntityKind.Enemy))
                                { Error(DiagnosticCode.LVL1026, "Spawner needs an enemy prototype.", actionPath + "/entity/props/entity"); }
                                foreach (PropertyDefinition property in def.Data.Properties.Definitions.Values)
                                {
                                    if (property.Type != "ref" || def.Kind == EntityKind.SpawnMarker && property.Name == "entity") { continue; }
                                    string reference = JsonData.Text(spawnProps[property.Name]); if (reference.Length == 0 || ReferenceExists(property.RefKind, reference)) { continue; }
                                    Error(property.Name == "openWhen" ? DiagnosticCode.LVL1025 : DiagnosticCode.LVL1020, "Unknown spawn property reference.", actionPath + "/entity/props/" + property.Name);
                                }
                                if (def.Kind is EntityKind.Player or EntityKind.Projectile) { Error(DiagnosticCode.LVL1020, "Players and projectiles require their dedicated factories, not a spawn action.", actionPath + "/entity/type"); }
                            }
                            if (_entities.TryGetValue(JsonData.Text(action["at"]), out JsonObject? marker) && content.Entities.TryGetValue(JsonData.Text(marker["type"]), out EntityDef? markerDef) && markerDef.Kind is not (EntityKind.Marker or EntityKind.SpawnMarker)) { Error(DiagnosticCode.LVL1020, "Spawn at must reference a marker.", actionPath + "/at"); }
                            break;
                        case "sound": case "music": if (!content.Sounds.ContainsKey(JsonData.Text(action["id"]))) { Error(DiagnosticCode.LVL1020, "Unknown audio id.", actionPath + "/id"); } break;
                        case "set_state":
                            Require(_entities, action, "entity", actionPath);
                            if (_entities.TryGetValue(JsonData.Text(action["entity"]), out JsonObject? stateTarget) && content.Entities.TryGetValue(JsonData.Text(stateTarget["type"]), out EntityDef? stateDef))
                            {
                                string state = JsonData.Text(action["state"]);
                                bool supported = stateDef.Kind switch
                                {
                                    EntityKind.Gate => state is "open" or "closed", EntityKind.Chest => state == "open", EntityKind.Checkpoint => state == "active",
                                    EntityKind.SpawnMarker => state == "active" && stateDef.Data.Properties.Definitions.ContainsKey("entity"), _ => false,
                                };
                                if (!supported) { Error(DiagnosticCode.LVL1020, "Entity does not support the requested state.", actionPath + "/state"); }
                            }
                            break;
                        case "set_flag": RequireFlag(action, actionPath); break;
                        case "checkpoint": Require(_entities, action, "id", actionPath); CheckTargetKind(action, "id", actionPath, [EntityKind.Checkpoint]); break;
                        case "remove_entity":
                            Require(_entities, action, "id", actionPath);
                            if (_entities.TryGetValue(JsonData.Text(action["id"]), out JsonObject? removed) && content.Entities.TryGetValue(JsonData.Text(removed["type"]), out EntityDef? removedDef) && removedDef.Kind == EntityKind.Player)
                            { Error(DiagnosticCode.LVL1020, "Player slots cannot be removed by a mission action.", actionPath + "/id"); }
                            break;
                        case "enable_trigger": case "disable_trigger": Require(_triggers, action, "id", actionPath); break;
                    }
                }
            }
            foreach (KeyValuePair<string, JsonObject> entry in _objectives)
            {
                JsonObject objective = entry.Value; string type = JsonData.Text(objective["type"]), path = Pointer(objective);
                CheckSeconds(objective, path);
                if (JsonData.Text(objective["initialState"], "hidden") != "active" && !activated.Contains(entry.Key)) { Warn(DiagnosticCode.LVL1022, "Objective is never activated.", path); }
                if (type is "reach" or "interact") { Require(_entities, objective, "target", path); }
                if (type == "reach") { CheckTargetKind(objective, "target", path, [EntityKind.Checkpoint, EntityKind.Marker, EntityKind.SpawnMarker, EntityKind.Exit]); }
                if (type == "interact") { CheckTargetKind(objective, "target", path, [EntityKind.Chest, EntityKind.Gate]); }
                if (type == "flag" && objective["flag"] is not null) { RequireFlag(objective, path); }
                if (type == "kill_tag" && !_tags.Contains(JsonData.Text(objective["tag"]))) { Error(DiagnosticCode.LVL1020, "Unknown objective tag.", path + "/tag"); }
                if (type == "survive" && JsonData.Number(objective["seconds"]) <= 0) { Error(DiagnosticCode.LVL1024, "Survive objective requires positive seconds.", path + "/seconds"); }
            }
            JsonObject completion = root["completion"]!.AsObject();
            if (JsonData.Text(completion["type"]) == "exit" && (!_entities.TryGetValue(JsonData.Text(completion["exit"]), out JsonObject? exit) || !content.Entities.TryGetValue(JsonData.Text(exit["type"]), out EntityDef? exitDef) || exitDef.Kind != EntityKind.Exit))
            { Error(DiagnosticCode.LVL1021, "Completion requires a known exit entity.", "/completion/exit"); }
            if (completion["requires"] is JsonArray requires)
            { foreach (JsonNode? required in requires) { if (!_objectives.ContainsKey(JsonData.Text(required))) { Error(DiagnosticCode.LVL1021, "Completion requires an unknown objective.", "/completion/requires"); } } }
            if (root["environment"] is JsonObject environment)
            { foreach (string key in new[] { "music", "ambience" }) { if (environment[key] is JsonNode audio && !content.Sounds.ContainsKey(JsonData.Text(audio))) { Error(DiagnosticCode.LVL1020, "Unknown environment audio.", "/environment/" + key); } } }
        }
        internal void Reachability()
        {
            if (_spawn is not Vec2 spawn || diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)) { return; }
            // Gate footprints are deliberately absent: this is the all-gates-open graph.
            NavGrid grid = Grid; var discoveries = new bool[grid.Count]; var pathfinder = new AStar(grid, discoveries); var path = new IntVec2[GameConstants.MaxPathWaypoints];
            foreach (var placement in _placements)
            {
                if (placement.def.Kind is not (EntityKind.Enemy or EntityKind.Exit)) { continue; }
                IntVec2 target = IntVec2.FromPosition(placement.position);
                // Every discovered cell has a real path from this same spawn,
                // even when it was only a frontier node in an earlier search.
                if (grid.Contains(target.X, target.Z) && discoveries[target.Z * grid.Width + target.X]) { continue; }
                bool reached = pathfinder.TryGetLastReachability(target, out bool known) ? known : pathfinder.FindPath(IntVec2.FromPosition(spawn), target, path).ReachedGoal;
                if (reached) { continue; }
                string pointer = Pointer(placement.json);
                if (placement.def.Kind == EntityKind.Enemy) { Warn(DiagnosticCode.LVL1014, "Monster has no route to player spawn.", pointer); }
                else
                {
                    Warn(DiagnosticCode.LVL1030, "Exit is unreachable on the open-gate graph.", pointer);
                    Error(DiagnosticCode.LVL1031, "Exit is unreachable even with every gate open.", pointer);
                }
            }
        }
        private void CheckTargetKind(JsonObject source, string key, string path, ReadOnlySpan<EntityKind> allowed)
        {
            if (!_entities.TryGetValue(JsonData.Text(source[key]), out JsonObject? target) || !content.Entities.TryGetValue(JsonData.Text(target["type"]), out EntityDef? definition)) { return; }
            for (int i = 0; i < allowed.Length; i++) { if (definition.Kind == allowed[i]) { return; } }
            Error(DiagnosticCode.LVL1020, "Referenced entity has an incompatible kind.", path + "/" + key);
        }
        private void Condition(JsonObject condition, string path)
        {
            switch (JsonData.Text(condition["type"]))
            {
                case "objective": Require(_objectives, condition, "id", path); break;
                case "flag": RequireFlag(condition, path); break;
                case "entities_dead": if (!_tags.Contains(JsonData.Text(condition["tag"]))) { Error(DiagnosticCode.LVL1020, "Unknown condition tag.", path + "/tag"); } break;
                case "not": if (condition["condition"] is JsonObject nested) { Condition(nested, path + "/condition"); } else { Error(DiagnosticCode.LVL0003, "not condition requires a nested condition.", path); } break;
            }
        }
        private bool ReferenceExists(string kind, string id) => kind switch
        {
            "entity" => content.Entities.ContainsKey(id), "loot" => content.Loot.ContainsKey(id), "sound" => content.Sounds.ContainsKey(id),
            "tag" => _tags.Contains(id), "objective" => _objectives.ContainsKey(id), "flag" => root["flags"] is JsonObject flags && flags.ContainsKey(id),
            "marker" => _entities.TryGetValue(id, out JsonObject? entity) && content.Entities.TryGetValue(JsonData.Text(entity["type"]), out EntityDef? def) && def.Kind is EntityKind.Marker or EntityKind.SpawnMarker,
            _ => false,
        };
        private void CheckSeconds(JsonObject source, string path)
        {
            if (source["seconds"] is not JsonNode seconds) { return; }
            try { _ = MathUtil.SecondsToTicks((float)JsonData.Number(seconds)); }
            catch (Exception error) when (error is ArgumentOutOfRangeException or OverflowException)
            { Error(DiagnosticCode.LVL0003, "Seconds must be finite, nonnegative and representable as a 32-bit tick duration.", path + "/seconds"); }
        }
        private void Require(Dictionary<string, JsonObject> ids, JsonObject source, string key, string path)
        { string id = JsonData.Text(source[key]); if (!ids.ContainsKey(id) && !(ReferenceEquals(ids, _triggers) && _implicitTriggers.Contains(id))) { Error(DiagnosticCode.LVL1020, "Unknown " + key + " reference.", path + "/" + key); } }
        private void RequireFlag(JsonObject source, string path)
        { if (root["flags"] is not JsonObject flags || !flags.ContainsKey(JsonData.Text(source["flag"]))) { Error(DiagnosticCode.LVL1020, "Unknown level flag.", path + "/flag"); } }
        private void AddTags(JsonArray? tags) { if (tags is not null) { foreach (JsonNode? tag in tags) { _tags.Add(JsonData.Text(tag)); } } }
        private void Index(JsonArray? array, Dictionary<string, JsonObject> destination, string path)
        {
            if (array is null) { return; }
            for (int i = 0; i < array.Count; i++)
            {
                JsonObject obj = array[i]!.AsObject();
                if (!destination.TryAdd(JsonData.Text(obj["id"]), obj)) { Error(DiagnosticCode.LVL1020, "Duplicate logic id.", path + "/" + JsonData.Index(i) + "/id"); }
            }
        }
        private static string Pointer(JsonNode node)
        {
            string path = "";
            while (node.Parent is JsonNode parent)
            {
                string segment = parent is JsonArray array ? JsonData.Index(array.IndexOf(node)) : JsonData.Pointer(parent.AsObject().First(pair => ReferenceEquals(pair.Value, node)).Key);
                path = "/" + segment + path; node = parent;
            }
            return path;
        }
        private void Error(DiagnosticCode code, string message, string path, IntVec2? cell = null) => diagnostics.Add(new(DiagnosticSeverity.Error, code, message, path, cell));
        private void Warn(DiagnosticCode code, string message, string path) => diagnostics.Add(new(DiagnosticSeverity.Warning, code, message, path));
    }
}
