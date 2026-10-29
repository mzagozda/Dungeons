using System.Text.Json;
using Dungeons.Core.Content;
using Dungeons.Core.Level;
using Dungeons.Core.Util;

namespace Dungeons.Core.Sim;

// Immutable per-placement tuning is compiled before ticks and retained by queued spawns.
internal sealed class SpawnSettings
{
    internal SpawnSettings(in EntityPlacement placement)
    {
        LevelId = placement.Id; Properties = placement.Properties; Tags = placement.Tags;
        PropertiesHash = RuntimeProperties.Hash(placement.Definition.Kind, Properties);
        Rules = new(placement.Definition.Kind, Properties);
        PickupAmount = placement.Definition.DefaultPickupAmount;
        if (placement.Definition.Pickup?.Kind == PickupKind.Emerald && Properties.TryGetProperty("amount", out JsonElement amount)) { PickupAmount = amount.GetInt32(); }
        PatrolRadius = placement.Definition.Brain?.PatrolRadius ?? 0;
        if (placement.Definition.Brain is not null && Properties.TryGetProperty("patrolRadius", out JsonElement patrol)) { PatrolRadius = patrol.GetSingle(); }
    }
    internal string? LevelId { get; }
    internal JsonElement Properties { get; }
    internal ulong PropertiesHash { get; }
    internal IReadOnlyList<string> Tags { get; }
    internal ObjectRules Rules { get; }
    internal int PickupAmount { get; }
    internal float PatrolRadius { get; }
    // Bound by the mission compiler before this immutable setting enters a tick.
    internal SpawnerTemplate? Spawner { get; set; }
    internal int GateObjective { get; set; } = -1;
    internal void Hash(ref Fnv1a hash)
    {
        hash.Add(LevelId?.Length ?? -1); if (LevelId is not null) { hash.Add(LevelId); }
        hash.Add(PropertiesHash); hash.Add(PickupAmount); hash.Add(BitConverter.SingleToInt32Bits(PatrolRadius)); Rules.Hash(ref hash); hash.Add(GateObjective);
    }
}
