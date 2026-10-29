using System.Text.Json;
using Dungeons.Core.Util;

namespace Dungeons.Core.Sim;

// Authoring shortcuts are compiled to mission actions and do not remain entity state.
internal static class RuntimeProperties
{
    internal static ulong Hash(EntityKind kind, JsonElement properties)
    {
        var node = JsonData.Parse(properties.GetRawText()).AsObject();
        if (kind == EntityKind.Gate) { node.Remove("openWhen"); }
        var hash = new Fnv1a(); hash.Add(JsonData.Stringify(node, canonical: true)); return hash.Value;
    }
}
