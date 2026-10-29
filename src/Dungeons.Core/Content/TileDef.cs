using Dungeons.Core.Math;
using Dungeons.Core.Sim;

namespace Dungeons.Core.Content;

/// <summary>Immutable terrain prototype.</summary>
public sealed class TileDef
{
    internal TileDef(string id, Cell cell, bool overlay, string? baseFloor, DefinitionData data)
    { Id = id; Cell = cell; Overlay = overlay; BaseFloor = baseFloor; Data = data; }
    /// <summary>Namespaced content identity.</summary>
    public string Id { get; }
    /// <summary>Base gameplay flags; authored height/direction are applied by baking.</summary>
    public Cell Cell { get; }
    /// <summary>Whether this tile belongs in the decoration layer.</summary>
    public bool Overlay { get; }
    /// <summary>Optional visible ground under a composite model.</summary>
    public string? BaseFloor { get; }
    /// <summary>Rendering/audio/editor/property metadata.</summary>
    public DefinitionData Data { get; }
}

/// <summary>One independent loot probability row.</summary>
public readonly struct LootRow(string item, float chance, int min, int max)
{
    /// <summary>Pickup kind.</summary>
    public string Item { get; } = item;
    /// <summary>Probability in [0,1].</summary>
    public float Chance { get; } = chance;
    /// <summary>Inclusive minimum amount.</summary>
    public int Min { get; } = min;
    /// <summary>Inclusive maximum amount.</summary>
    public int Max { get; } = max;
}

/// <summary>Immutable independent loot rows.</summary>
public sealed class LootTable
{
    internal LootTable(string id, LootRow[] rows) { Id = id; Rows = Array.AsReadOnly(rows); }
    /// <summary>Namespaced identity.</summary>
    public string Id { get; }
    /// <summary>Ordered row probabilities.</summary>
    public IReadOnlyList<LootRow> Rows { get; }
}

/// <summary>Missing content indicates a programmer error; invalid levels return diagnostics.</summary>
public sealed class ContentMissingException(string id) : KeyNotFoundException("Unknown content id: " + id);
