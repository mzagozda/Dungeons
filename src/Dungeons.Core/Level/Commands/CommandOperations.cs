using System.Text.Json;
using System.Text.Json.Nodes;
using Dungeons.Core.Content;
using Dungeons.Core.Sim;
using Dungeons.Core.Util;

namespace Dungeons.Core.Level.Commands;

internal static class CommandOperations
{
    private static readonly string[] Structural = ["formatVersion", "size", "palette", "layers", "cells", "entities", "triggers", "objectives", "environment"];
    internal static void Validate(JsonObject operation) { int leaves = 0; Validate(operation, 0, ref leaves); }
    private static void Validate(JsonObject op, int depth, ref int leaves)
    {
        if (depth > 8) { throw new JsonException("Command batch depth limit exceeded."); }
        string kind = Text(op["type"]);
        if (kind != "batch" && ++leaves > GameConstants.MaxLevelSize * GameConstants.MaxLevelSize) { throw new JsonException("Command batch count limit exceeded."); }
        switch (kind)
        {
            case "set_tile": Keys(op, "x", "z", "reference"); Coordinates(op); Reference(op["reference"]); break;
            case "set_height": Keys(op, "x", "z", "height"); Coordinates(op); Integer(op["height"], 0, 15); break;
            case "set_overlay": Keys(op, "x", "z", "overlay"); Coordinates(op); if (!op.ContainsKey("overlay")) { throw new JsonException("Missing overlay."); } if (op["overlay"] is not null) { _ = Text(op["overlay"]); } break;
            case "set_cell_props": Keys(op, "x", "z", "props"); Coordinates(op); Object(op["props"]); break;
            case "resize":
                Keys(op, "width", "height", "anchor", "fill"); Integer(op["width"], 8, GameConstants.MaxLevelSize); Integer(op["height"], 8, GameConstants.MaxLevelSize);
                if (!Enum.TryParse(Text(op["anchor"]), false, out ResizeAnchor anchor) || !Enum.IsDefined(typeof(ResizeAnchor), anchor)) { throw new JsonException("Unknown resize anchor."); }
                if (op["fill"] is not null) { Reference(op["fill"]); } break;
            case "add_entity": Keys(op, "entity", "index"); Object(op["entity"]); Integer(op["index"], -1, 4096); break;
            case "remove_entity": Keys(op, "entity"); Selector(op["entity"]); break;
            case "move_entity":
                Keys(op, "entity", "x", "z", "facing"); Selector(op["entity"]); Number(op["x"]); Number(op["z"]);
                if (op["facing"] is not null) { if (op["facing"] is not JsonArray vector || vector.Count != 2) { throw new JsonException("Facing needs two components."); } Number(vector[0]); Number(vector[1]); } break;
            case "set_entity_props": Keys(op, "entity", "props"); Selector(op["entity"]); Object(op["props"]); break;
            case "add_trigger": Keys(op, "trigger", "index"); Object(op["trigger"]); Integer(op["index"], -1, 512); break;
            case "edit_trigger": Keys(op, "trigger", "value"); Selector(op["trigger"]); if (!op.ContainsKey("value")) { throw new JsonException("Missing trigger value."); } if (op["value"] is not null) { Object(op["value"]); } break;
            case "set_objective": Keys(op, "objective", "value"); Selector(op["objective"]); if (!op.ContainsKey("value")) { throw new JsonException("Missing objective value."); } if (op["value"] is not null) { Object(op["value"]); } break;
            case "set_environment": Keys(op, "value"); if (!op.ContainsKey("value")) { throw new JsonException("Missing environment value."); } if (op["value"] is not null) { Object(op["value"]); } break;
            case "set_metadata":
                Keys(op, "values", "remove"); JsonObject values = Object(op["values"]);
                if (op["remove"] is not JsonArray removed) { throw new JsonException("Metadata removals need an array."); }
                foreach (string key in values.Select(pair => pair.Key).Concat(removed.Select(Text)))
                { if (Structural.Contains(key, StringComparer.Ordinal)) { throw new JsonException("Use a dedicated command for structural level fields."); } }
                if (removed.Select(Text).Distinct(StringComparer.Ordinal).Count() != removed.Count || removed.Select(Text).Any(values.ContainsKey)) { throw new JsonException("Metadata set/remove fields overlap or repeat."); } break;
            case "batch":
                Keys(op, "commands"); if (op["commands"] is not JsonArray commands || commands.Count == 0) { throw new JsonException("A batch needs at least one operation."); }
                foreach (JsonNode? child in commands) { Validate(Object(child), depth + 1, ref leaves); } break;
            default: throw new JsonException("Unknown level command: " + kind);
        }
    }
    private static void Keys(JsonObject op, params string[] keys) => JsonDelta.Keys(op, ["type", .. keys]);
    private static void Coordinates(JsonObject op) { Integer(op["x"], 0, GameConstants.MaxLevelSize - 1); Integer(op["z"], 0, GameConstants.MaxLevelSize - 1); }
    private static void Selector(JsonNode? node) { if (node is JsonValue value && value.TryGetValue<string>(out _)) { _ = Text(node); } else { Integer(node, 0, 4095); } }
    private static string Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out string? text) && !string.IsNullOrWhiteSpace(text) ? text : throw new JsonException("Expected a nonempty string.");
    private static JsonObject Object(JsonNode? node) => node as JsonObject ?? throw new JsonException("Expected an object.");
    private static int Integer(JsonNode? node, int min, int max)
    { double value = Number(node); if (value < min || value > max || value != System.Math.Truncate(value)) { throw new JsonException("Command integer is outside its range."); } return (int)value; }
    private static double Number(JsonNode? node)
    { double value = JsonData.Number(node, double.NaN); if (double.IsNaN(value) || double.IsInfinity(value)) { throw new JsonException("Expected a finite number."); } return value; }
    private static void Reference(JsonNode? node)
    {
        if (node is JsonObject reference) { _ = Text(reference["tile"]); if (reference["overlay"] is not null) { _ = Text(reference["overlay"]); } if (reference["props"] is not null) { Object(reference["props"]); } }
        else { _ = Text(node); }
    }
    internal static void Execute(JsonObject op, JsonObject root, ContentRegistry content)
    {
        string kind = Text(op["type"]);
        if (kind == "batch") { foreach (JsonNode? child in op["commands"]!.AsArray()) { Execute(child!.AsObject(), root, content); } return; }
        if (kind == "resize") { ResizeRoot(op, root, content); return; }
        if (kind.StartsWith("set_", StringComparison.Ordinal) && op.ContainsKey("x"))
        {
            int x = (int)Number(op["x"]), z = (int)Number(op["z"]); CheckCell(root, x, z);
            if (kind == "set_height") { SetChar(root, "heights", x, z, "0123456789abcdef"[(int)Number(op["height"])]); return; }
            if (kind == "set_cell_props") { CellProps(root, x, z, op["props"]!.AsObject()); return; }
            if (kind == "set_tile")
            {
                SetChar(root, "floor", x, z, Palette(root, op["reference"]!)); ClearOverlay(root, x, z); CellProps(root, x, z, new()); return;
            }
            JsonObject palette = root["palette"]!.AsObject(); JsonNode floor = palette[ReadRow(root, "floor", z)[x].ToString()] ?? throw new LevelEditException("The cell has no floor reference.");
            if (floor is JsonObject composite && composite.ContainsKey("overlay")) { var clean = composite.DeepClone().AsObject(); clean.Remove("overlay"); SetChar(root, "floor", x, z, Palette(root, clean)); }
            ClearOverlay(root, x, z);
            if (op["overlay"] is not null)
            { if (root["layers"]!["overlay"] is null) { root["layers"]!["overlay"] = new JsonArray(Enumerable.Range(0, Height(root)).Select(_ => (JsonNode?)JsonValue.Create(new string(' ', Width(root)))).ToArray()); } SetChar(root, "overlay", x, z, Palette(root, op["overlay"]!)); }
            return;
        }
        switch (kind)
        {
            case "add_entity": Insert(root["entities"]!.AsArray(), op["entity"]!, (int)Number(op["index"])); break;
            case "remove_entity": { JsonArray entities = root["entities"]!.AsArray(); entities.RemoveAt(Find(entities, op["entity"]!)); break; }
            case "move_entity": { JsonObject entity = root["entities"]![Find(root["entities"]!.AsArray(), op["entity"]!)]!.AsObject(); entity["x"] = op["x"]!.DeepClone(); entity["z"] = op["z"]!.DeepClone(); if (op["facing"] is not null) { entity["facing"] = op["facing"]!.DeepClone(); } break; }
            case "set_entity_props": root["entities"]![Find(root["entities"]!.AsArray(), op["entity"]!)]!["props"] = op["props"]!.DeepClone(); break;
            case "add_trigger": { if (root["triggers"] is null) { root["triggers"] = new JsonArray(); } Insert(root["triggers"]!.AsArray(), op["trigger"]!, (int)Number(op["index"])); break; }
            case "edit_trigger": { JsonArray triggers = root["triggers"] as JsonArray ?? throw new LevelEditException("No triggers exist."); int selected = Find(triggers, op["trigger"]!); if (op["value"] is null) { triggers.RemoveAt(selected); } else { triggers[selected] = op["value"]!.DeepClone(); } break; }
            case "set_objective":
                if (root["objectives"] is null) { root["objectives"] = new JsonArray(); }
                JsonArray objectives = root["objectives"]!.AsArray(); int index = Find(objectives, op["objective"]!, op["value"] is not null);
                if (index < 0) { objectives.Add(op["value"]!.DeepClone()); } else if (op["value"] is null) { objectives.RemoveAt(index); } else { objectives[index] = op["value"]!.DeepClone(); } break;
            case "set_environment": if (op["value"] is null) { root.Remove("environment"); } else { root["environment"] = op["value"]!.DeepClone(); } break;
            case "set_metadata":
                foreach (var pair in op["values"]!.AsObject()) { root[pair.Key] = pair.Value?.DeepClone(); }
                foreach (JsonNode? key in op["remove"]!.AsArray()) { root.Remove(Text(key)); } break;
            default: throw new LevelEditException("Unknown command operation.");
        }
    }
    private static void Insert(JsonArray array, JsonNode value, int index)
    { if (index == -1) { index = array.Count; } if (index > array.Count) { throw new LevelEditException("Insertion index is outside the collection."); } array.Insert(index, value.DeepClone()); }
    private static int Find(JsonArray array, JsonNode selector, bool missing = false)
    {
        if (selector is JsonValue number && number.GetValueKind() == JsonValueKind.Number)
        { int index = (int)Number(selector); if (index >= array.Count) { throw new LevelEditException("Selection index is outside the collection."); } return index; }
        string id = Text(selector); int found = -1;
        for (int i = 0; i < array.Count; i++) { if (JsonData.Text(array[i]?["id"]) != id) { continue; } if (found >= 0) { throw new LevelEditException("The id is ambiguous; select by index to repair duplicates."); } found = i; }
        return found >= 0 || missing ? found : throw new LevelEditException("No authored item has that id.");
    }
    private static int Width(JsonObject root) => JsonData.Integer(root["size"]!["width"]);
    private static int Height(JsonObject root) => JsonData.Integer(root["size"]!["height"]);
    private static void CheckCell(JsonObject root, int x, int z)
    { if (x >= Width(root) || z >= Height(root)) { throw new LevelEditException("The cell is outside the level."); } }
    private static string ReadRow(JsonObject root, string layer, int z)
    { if (root["layers"]?[layer] is not JsonArray rows || z >= rows.Count || rows[z] is not JsonValue value || !value.TryGetValue<string>(out string? text) || text.Length != Width(root)) { throw new LevelEditException("Resize malformed layers before painting them."); } return text; }
    private static void SetChar(JsonObject root, string layer, int x, int z, char value)
    { char[] row = ReadRow(root, layer, z).ToCharArray(); row[x] = value; root["layers"]![layer]![z] = new string(row); }
    private static void ClearOverlay(JsonObject root, int x, int z) { if (root["layers"]?["overlay"] is not null) { SetChar(root, "overlay", x, z, ' '); } }
    private static char Palette(JsonObject root, JsonNode reference)
    {
        reference = CompactReference(reference);
        JsonObject palette = root["palette"]!.AsObject();
        foreach (var pair in palette.OrderBy(pair => pair.Key, StringComparer.Ordinal)) { if (JsonDelta.Equivalent(CompactReference(pair.Value!), reference)) { return pair.Key[0]; } }
        for (char symbol = '!'; symbol <= '~'; symbol++) { if (!palette.ContainsKey(symbol.ToString())) { palette[symbol.ToString()] = reference.DeepClone(); return symbol; } }
        throw new LevelEditException("The printable ASCII palette has no free symbol.");
    }
    private static JsonNode CompactReference(JsonNode reference)
        => reference is JsonObject obj && obj.All(pair => pair.Key == "tile" || pair.Key == "props" && pair.Value is JsonObject props && props.Count == 0)
            ? JsonValue.Create(JsonData.Text(obj["tile"]))! : reference;
    private static void CellProps(JsonObject root, int x, int z, JsonObject properties)
    {
        if (root["cells"] is not JsonArray cells) { if (properties.Count == 0) { return; } cells = new(); root["cells"] = cells; }
        int first = -1;
        for (int i = 0; i < cells.Count; i++)
        { if (JsonData.Integer(cells[i]?["x"], -1) != x || JsonData.Integer(cells[i]?["z"], -1) != z) { continue; } if (first < 0 && properties.Count > 0) { first = i; } else { cells.RemoveAt(i--); } }
        if (properties.Count == 0) { return; }
        var value = new JsonObject { ["x"] = x, ["z"] = z, ["props"] = properties.DeepClone() };
        if (first < 0) { cells.Add((JsonNode)value); } else { cells[first] = value; }
    }
    private static void ResizeRoot(JsonObject op, JsonObject root, ContentRegistry content)
    {
        int oldWidth = Width(root), oldHeight = Height(root), width = (int)Number(op["width"]), height = (int)Number(op["height"]);
        ResizeAnchor anchor = Enum.Parse<ResizeAnchor>(Text(op["anchor"])); int horizontal = (int)anchor % 3, vertical = (int)anchor / 3;
        int dx = horizontal == 0 ? 0 : horizontal == 2 ? width - oldWidth : (int)System.Math.Floor((width - oldWidth) / 2.0);
        int dz = vertical == 0 ? 0 : vertical == 2 ? height - oldHeight : (int)System.Math.Floor((height - oldHeight) / 2.0);
        JsonObject layers = root["layers"]!.AsObject(); bool same = width == oldWidth && height == oldHeight && layers.All(pair => pair.Value is JsonArray rows && rows.Count == height && rows.All(row => JsonData.Text(row).Length == width));
        if (same) { return; }
        JsonNode fill = op["fill"] ?? DefaultFill(root, content); char floor = Palette(root, fill);
        foreach (string name in new[] { "floor", "heights", "overlay" })
        {
            if (layers[name] is not JsonArray old) { continue; } var rows = new JsonArray();
            for (int z = 0; z < height; z++)
            {
                char[] row = Enumerable.Repeat(name == "floor" ? floor : name == "heights" ? '0' : ' ', width).ToArray(); int sourceZ = z - dz;
                string source = sourceZ >= 0 && sourceZ < oldHeight && sourceZ < old.Count ? JsonData.Text(old[sourceZ]) : "";
                for (int x = 0; x < width; x++) { int sourceX = x - dx; if (sourceX >= 0 && sourceX < oldWidth && sourceX < source.Length) { row[x] = source[sourceX]; } }
                rows.Add((JsonNode)new string(row));
            }
            layers[name] = rows;
        }
        root["size"]!["width"] = width; root["size"]!["height"] = height;
        if (root["cells"] is JsonArray cells)
        {
            for (int i = cells.Count - 1; i >= 0; i--)
            { JsonObject cell = cells[i]!.AsObject(); int x = JsonData.Integer(cell["x"]) + dx, z = JsonData.Integer(cell["z"]) + dz; if (x < 0 || z < 0 || x >= width || z >= height) { cells.RemoveAt(i); } else { cell["x"] = x; cell["z"] = z; } }
        }
        foreach (JsonNode? entity in root["entities"]!.AsArray()) { Shift(entity!.AsObject(), dx, dz); }
        if (root["triggers"] is JsonArray triggers) { foreach (JsonNode? trigger in triggers) { if (trigger?["event"]?["area"] is JsonObject area) { Shift(area, dx, dz); } } }
        if (root["environment"]?["camera"]?["bounds"] is JsonObject bounds)
        {
            Shift(bounds, dx, dz); double x = System.Math.Max(0, Number(bounds["x"])), z = System.Math.Max(0, Number(bounds["z"]));
            double w = System.Math.Min(width, Number(bounds["x"]) + Number(bounds["w"])) - x, h = System.Math.Min(height, Number(bounds["z"]) + Number(bounds["h"])) - z;
            if (w <= 0 || h <= 0) { root["environment"]!["camera"]!.AsObject().Remove("bounds"); } else { bounds["x"] = x; bounds["z"] = z; bounds["w"] = w; bounds["h"] = h; }
        }
    }
    private static void Shift(JsonObject value, int dx, int dz) { value["x"] = Number(value["x"]) + dx; value["z"] = Number(value["z"]) + dz; }
    private static JsonNode DefaultFill(JsonObject root, ContentRegistry content)
    {
        foreach (var pair in root["palette"]!.AsObject().OrderBy(pair => pair.Value is JsonObject obj && obj.ContainsKey("overlay") ? 1 : 0).ThenBy(pair => pair.Key, StringComparer.Ordinal))
        {
            string id = pair.Value is JsonObject reference ? JsonData.Text(reference["tile"]) : JsonData.Text(pair.Value);
            if (content.Tiles.TryGetValue(id, out TileDef? tile) && !tile.Overlay && tile.Cell.Walkable && !tile.Cell.Solid && !(tile.Data.Json.TryGetProperty("render", out JsonElement render) && render.TryGetProperty("kind", out JsonElement kind) && kind.GetString() == "stairs")) { return pair.Value!.DeepClone(); }
        }
        throw new LevelEditException("Resize needs an explicit walkable fill reference when the palette has none.");
    }
}
