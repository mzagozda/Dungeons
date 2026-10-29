using System.Text.Json;
using System.Text.Json.Nodes;
using Dungeons.Core.Sim;
using Dungeons.Core.Util;

namespace Dungeons.Core.Level;

public sealed partial class LevelDocument
{
    private JsonObject? _missionSource;
    // The loader prepares this before returning a document. Code scenarios and editor
    // additions use the same compiler lazily, always before a session starts ticking.
    internal JsonObject MissionSource => _missionSource ??= CompileMissionSource();
    private JsonObject CompileMissionSource()
    {
        var program = new JsonObject
        {
            ["flags"] = Source?["flags"]?.DeepClone() ?? new JsonObject(),
            ["objectives"] = Source?["objectives"]?.DeepClone() ?? new JsonArray(),
            ["triggers"] = Source?["triggers"]?.DeepClone() ?? new JsonArray(),
            ["completion"] = Source?["completion"]?.DeepClone() ?? new JsonObject(),
        };
        JsonArray triggers = program["triggers"]!.AsArray();
        foreach (EntityPlacement entity in Entities)
        {
            if (entity.Definition.Kind != EntityKind.Gate || entity.Id is null) { continue; }
            bool bound = entity.Properties.TryGetProperty("openWhen", out JsonElement value);
            string objective = bound ? value.GetString()! : "";
            if (!bound && Source?["entities"] is JsonArray authored)
            { objective = JsonData.Text(authored.FirstOrDefault(node => JsonData.Text(node?["id"]) == entity.Id)?["props"]?["openWhen"]); }
            if (objective.Length == 0) { continue; }
            triggers.Add((JsonNode)new JsonObject
            {
                ["id"] = "__gate_" + entity.Id,
                ["event"] = new JsonObject { ["type"] = "objective", ["id"] = objective, ["state"] = "complete" },
                ["actions"] = new JsonArray(new JsonObject { ["type"] = "set_state", ["entity"] = entity.Id, ["state"] = "open" }),
            });
        }
        return program;
    }
}
