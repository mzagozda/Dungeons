using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Dungeons.Core.Level;
using Dungeons.Core.Sim;
using Dungeons.Core.Util;

namespace Dungeons.Core.Content;

/// <summary>Validated immutable content catalogue. All inputs are supplied by the host; no file IO.</summary>
public sealed class ContentRegistry
{
    private readonly Dictionary<string, TileDef> _tiles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EntityDef> _entities = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LootTable> _loot = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DefinitionData> _sounds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WeaponDef> _weapons = new(StringComparer.Ordinal);
    private readonly List<Diagnostic> _diagnostics = [];
    private ContentRegistry()
    {
        Tiles = new ReadOnlyDictionary<string, TileDef>(_tiles); Entities = new ReadOnlyDictionary<string, EntityDef>(_entities);
        Loot = new ReadOnlyDictionary<string, LootTable>(_loot); Sounds = new ReadOnlyDictionary<string, DefinitionData>(_sounds);
        Weapons = new ReadOnlyDictionary<string, WeaponDef>(_weapons);
        Diagnostics = _diagnostics.AsReadOnly();
    }
    /// <summary>Tiles indexed with ordinal case-sensitive ids.</summary>
    public IReadOnlyDictionary<string, TileDef> Tiles { get; }
    /// <summary>Entity prototypes.</summary>
    public IReadOnlyDictionary<string, EntityDef> Entities { get; }
    /// <summary>Loot tables.</summary>
    public IReadOnlyDictionary<string, LootTable> Loot { get; }
    /// <summary>Audio definitions (including music/ambience).</summary>
    public IReadOnlyDictionary<string, DefinitionData> Sounds { get; }
    /// <summary>Immutable gear definitions, resolved before entity loadouts.</summary>
    public IReadOnlyDictionary<string, WeaponDef> Weapons { get; }
    /// <summary>Stable source diagnostics.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; }
    /// <summary>Whether this catalogue may be used by a game session.</summary>
    public bool IsValid => !_diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
    /// <summary>SHA256 identity of path-sorted source bytes, with explicit length framing.</summary>
    public string Hash { get; private set; } = "";
    /// <summary>Returns a known tile. User mistakes must be checked before calling.</summary>
    public TileDef Tile(string id) => _tiles.TryGetValue(id, out TileDef? tile) ? tile : throw new ContentMissingException(id);
    /// <summary>Loads definitions, resolves extends, checks schemas and cross references.</summary>
    public static ContentRegistry Load(IEnumerable<(string path, string json)> files, ILogger? log = null)
    {
        var registry = new ContentRegistry();
        var sources = files.OrderBy(f => f.path.Replace('\\', '/'), StringComparer.Ordinal).ToArray();
        var raw = new Dictionary<string, (string path, JsonObject data)>(StringComparer.Ordinal);
        using var identity = new MemoryStream();
        using (var writer = new BinaryWriter(identity, Encoding.UTF8, true))
        {
            foreach ((string path, string json) in sources)
            {
                writer.Write(path.Replace('\\', '/')); writer.Write(json);
                try
                {
                    JsonObject root = JsonData.Parse(json).AsObject();
                    if (path.Replace('\\', '/').EndsWith("/audio.json", StringComparison.Ordinal) || path == "audio.json")
                    {
                        foreach (KeyValuePair<string, JsonNode?> sound in root)
                        {
                            if (!IsId(sound.Key) || sound.Value is not JsonObject data) { throw new ArgumentException("Invalid sound definition."); }
                            if (data["files"] is not JsonArray audioFiles || audioFiles.Count == 0 || audioFiles.Any(f => !LevelPackage.IsSafePath(JsonData.Text(f)))) { throw new ArgumentException("Sound files must be portable relative paths."); }
                            _ = Float(data, "volumeDb");
                            if (Int(data, "maxInstances", 4) < 1) { throw new ArgumentException("Sound maxInstances must be positive."); }
                            if (data["pitchRange"] is JsonNode pitch && (pitch is not JsonArray range || range.Count != 2 || JsonData.Number(range[0], double.NaN) <= 0 || JsonData.Number(range[1], double.NaN) < JsonData.Number(range[0], double.NaN) || range.Any(p => !double.IsFinite(JsonData.Number(p, double.NaN))))) { throw new ArgumentException("Invalid sound pitch range."); }
                            if (!registry._sounds.TryAdd(sound.Key, new DefinitionData(data))) { registry.Error(DiagnosticCode.CNT1001, "Duplicate audio id: " + sound.Key, path); }
                        }
                        continue;
                    }
                    string id = JsonData.Text(root["id"]);
                    if (!IsId(id)) { throw new ArgumentException("Namespaced content id is required."); }
                    if (!raw.TryAdd(id, (path.Replace('\\', '/'), root))) { registry.Error(DiagnosticCode.CNT1001, "Duplicate content id: " + id, path); }
                }
                catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
                { registry.Error(DiagnosticCode.CNT0001, ex.Message, path); }
            }
        }
        using (SHA256 sha = SHA256.Create()) { registry.Hash = Hex(sha.ComputeHash(identity.ToArray())); }
        var resolved = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (string id in raw.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            if (!raw[id].path.StartsWith("weapons/", StringComparison.Ordinal) && !raw[id].path.Contains("/weapons/")) { continue; }
            try { registry._weapons.Add(id, WeaponDef.Parse(Resolve(id, raw, resolved, new HashSet<string>(StringComparer.Ordinal)))); }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or JsonException)
            { registry.Error(DiagnosticCode.CNT1002, id + ": " + ex.Message, raw[id].path); }
        }
        foreach (string id in raw.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            try
            {
                JsonObject source = Resolve(id, raw, resolved, new HashSet<string>(StringComparer.Ordinal));
                string path = raw[id].path;
                if (path.StartsWith("weapons/", StringComparison.Ordinal) || path.Contains("/weapons/")) { continue; }
                ValidateShape(source);
                var metadata = new DefinitionData(source);
                if (path.StartsWith("tiles/", StringComparison.Ordinal) || path.Contains("/tiles/"))
                {
                    var flags = new Cell(walkable: Bool(source, "walkable", true), solid: Bool(source, "solid"), blocksProjectiles: Bool(source, "blocksProjectiles"), blocksVision: Bool(source, "blocksVision"), moveCost: Float(source, "moveCost", 1));
                    registry._tiles.Add(id, new(id, flags, Bool(source, "overlay"), source["baseFloor"] is null ? null : JsonData.Text(source["baseFloor"]), metadata));
                }
                else if (path.StartsWith("entities/", StringComparison.Ordinal) || path.Contains("/entities/"))
                {
                    if (!Enum.TryParse(JsonData.Text(source["kind"]), false, out EntityKind kind) || !Enum.IsDefined(typeof(EntityKind), kind)) { throw new ArgumentException("Unknown entity kind."); }
                    JsonObject stats = source["stats"] as JsonObject ?? new();
                    var block = new StatBlock(Int(stats, "hp", 1), Float(stats, "speed"), Int(stats, "armor"), Float(stats, "damageMultiplier", 1), Float(stats, "cooldownMultiplier", 1));
                    WeaponLoadout? loadout = null;
                    if (source["weapons"] is JsonObject slots)
                    {
                        WeaponDef? ResolveSlot(string slot)
                        {
                            string reference = JsonData.Text(slots[slot]); if (reference.Length == 0) { return null; }
                            if (registry._weapons.TryGetValue(reference, out WeaponDef? weapon)) { return weapon; }
                            registry.Error(DiagnosticCode.CNT1003, "Unknown weapon: " + reference, id + "/weapons/" + slot); return null;
                        }
                        loadout = new(ResolveSlot("melee"), ResolveSlot("ranged"));
                    }
                    registry._entities.Add(id, new(id, kind, block, Float(source, "radius", 0), Bool(source, "solid"), metadata, loadout));
                }
                else if (path.StartsWith("loot/", StringComparison.Ordinal) || path.Contains("/loot/"))
                {
                    if (source["rows"] is not JsonArray rows) { throw new ArgumentException("Loot rows are required."); }
                    var parsed = new List<LootRow>();
                    foreach (JsonNode? node in rows)
                    {
                        if (node is not JsonObject row) { throw new ArgumentException("Invalid loot row."); }
                        string item = JsonData.Text(row["item"]); float chance = Float(row, "chance", 1);
                        int min = Int(row, "min", 1), max = Int(row, "max", min);
                        if (!new[] { "emerald", "food", "arrows" }.Contains(item, StringComparer.Ordinal) || chance < 0 || chance > 1 || min < 1 || max < min || max > 9999) { throw new ArgumentException("Invalid loot probability/amount/item."); }
                        parsed.Add(new(item, chance, min, max));
                    }
                    registry._loot.Add(id, new(id, parsed.ToArray()));
                }
                else { throw new ArgumentException("Definition must be in tiles/, entities/, weapons/ or loot/."); }
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or JsonException)
            { registry.Error(DiagnosticCode.CNT1002, id + ": " + ex.Message, raw[id].path); }
        }
        try { registry.ValidateReferences(); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or JsonException)
        { registry.Error(DiagnosticCode.CNT1002, ex.Message, ""); }
        foreach (Diagnostic diagnostic in registry.Diagnostics) { (log ?? NullLogger.Instance).Log(diagnostic.Severity == DiagnosticSeverity.Error ? LogLevel.Error : LogLevel.Warning, "Level", diagnostic.ToString()); }
        return registry;
    }
    private static JsonObject Resolve(string id, Dictionary<string, (string path, JsonObject data)> raw, Dictionary<string, JsonObject> resolved, HashSet<string> visiting)
    {
        if (resolved.TryGetValue(id, out JsonObject? cached)) { return cached; }
        if (!raw.TryGetValue(id, out var source)) { throw new ArgumentException("Unknown extends target: " + id); }
        if (!visiting.Add(id)) { throw new ArgumentException("Cyclic extends definition: " + id); }
        JsonObject result = source.data.DeepClone().AsObject();
        foreach (string key in new[] { "extends", "baseDef" }) { if (result.ContainsKey(key) && !IsId(JsonData.Text(result[key]))) { throw new ArgumentException("Invalid inheritance reference."); } }
        if (result.ContainsKey("statMultipliers") && result["statMultipliers"] is not JsonObject) { throw new ArgumentException("Stat multipliers must be an object."); }
        string parent = JsonData.Text(result["extends"], JsonData.Text(result["baseDef"]));
        if (parent.Length > 0) { result = Merge(Resolve(parent, raw, resolved, visiting), result); }
        if (result["statMultipliers"] is JsonObject multipliers)
        {
            JsonObject stats = result["stats"]?.DeepClone().AsObject() ?? new JsonObject();
            foreach (KeyValuePair<string, JsonNode?> pair in multipliers)
            {
                if (!new[] { "hp", "armor", "speed", "damageMultiplier", "cooldownMultiplier" }.Contains(pair.Key, StringComparer.Ordinal)) { throw new ArgumentException("Unknown stat multiplier."); }
                double multiplier = JsonData.Number(pair.Value, double.NaN), value = JsonData.Number(stats[pair.Key], pair.Key is "hp" or "damageMultiplier" or "cooldownMultiplier" ? 1 : 0) * multiplier;
                if (double.IsNaN(value) || double.IsInfinity(value) || multiplier <= 0) { throw new ArgumentException("Stat multipliers must be positive and finite."); }
                stats[pair.Key] = pair.Key is "hp" or "armor" ? System.Math.Ceiling(value) : value;
            }
            result["stats"] = stats; result.Remove("statMultipliers");
        }
        visiting.Remove(id); result.Remove("extends"); result.Remove("baseDef"); resolved[id] = result; return result;
    }
    private static JsonObject Merge(JsonObject parent, JsonObject child)
    {
        JsonObject result = parent.DeepClone().AsObject();
        foreach (KeyValuePair<string, JsonNode?> pair in child)
        { result[pair.Key] = pair.Value is JsonObject nested && result[pair.Key] is JsonObject original ? Merge(original, nested) : pair.Value?.DeepClone(); }
        return result;
    }
    private void ValidateReferences()
    {
        foreach (WeaponDef weapon in _weapons.Values)
        { if (weapon.ProjectileId is string projectile && (!_entities.TryGetValue(projectile, out EntityDef? definition) || definition.Kind != EntityKind.Projectile || definition.Projectile is null)) { Error(DiagnosticCode.CNT1003, "Unknown/non-projectile weapon ammunition: " + projectile, weapon.Id + "/projectile"); } }
        foreach (TileDef tile in _tiles.Values)
        { if (tile.BaseFloor is string floor && (!_tiles.TryGetValue(floor, out TileDef? target) || target.Overlay)) { Error(DiagnosticCode.CNT1003, "Unknown/non-floor baseFloor: " + floor, tile.Id); } }
        foreach (EntityDef entity in _entities.Values)
        {
            JsonElement json = entity.Data.Json;
            if (json.TryGetProperty("loot", out JsonElement loot) && !_loot.ContainsKey(loot.GetString()!)) { Error(DiagnosticCode.CNT1003, "Unknown loot table.", entity.Id + "/loot"); }
            if (json.TryGetProperty("brain", out JsonElement brain) && !new[] { "core:brain_none", "core:brain_melee_chaser", "core:brain_archer" }.Contains(brain.GetString(), StringComparer.Ordinal)) { Error(DiagnosticCode.CNT1003, "Unsupported brain.", entity.Id + "/brain"); }
            if (json.TryGetProperty("audio", out JsonElement audio))
            { foreach (JsonProperty sound in audio.EnumerateObject()) { if (sound.Value.ValueKind == JsonValueKind.String && !_sounds.ContainsKey(sound.Value.GetString()!)) { Error(DiagnosticCode.CNT1003, "Unknown sound id.", entity.Id + "/audio/" + sound.Name); } } }
            if (json.TryGetProperty("attack", out JsonElement attack) && attack.TryGetProperty("projectile", out JsonElement projectile) && (!_entities.TryGetValue(projectile.GetString()!, out EntityDef? definition) || definition.Kind != EntityKind.Projectile)) { Error(DiagnosticCode.CNT1003, "Attack references an unknown/non-projectile definition.", entity.Id + "/attack/projectile"); }
        }
    }
    private static void ValidateShape(JsonObject source)
    {
        foreach (string key in new[] { "displayName", "category", "faction", "brain", "loot", "baseFloor" })
        { if (source.ContainsKey(key) && source[key]?.GetValueKind() != JsonValueKind.String) { throw new ArgumentException("Expected string: " + key); } }
        foreach (string key in new[] { "stats", "props", "render", "audio", "editor", "attack", "projectile", "pickup", "weapons", "control" })
        { if (source.ContainsKey(key) && source[key] is not JsonObject) { throw new ArgumentException("Expected object: " + key); } }
        foreach (string key in new[] { "requiresGround", "unique", "solid", "walkable", "overlay", "blocksProjectiles", "blocksVision" }) { _ = Bool(source, key); }
        _ = Bool(source, "breakable");
        if (source["stats"] is JsonObject stats) { _ = Int(stats, "xp"); _ = Float(stats, "knockbackResistance"); _ = Float(stats, "aggroRadius"); _ = Float(stats, "hearingRadius"); }
        if (source["attack"] is JsonObject attack)
        {
            _ = Int(attack, "damage");
            foreach (string key in new[] { "windup", "active", "recovery", "reach", "halfAngleDeg", "knockback", "triggerDistance", "aim", "minRange", "maxRange", "retreatBelow", "retreatMax" }) { _ = Float(attack, key); }
        }
        if (source["projectile"] is JsonObject projectile)
        {
            foreach (string key in new[] { "damage", "lifetimeTicks", "pierce" }) { _ = Int(projectile, key); }
            foreach (string key in new[] { "speed", "lifetime", "knockback", "height" }) { _ = Float(projectile, key); }
        }
        if (source["pickup"] is JsonObject pickup) { _ = Int(pickup, "amount"); _ = Float(pickup, "healFraction"); _ = Float(pickup, "healPercent"); }
        if (source["control"] is JsonObject control)
        {
            foreach (string key in new[] { "rollTicks", "rollInvulnerableFirst", "rollInvulnerableLast", "rollCooldownTicks", "potionCooldownTicks", "arrowsStart", "arrowsMax" }) { _ = Int(control, key); }
            foreach (string key in new[] { "rollDistance", "pickupRadius", "magnetRadius", "magnetSpeed", "recoveryWalk" }) { _ = Float(control, key); }
        }
        if (source["weapons"] is JsonObject weapons)
        { foreach (var slot in weapons) { if (slot.Key is not ("melee" or "ranged") || !IsId(JsonData.Text(slot.Value))) { throw new ArgumentException("Invalid weapon slot/reference."); } } }
        if (source["render"] is JsonObject render)
        {
            if (render["kind"] is JsonNode kind && !new[] { "block", "model", "water", "none", "stairs", "cross" }.Contains(JsonData.Text(kind), StringComparer.Ordinal)) { throw new ArgumentException("Unknown render kind."); }
            if (render["model"] is JsonNode model && !LevelPackage.IsSafePath(JsonData.Text(model))) { throw new ArgumentException("Model path must be portable and relative."); }
            foreach (string field in new[] { "variants", "topVariants" })
            { if (render[field] is JsonNode count && (!count.AsValue().TryGetValue<int>(out int value) || value < 1)) { throw new ArgumentException("Render variant count must be a positive integer."); } }
            foreach (string field in new[] { "randomRotation", "castsShadow", "waterBelow", "light" }) { _ = Bool(render, field); }
            foreach (string field in new[] { "scale", "thickness" })
            { if (render.ContainsKey(field) && Float(render, field) <= 0) { throw new ArgumentException("Render scale/thickness must be positive."); } }
            if (render.ContainsKey("thickness") && Float(render, "thickness") > 1) { throw new ArgumentException("A thin slab cannot exceed one block."); }
            if (render["animations"] is JsonNode animations && (animations is not JsonObject clips || clips.Any(p => p.Value?.GetValueKind() != JsonValueKind.String))) { throw new ArgumentException("Animations must map names to string clip ids."); }
        }
        if (source["audio"] is JsonObject audio && audio.Any(p => p.Value?.GetValueKind() != JsonValueKind.String)) { throw new ArgumentException("Audio event ids must be strings."); }
    }
    private void Error(DiagnosticCode code, string message, string path) => _diagnostics.Add(new(DiagnosticSeverity.Error, code, message, path));
    internal static bool IsId(string id) => Regex.IsMatch(id, "^[a-z0-9_]+:[a-z0-9_]+$", RegexOptions.CultureInvariant);
    internal static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
    private static bool Bool(JsonObject obj, string name, bool fallback = false)
    { if (obj.ContainsKey(name) && obj[name]?.GetValueKind() is not (JsonValueKind.True or JsonValueKind.False)) { throw new ArgumentException("Invalid boolean: " + name); } return JsonData.Boolean(obj[name], fallback); }
    private static float Float(JsonObject obj, string name, float fallback = 0)
    { double value = obj.ContainsKey(name) ? JsonData.Number(obj[name], double.NaN) : fallback; if (double.IsNaN(value) || double.IsInfinity(value) || value < -float.MaxValue || value > float.MaxValue) { throw new ArgumentException("Invalid number: " + name); } return (float)value; }
    private static int Int(JsonObject obj, string name, int fallback = 0)
    { double value = obj.ContainsKey(name) ? JsonData.Number(obj[name], double.NaN) : fallback; if (double.IsNaN(value) || value < int.MinValue || value > int.MaxValue || value != System.Math.Truncate(value)) { throw new ArgumentException("Invalid integer: " + name); } return (int)value; }
}
