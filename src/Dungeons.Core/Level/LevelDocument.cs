using Dungeons.Core.Content;
using Dungeons.Core.Math;
using Dungeons.Core.Sim;
using System.Text.Json;

namespace Dungeons.Core.Level;

/// <summary>An authored entity placement, independent of any running session.</summary>
public readonly struct EntityPlacement(EntityDef definition, Vec2 position, string? id = null)
{
    internal EntityPlacement(EntityDef definition, Vec2 position, string? id, Vec2 facing, IReadOnlyList<string> tags, JsonElement properties) : this(definition, position, id)
    { Facing = facing; Tags = tags; Properties = properties; }
    /// <summary>Immutable content prototype.</summary>
    public EntityDef Definition { get; } = definition;
    /// <summary>World XZ position.</summary>
    public Vec2 Position { get; } = position;
    /// <summary>Optional authored id.</summary>
    public string? Id { get; } = id;
    /// <summary>Authored unit direction; code fixtures default north.</summary>
    public Vec2 Facing { get; } = Vec2.North;
    /// <summary>Immutable authored tags.</summary>
    public IReadOnlyList<string> Tags { get; } = Array.Empty<string>();
    /// <summary>Validated properties with defaults, independent of the editable source.</summary>
    public JsonElement Properties { get; } = DefinitionData.Empty.Json;
}

/// <summary>In-memory level source. M2 adds JSON metadata/palettes/triggers over this document.</summary>
public sealed partial class LevelDocument
{
    private Cell[] _cells;
    private readonly List<EntityPlacement> _entities = [];
    /// <summary>Creates a flat, editable document with no host or file assumptions.</summary>
    public LevelDocument(int width, int height, string id = "untitled")
    {
        if (width <= 0 || width > GameConstants.MaxLevelSize) { throw new ArgumentOutOfRangeException(nameof(width)); }
        if (height <= 0 || height > GameConstants.MaxLevelSize) { throw new ArgumentOutOfRangeException(nameof(height)); }
        if (string.IsNullOrWhiteSpace(id)) { throw new ArgumentException("A level id is required.", nameof(id)); }
        Width = width; Height = height; Id = id; _cells = new Cell[width * height];
        for (int i = 0; i < _cells.Length; i++) { _cells[i] = Cell.Floor; }
        Entities = _entities.AsReadOnly();
    }
    /// <summary>Stable authored level id.</summary>
    public string Id { get; private set; }
    /// <summary>Number of columns.</summary>
    public int Width { get; private set; }
    /// <summary>Number of rows.</summary>
    public int Height { get; private set; }
    /// <summary>Authored placements in stable authoring order.</summary>
    public IReadOnlyList<EntityPlacement> Entities { get; }
    /// <summary>Reserved package assets; v1 preserves bytes but does not apply rendering overrides.</summary>
    public IAssetSource Assets { get; internal set; } = MemoryAssetSource.Empty;
    /// <summary>Immutable registry bound by the loader; null for code-only scenarios.</summary>
    public ContentRegistry? Registry => Content;
    /// <summary>Reads the current authored floor/overlay ids without copying the entire source tree.
    /// Baked flags, height and stair direction remain available through GetCell.</summary>
    public (string Tile, string? Overlay) GetTileReference(int x, int z)
    {
        _ = Index(x, z);
        if (Source is null) { throw new InvalidOperationException("Tile references require an authored document."); }
        System.Text.Json.Nodes.JsonObject palette = Source["palette"]!.AsObject();
        string row = Util.JsonData.Text(Source["layers"]!["floor"]![z]);
        System.Text.Json.Nodes.JsonNode reference = palette[row[x].ToString()]!;
        string tile = reference is System.Text.Json.Nodes.JsonObject mapping ? Util.JsonData.Text(mapping["tile"]) : Util.JsonData.Text(reference);
        string? overlay = (reference as System.Text.Json.Nodes.JsonObject)?["overlay"] is { } mapped ? Util.JsonData.Text(mapped) : null;
        if (Source["layers"]!["overlay"] is System.Text.Json.Nodes.JsonArray rows)
        {
            char symbol = Util.JsonData.Text(rows[z])[x];
            if (symbol != ' ')
            {
                System.Text.Json.Nodes.JsonNode decoration = palette[symbol.ToString()]!;
                overlay = decoration is System.Text.Json.Nodes.JsonObject composite ? Util.JsonData.Text(composite["tile"]) : Util.JsonData.Text(decoration);
            }
        }
        return (tile, overlay);
    }
    /// <summary>Diagnostics from the last load or command commit; editing may retain semantic errors.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; internal set; } = Array.Empty<Diagnostic>();
    /// <summary>Whether the bound editing snapshot reports any error that prevents play.</summary>
    public bool HasErrors => Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    /// <summary>Changes after each effective command commit, undo or redo; no-op commands preserve it.</summary>
    public long Revision { get; private set; }
    internal bool EditingEnabled { get; set; }
    internal CellOverrideIndex? OverrideIndex { get; set; }
    internal void AdoptEditable(LevelDocument next)
    {
        _cells = next._cells; _entities.Clear(); _entities.AddRange(next._entities);
        Id = next.Id; Width = next.Width; Height = next.Height;
        Source = next.Source; Content = next.Content; ContentHash = next.ContentHash; Assets = next.Assets;
        Environment = next.Environment; SourceHash = next.SourceHash; _missionSource = next._missionSource;
        Diagnostics = next.Diagnostics; OverrideIndex = next.OverrideIndex; EditingEnabled = true; Revision = unchecked(Revision + 1);
    }
    /// <summary>Reads one authored cell.</summary>
    public Cell GetCell(int x, int z) => _cells[Index(x, z)];
    /// <summary>Edits code-fixture terrain. Authored packages use PaintCell to retain serialization.</summary>
    public void SetCell(int x, int z, Cell cell)
    {
        if (Source is not null) { throw new InvalidOperationException("Use PaintCell for authored documents so serialization retains the terrain edit."); }
        SetBakedCell(x, z, cell);
    }
    internal void SetBakedCell(int x, int z, Cell cell) => _cells[Index(x, z)] = cell;
    /// <summary>Adds an authored placement; semantic validation is extended at M2.</summary>
    public void AddEntity(EntityPlacement placement)
    {
        if (placement.Definition is null || !placement.Position.IsFinite) { throw new ArgumentException("Invalid placement.", nameof(placement)); }
        if (Source is not null) { AddAuthoredEntity(placement); return; }
        AddBakedEntity(placement);
    }
    internal void AddBakedEntity(EntityPlacement placement)
    {
        _entities.Add(placement);
    }
    /// <summary>Bakes a snapshot used identically by runtime/navigation/validation.</summary>
    public NavGrid BakeGrid() => new(Width, Height, _cells);
    private int Index(int x, int z)
    {
        if ((uint)x >= (uint)Width || (uint)z >= (uint)Height) { throw new ArgumentOutOfRangeException(nameof(x)); }
        return z * Width + x;
    }
}
