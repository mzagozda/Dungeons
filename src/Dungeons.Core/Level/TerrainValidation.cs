using System.Text.Json;
using System.Text.Json.Nodes;
using Dungeons.Core.Content;
using Dungeons.Core.Math;
using Dungeons.Core.Sim;
using Dungeons.Core.Util;

namespace Dungeons.Core.Level;

/// <summary>One terrain/property/stairs implementation for loading, full and regional validation.</summary>
internal sealed class TerrainValidation(JsonObject root, ContentRegistry content, List<Diagnostic> diagnostics, int width, int height, CellOverrideIndex overrides)
{
    private sealed class Reference(TileDef tile, TileDef? overlay, JsonObject props)
    {
        internal readonly TileDef Tile = tile;
        internal readonly TileDef? Overlay = overlay;
        internal readonly JsonObject Props = props;
        internal readonly bool Stairs = tile.Data.Json.TryGetProperty("render", out JsonElement render) && render.TryGetProperty("kind", out JsonElement kind) && kind.GetString() == "stairs";
        internal StairsDirection Direction;
    }
    private readonly Dictionary<char, Reference> _palette = [];
    private readonly List<IntVec2> _stairs = [];
    private readonly Dictionary<int, Cell?> _local = [];
    private readonly List<Diagnostic> _ignored = [];
    private string[] _floor = [], _heights = [], _floorPaths = [];
    private string[]? _overlay;
    internal void Bind(LevelDocument document, bool editing)
    {
        if (!Prepare() || !editing && diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)) { return; }
        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                Cell? cell = ReadCell(x, z, true); if (cell is not { } value) { continue; }
                document.SetBakedCell(x, z, value); if (value.StairsDir != StairsDirection.None) { _stairs.Add(new(x, z)); }
            }
        }
        foreach (IntVec2 point in _stairs) { CheckStairs(point, document.GetCell(point.X, point.Z), (x, z) => (uint)x < (uint)width && (uint)z < (uint)height ? document.GetCell(x, z) : default); }
    }
    internal void Validate(CellRect rectangle)
    {
        if (!Prepare()) { return; }
        CellRect area = rectangle.Clip(width, height, 1);
        for (int z = area.Z; z < area.Z + area.Height; z++)
        {
            for (int x = area.X; x < area.X + area.Width; x++)
            { Cell? cell = LocalCell(x, z, true); if (cell is { StairsDir: not StairsDirection.None }) { _stairs.Add(new(x, z)); } }
        }
        foreach (IntVec2 point in _stairs) { CheckStairs(point, _local[point.Z * width + point.X]!.Value, (x, z) => LocalCell(x, z, false).GetValueOrDefault()); }
    }
    private Cell? LocalCell(int x, int z, bool report)
    {
        if ((uint)x >= (uint)width || (uint)z >= (uint)height) { return null; }
        int key = z * width + x; if (_local.TryGetValue(key, out Cell? cached)) { return cached; }
        Cell? cell = ReadCell(x, z, report); _local.Add(key, cell); return cell;
    }
    private bool Prepare()
    {
        if (root["layers"] is not JsonObject layers || root["palette"] is not JsonObject palette)
        { diagnostics.Add(new(DiagnosticSeverity.Error, DiagnosticCode.LVL0003, "Terrain requires layers and palette.", "/layers")); return false; }
        bool dimensions = true;
        foreach (string name in new[] { "floor", "heights", "overlay" })
        {
            if (name == "overlay" && layers[name] is null) { continue; }
            if (layers[name] is not JsonArray rows || rows.Count != height || rows.Any(row => row is not JsonValue value || !value.TryGetValue<string>(out string? text) || text.Length != width))
            { dimensions = false; diagnostics.Add(new(DiagnosticSeverity.Error, DiagnosticCode.LVL1001, "Layer dimensions must match size.", "/layers/" + name)); }
        }
        foreach (var pair in palette)
        {
            string path = "/palette/" + JsonData.Pointer(pair.Key), id = pair.Value is JsonObject reference ? JsonData.Text(reference["tile"]) : JsonData.Text(pair.Value);
            if (pair.Key.Length != 1 || !content.Tiles.TryGetValue(id, out TileDef? tile))
            { diagnostics.Add(new(DiagnosticSeverity.Error, DiagnosticCode.LVL1003, "Unknown tile: " + id, path)); continue; }
            TileDef? overlay = null;
            if (pair.Value is JsonObject mapping && mapping["overlay"] is JsonNode overlayId && !content.Tiles.TryGetValue(JsonData.Text(overlayId), out overlay))
            { diagnostics.Add(new(DiagnosticSeverity.Error, DiagnosticCode.LVL1003, "Unknown overlay tile.", path + "/overlay")); }
            JsonObject props = tile.Data.Properties.Normalize((pair.Value as JsonObject)?["props"] as JsonObject, diagnostics, path + "/props", DiagnosticCode.LVL1003);
            var entry = new Reference(tile, overlay, props);
            if (entry.Stairs) { Enum.TryParse(JsonData.Text(props["dir"], "north"), true, out entry.Direction); }
            _palette[pair.Key[0]] = entry;
        }
        diagnostics.AddRange(overrides.Diagnostics);
        if (!dimensions) { return false; }
        _floor = layers["floor"]!.AsArray().Select(row => JsonData.Text(row)).ToArray(); _heights = layers["heights"]!.AsArray().Select(row => JsonData.Text(row)).ToArray();
        _overlay = (layers["overlay"] as JsonArray)?.Select(row => JsonData.Text(row)).ToArray();
        _floorPaths = Enumerable.Range(0, height).Select(z => "/layers/floor/" + JsonData.Index(z)).ToArray(); return true;
    }
    private Cell? ReadCell(int x, int z, bool report)
    {
        char symbol = _floor[z][x]; var point = new IntVec2(x, z); string path = _floorPaths[z];
        if (symbol == ' ' || !_palette.TryGetValue(symbol, out Reference? entry))
        { Error(report, DiagnosticCode.LVL1002, "Floor character is not in the palette.", path, point); return null; }
        TileDef floor = entry.Tile; TileDef? overlay = entry.Overlay; StairsDirection direction = entry.Direction;
        if (_overlay is not null && _overlay[z][x] != ' ')
        {
            if (!_palette.TryGetValue(_overlay[z][x], out Reference? mapped)) { Error(report, DiagnosticCode.LVL1002, "Overlay character is not in the palette.", "/layers/overlay/" + JsonData.Index(z), point); }
            else { overlay = mapped.Tile; }
        }
        if (floor.Overlay || overlay is not null && (!overlay.Overlay || !overlay.Cell.Walkable || overlay.Cell.Solid || overlay.Cell.BlocksProjectiles || overlay.Cell.BlocksVision))
        { Error(report, DiagnosticCode.LVL1004, "Floor/overlay roles or overlay blocking flags are invalid.", path, point); }
        if (overrides.Entries.TryGetValue(z * width + x, out var custom))
        {
            JsonObject merged = entry.Props.DeepClone().AsObject(); foreach (var pair in custom.Props) { merged[pair.Key] = pair.Value?.DeepClone(); }
            _ignored.Clear(); JsonObject props = floor.Data.Properties.Normalize(merged, _ignored, "/cells/" + JsonData.Index(custom.Index) + "/props", DiagnosticCode.LVL1003);
            if (report) { foreach (Diagnostic diagnostic in _ignored) { diagnostics.Add(new(diagnostic.Severity, diagnostic.Code, diagnostic.Message, diagnostic.JsonPath, point)); } }
            if (entry.Stairs) { Enum.TryParse(JsonData.Text(props["dir"], "north"), true, out direction); }
        }
        char h = _heights[z][x]; int value = h is >= '0' and <= '9' ? h - '0' : h is >= 'a' and <= 'f' ? h - 'a' + 10 : -1;
        if (value < 0) { Error(report, DiagnosticCode.LVL1005, "Height must be a lowercase hex digit.", "/layers/heights/" + JsonData.Index(z), point); return null; }
        Cell flags = floor.Cell; return new Cell((byte)value, flags.Walkable, flags.Solid, flags.BlocksProjectiles, flags.BlocksVision, direction, flags.MoveCost * (overlay?.Cell.MoveCost ?? 1));
    }
    private void CheckStairs(IntVec2 point, Cell cell, Func<int, int, Cell> read)
    {
        IntVec2 up = cell.StairsDir switch { StairsDirection.North => new(point.X, point.Z - 1), StairsDirection.East => new(point.X + 1, point.Z), StairsDirection.South => new(point.X, point.Z + 1), _ => new(point.X - 1, point.Z) };
        Cell upper = read(up.X, up.Z);
        if (!upper.Walkable || upper.Solid || upper.Height != cell.Height + 1) { Error(true, DiagnosticCode.LVL1006, "Stairs require walkable h+1 terrain uphill.", _floorPaths[point.Z], point); }
    }
    private void Error(bool report, DiagnosticCode code, string message, string path, IntVec2 cell)
    { if (report) { diagnostics.Add(new(DiagnosticSeverity.Error, code, message, path, cell)); } }
}
