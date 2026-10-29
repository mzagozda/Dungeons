using System.Text.Json.Nodes;
using Dungeons.Core.Math;

namespace Dungeons.Core.Level.Commands;

/// <summary>Replaces one floor reference, preserving height and clearing that cell's old overlay/overrides.</summary>
public sealed class SetTile : LevelCommand
{
    /// <summary>Paints a plain content tile reference.</summary>
    public SetTile(int x, int z, string tile) : this(x, z, JsonValue.Create(tile)!) { }
    /// <summary>Paints a v1 string/object tile reference, including composite overlay/properties.</summary>
    public SetTile(int x, int z, JsonNode reference) : base("set_tile", new() { ["x"] = x, ["z"] = z, ["reference"] = reference?.DeepClone() }) { }
}
/// <summary>Sets one terrain height without changing its tile, overlay or properties.</summary>
public sealed class SetHeight : LevelCommand
{
    /// <summary>Sets an integral height from zero through fifteen.</summary>
    public SetHeight(int x, int z, int height) : base("set_height", new() { ["x"] = x, ["z"] = z, ["height"] = height }) { }
}
/// <summary>Sets or clears an overlay, including overlays originally embedded in the floor palette reference.</summary>
public sealed class SetOverlay : LevelCommand
{
    /// <summary>Sets a decoration tile or clears it with null.</summary>
    public SetOverlay(int x, int z, string? overlay) : base("set_overlay", new() { ["x"] = x, ["z"] = z, ["overlay"] = overlay }) { }
}
/// <summary>Replaces a cell's property override, preserving shared palette definitions.</summary>
public sealed class SetCellProps : LevelCommand
{
    /// <summary>An empty property object removes the cell override.</summary>
    public SetCellProps(int x, int z, JsonObject properties) : base("set_cell_props", new() { ["x"] = x, ["z"] = z, ["props"] = properties?.DeepClone() }) { }
}
/// <summary>Point of the old/new grid to keep aligned while resizing.</summary>
public enum ResizeAnchor
{
    /// <summary>Keep the north-west corner.</summary>
    NorthWest,
    /// <summary>Keep the northern edge centred.</summary>
    North,
    /// <summary>Keep the north-east corner.</summary>
    NorthEast,
    /// <summary>Keep the western edge centred.</summary>
    West,
    /// <summary>Keep the grid centre; odd offsets round toward the north-west.</summary>
    Center,
    /// <summary>Keep the eastern edge centred.</summary>
    East,
    /// <summary>Keep the south-west corner.</summary>
    SouthWest,
    /// <summary>Keep the southern edge centred.</summary>
    South,
    /// <summary>Keep the south-east corner.</summary>
    SouthEast
}
/// <summary>Resizes layers and shifts spatial authoring data. Out-of-bounds entities/triggers remain for repair.</summary>
public sealed class Resize : LevelCommand
{
    /// <summary>Resizes to 8–256 cells per axis. New cells use height zero and an optional floor reference;
    /// the default is a stable existing walkable palette entry. Cropped cell overrides are removed.</summary>
    public Resize(int width, int height, ResizeAnchor anchor = ResizeAnchor.NorthWest, JsonNode? fillReference = null)
        : base("resize", new() { ["width"] = width, ["height"] = height, ["anchor"] = anchor.ToString(), ["fill"] = fillReference?.DeepClone() }) { }
}
/// <summary>Inserts an authored entity, preserving optional ids, facing, tags and forward-compatible properties.</summary>
public sealed class AddEntity : LevelCommand
{
    /// <summary>Inserts at an array index; minus one appends.</summary>
    public AddEntity(JsonObject entity, int index = -1) : base("add_entity", new() { ["entity"] = entity?.DeepClone(), ["index"] = index }) { }
}
/// <summary>Removes an entity by unique authored id or stable authoring-array index.</summary>
public sealed class RemoveEntity : LevelCommand
{
    /// <summary>Removes the entity with a unique id.</summary>
    public RemoveEntity(string id) : base("remove_entity", new() { ["entity"] = id }) { }
    /// <summary>Removes an anonymous or duplicate-id entity by array index.</summary>
    public RemoveEntity(int index) : base("remove_entity", new() { ["entity"] = index }) { }
}
/// <summary>Moves an entity and optionally changes facing, without rewriting its other metadata.</summary>
public sealed class MoveEntity : LevelCommand
{
    /// <summary>Moves the uniquely identified entity. Out-of-bounds positions remain editable with diagnostics.</summary>
    public MoveEntity(string id, Vec2 position, Vec2? facing = null) : this(JsonValue.Create(id)!, position, facing) { }
    /// <summary>Moves an entity selected by authoring-array index.</summary>
    public MoveEntity(int index, Vec2 position, Vec2? facing = null) : this(JsonValue.Create(index)!, position, facing) { }
    private MoveEntity(JsonNode selector, Vec2 position, Vec2? facing) : base("move_entity", new() { ["entity"] = selector, ["x"] = position.X, ["z"] = position.Z, ["facing"] = facing is { } value ? new JsonArray(value.X, value.Z) : null }) { }
}
/// <summary>Replaces an entity's property object; defaults are resolved during binding.</summary>
public sealed class SetEntityProps : LevelCommand
{
    /// <summary>Sets properties by unique authored id.</summary>
    public SetEntityProps(string id, JsonObject properties) : base("set_entity_props", new() { ["entity"] = id, ["props"] = properties?.DeepClone() }) { }
    /// <summary>Sets properties by authoring-array index.</summary>
    public SetEntityProps(int index, JsonObject properties) : base("set_entity_props", new() { ["entity"] = index, ["props"] = properties?.DeepClone() }) { }
}
/// <summary>Inserts a trigger into its authored evaluation order.</summary>
public sealed class AddTrigger : LevelCommand
{
    /// <summary>Inserts at an index; minus one appends.</summary>
    public AddTrigger(JsonObject trigger, int index = -1) : base("add_trigger", new() { ["trigger"] = trigger?.DeepClone(), ["index"] = index }) { }
}
/// <summary>Replaces or removes a trigger. Referenced-id changes can create repairable diagnostics.</summary>
public sealed class EditTrigger : LevelCommand
{
    /// <summary>Replaces a trigger selected by unique id; null removes it.</summary>
    public EditTrigger(string id, JsonObject? trigger) : base("edit_trigger", new() { ["trigger"] = id, ["value"] = trigger?.DeepClone() }) { }
    /// <summary>Replaces a trigger selected by array index; null removes it.</summary>
    public EditTrigger(int index, JsonObject? trigger) : base("edit_trigger", new() { ["trigger"] = index, ["value"] = trigger?.DeepClone() }) { }
}
/// <summary>Upserts or removes one objective, preserving the order of other objectives.</summary>
public sealed class SetObjective : LevelCommand
{
    /// <summary>Updates a matching id or appends a new objective.</summary>
    public SetObjective(JsonObject objective) : this(Util.JsonData.Text(objective?["id"]), objective) { }
    /// <summary>Replaces/upserts by unique id; null removes the selected objective.</summary>
    public SetObjective(string id, JsonObject? objective) : base("set_objective", new() { ["objective"] = id, ["value"] = objective?.DeepClone() }) { }
    /// <summary>Replaces/removes by authoring-array index.</summary>
    public SetObjective(int index, JsonObject? objective) : base("set_objective", new() { ["objective"] = index, ["value"] = objective?.DeepClone() }) { }
}
/// <summary>Replaces authored environment overrides without losing the document's default environment.</summary>
public sealed class SetEnvironment : LevelCommand
{
    /// <summary>Null removes overrides and restores the shared defaults.</summary>
    public SetEnvironment(JsonObject? environment) : base("set_environment", new() { ["value"] = environment?.DeepClone() }) { }
}
/// <summary>Sets root metadata, flags or completion, preserving all unrelated fields.</summary>
public sealed class SetMetadata : LevelCommand
{
    /// <summary>Sets one field, including a JSON null value.</summary>
    public SetMetadata(string key, JsonNode? value) : this(new JsonObject { [key] = value?.DeepClone() }) { }
    /// <summary>Sets fields and optionally removes optional fields. Structural collections use their dedicated commands.</summary>
    public SetMetadata(JsonObject values, IEnumerable<string>? remove = null) : base("set_metadata", new() { ["values"] = values?.DeepClone(), ["remove"] = new JsonArray((remove ?? []).Select(key => (JsonNode?)JsonValue.Create(key)).ToArray()) }) { }
}
/// <summary>One atomic macro/brush stroke, bound and validated once, with one undo record.</summary>
public sealed class LevelCommandBatch : LevelCommand
{
    /// <summary>Copies new built-in operations. Children retain no shared mutable history.</summary>
    public LevelCommandBatch(IEnumerable<ILevelCommand> commands) : base("batch", Arguments(commands)) { }
    private static JsonObject Arguments(IEnumerable<ILevelCommand> commands)
    {
        if (commands is null) { throw new ArgumentNullException(nameof(commands)); } var operations = new JsonArray();
        foreach (ILevelCommand command in commands)
        { if (command is not LevelCommand known || known.HasHistory) { throw new ArgumentException("A batch needs new built-in commands.", nameof(commands)); } operations.Add(known.Operation.DeepClone()); }
        return new() { ["commands"] = operations };
    }
}
