using System.Text.Json;
using Dungeons.Core.Content;
using Dungeons.Core.Level;
using Dungeons.Core.Math;
using Dungeons.Core.Util;

namespace Dungeons.Core.Sim;

// Parsed once for authored placements and spawn actions; shared by runtime instances.
internal sealed class SpawnerTemplate
{
    internal SpawnerTemplate(JsonElement properties, ContentRegistry content, DifficultyProfile difficulty)
    {
        string id = properties.GetProperty("entity").GetString()!;
        if (!content.Entities.TryGetValue(id, out EntityDef? definition) || definition.Kind != EntityKind.Enemy) { throw new ContentMissingException(id); }
        int Integer(string name, int fallback) => properties.TryGetProperty(name, out JsonElement value) ? value.GetInt32() : fallback;
        float Number(string name, float fallback) => properties.TryGetProperty(name, out JsonElement value) ? value.GetSingle() : fallback;
        string Text(string name, string fallback) => properties.TryGetProperty(name, out JsonElement value) ? value.GetString()! : fallback;
        Definition = definition; Count = difficulty.ScaleSpawnCount(Integer("count", 1)); Tag = Text("tag", "");
        Delay = MathUtil.SecondsToTicks(Number("delay", 0)); Interval = MathUtil.SecondsToTicks(Number("interval", 1));
        MaxAlive = Integer("maxAlive", 0); if (MaxAlive == 0) { MaxAlive = System.Math.Min(GameConstants.MaxMonsters, System.Math.Max(1, Count)); }
        Radius = Number("radius", 0); ActivationRadius = Number("activationRadius", 8); Activation = Text("activation", "trigger");
        string[] tags = Tag.Length == 0 ? [] : [Tag];
        Settings = new(new EntityPlacement(definition, default, null, Vec2.North, Array.AsReadOnly(tags), definition.DefaultProperties));
        var hash = new Fnv1a(); hash.Add(properties.GetRawText()); hash.Add(definition.SimulationHash); Identity = hash.Value;
    }
    internal EntityDef Definition { get; }
    internal SpawnSettings Settings { get; }
    internal string Tag { get; }
    internal string Activation { get; }
    internal int Count { get; }
    internal int Delay { get; }
    internal int Interval { get; }
    internal int MaxAlive { get; }
    internal float Radius { get; }
    internal float ActivationRadius { get; }
    internal ulong Identity { get; }
}
