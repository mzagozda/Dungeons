using System.Text.Json;
using System.Text.Json.Nodes;
using Dungeons.Core.Content;
using Dungeons.Core.Util;
using Dungeons.Core.Sim;
using System.Security.Cryptography;
using System.Text;

namespace Dungeons.Core.Level;

public sealed partial class LevelDocument
{
    private JsonObject? _source;
    internal JsonObject? Source { get => _source; set { _source = value; _missionSource = null; _sourceHash = ""; OverrideIndex = null; } }
    internal ContentRegistry? Content { get; set; }
    private string _sourceHash = "";
    /// <summary>Immutable environment with all standard defaults populated.</summary>
    public LevelEnvironment Environment { get; internal set; } = LevelEnvironment.Default;
    /// <summary>SHA256 of the exact loaded level.json bytes, for replay compatibility.</summary>
    public string SourceHash
    {
        get
        {
            if (_sourceHash.Length == 0 && Source is not null)
            { using SHA256 sha = SHA256.Create(); _sourceHash = ContentRegistry.Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(ToJson()))); }
            return _sourceHash;
        }
        internal set => _sourceHash = value;
    }
    /// <summary>Identity of the registry used to bind this document.</summary>
    public string ContentHash { get; internal set; } = "";
    /// <summary>English display name (or authored id for code fixtures).</summary>
    public string Name => Source is null ? Id : JsonData.Localized(Source["name"]);
    /// <summary>Author metadata.</summary>
    public string Author => Source is null ? "" : JsonData.Text(Source["author"]);
    /// <summary>Semantic package version.</summary>
    public string Version => Source is null ? "1.0.0" : JsonData.Text(Source["version"]);
    /// <summary>Snapshot of authored metadata, layers and logic. Edit a copy then re-load/validate.</summary>
    public JsonObject ToJsonObject() => Source?.DeepClone().AsObject() ?? throw new InvalidOperationException("Code-only scenarios have no authored package; construct a v1 document for serialization.");
    /// <summary>Human-readable package serialization preserving forward-compatible fields.</summary>
    public string ToJson() => JsonData.Stringify(ToJsonObject(), true);
    /// <summary>Paints an authored cell and its serialized palette/layers together. Validate after editing.</summary>
    public void PaintCell(int x, int z, string tileId, byte height, string? overlayId = null, JsonObject? properties = null)
    {
        if (EditingEnabled)
        {
            var editedReference = new JsonObject { ["tile"] = tileId };
            if (overlayId is not null) { editedReference["overlay"] = overlayId; }
            if (properties is not null) { editedReference["props"] = properties.DeepClone(); }
            new Commands.LevelCommandBatch([new Commands.SetTile(x, z, editedReference), new Commands.SetHeight(x, z, height)]).Apply(this); return;
        }
        _ = Index(x, z);
        if (Source is null || Content is null) { throw new InvalidOperationException("PaintCell needs a bound authored document; code fixtures use SetCell."); }
        if (height > 15) { throw new ArgumentOutOfRangeException(nameof(height)); }
        TileDef tile = Content.Tile(tileId); TileDef? overlay = overlayId is null ? null : Content.Tile(overlayId);
        if (tile.Overlay || overlay is not null && (!overlay.Overlay || !overlay.Cell.Walkable || overlay.Cell.Solid || overlay.Cell.BlocksProjectiles || overlay.Cell.BlocksVision)) { throw new ArgumentException("Invalid floor/overlay role."); }
        var diagnostics = new List<Diagnostic>(); JsonObject props = tile.Data.Properties.Normalize(properties, diagnostics, "/props");
        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)) { throw new ArgumentException("Cell properties fail the tile schema."); }
        JsonNode reference = props.Count == 0 && overlay is null ? JsonValue.Create(tileId)! : new JsonObject { ["tile"] = tileId, ["props"] = props };
        if (overlay is not null) { reference["overlay"] = overlayId; }
        JsonObject palette = Source["palette"]!.AsObject(); char symbol = '\0';
        foreach (KeyValuePair<string, JsonNode?> pair in palette) { if (JsonNode.DeepEquals(pair.Value, reference)) { symbol = pair.Key[0]; break; } }
        if (symbol == '\0')
        {
            for (char candidate = '!'; candidate <= '~'; candidate++) { if (!palette.ContainsKey(candidate.ToString())) { symbol = candidate; break; } }
            if (symbol == '\0') { throw new InvalidOperationException("The ASCII palette has no free symbols."); }
            palette[symbol.ToString()] = reference;
        }
        JsonObject layers = Source["layers"]!.AsObject(); Replace(layers["floor"]!.AsArray(), z, x, symbol);
        Replace(layers["heights"]!.AsArray(), z, x, "0123456789abcdef"[height]);
        if (layers["overlay"] is JsonArray overlays) { Replace(overlays, z, x, ' '); }
        if (Source["cells"] is JsonArray overrides)
        { for (int i = overrides.Count - 1; i >= 0; i--) { if (JsonData.Integer(overrides[i]!["x"]) == x && JsonData.Integer(overrides[i]!["z"]) == z) { overrides.RemoveAt(i); } } }
        StairsDirection direction = StairsDirection.None;
        if (tile.Data.Json.TryGetProperty("render", out JsonElement render) && render.TryGetProperty("kind", out JsonElement kind) && kind.GetString() == "stairs") { Enum.TryParse(JsonData.Text(props["dir"], "north"), true, out direction); }
        Cell flags = tile.Cell; SetBakedCell(x, z, new(height, flags.Walkable, flags.Solid, flags.BlocksProjectiles, flags.BlocksVision, direction, flags.MoveCost * (overlay?.Cell.MoveCost ?? 1)));
        _sourceHash = ""; OverrideIndex = null;
    }
    private static void Replace(JsonArray rows, int z, int x, char symbol)
    { char[] row = JsonData.Text(rows[z]).ToCharArray(); row[x] = symbol; rows[z] = new string(row); }
    private void AddAuthoredEntity(EntityPlacement placement)
    {
        if (Content is null || !Content.Entities.TryGetValue(placement.Definition.Id, out EntityDef? definition) || !ReferenceEquals(definition, placement.Definition)) { throw new ArgumentException("Placement must use this document's content registry."); }
        var diagnostics = new List<Diagnostic>();
        JsonObject props = definition.Data.Properties.Normalize(JsonData.Parse(placement.Properties.GetRawText()).AsObject(), diagnostics, "/props");
        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error) || !placement.Facing.IsFinite || placement.Facing == Dungeons.Core.Math.Vec2.Zero) { throw new ArgumentException("Invalid authored placement."); }
        var source = new JsonObject { ["type"] = definition.Id, ["x"] = placement.Position.X, ["z"] = placement.Position.Z,
            ["facing"] = new JsonArray(placement.Facing.X, placement.Facing.Z), ["props"] = props };
        if (placement.Id is not null) { source["id"] = placement.Id; }
        var tags = new JsonArray(); foreach (string tag in placement.Tags) { tags.Add((JsonNode?)JsonValue.Create(tag)); } source["tags"] = tags;
        if (EditingEnabled) { new Commands.AddEntity(source).Apply(this); return; }
        using JsonDocument properties = JsonDocument.Parse(JsonData.Stringify(props));
        AddBakedEntity(new(definition, placement.Position, placement.Id, placement.Facing.Normalized, Array.AsReadOnly(placement.Tags.ToArray()), properties.RootElement.Clone()));
        Source!["entities"]!.AsArray().Add((JsonNode)source); _sourceHash = ""; _missionSource = null;
    }
}
