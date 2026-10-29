using System.Text.Json;
using Dungeons.Core.Util;

namespace Dungeons.Core.Sim;

/// <summary>Gate presentation phase; opening immediately permits traversal.</summary>
public enum GatePhase
{
    /// <summary>Finished closing; blocks movement and shots.</summary>
    Closed,
    /// <summary>Opening animation; already traversable.</summary>
    Opening,
    /// <summary>Finished opening.</summary>
    Open,
    /// <summary>Closing animation; already blocks movement and shots.</summary>
    Closing,
}

/// <summary>Read-only snapshot of interactable world object progress.</summary>
public struct WorldObjectState
{
    /// <summary>Current gate phase.</summary>
    public GatePhase Gate { get; internal set; }
    /// <summary>Gate animation ticks remaining.</summary>
    public int StateTicks { get; internal set; }
    /// <summary>Whether a chest already granted its loot.</summary>
    public bool Opened { get; internal set; }
    /// <summary>Consecutive nearby-player ticks.</summary>
    public int ProximityTicks { get; internal set; }
    /// <summary>Whether this checkpoint has activated.</summary>
    public bool Activated { get; internal set; }
    /// <summary>The most recent state change was a player interaction opening.</summary>
    public bool Interacted { get; internal set; }
    /// <summary>Latest opening/activation event tick, or -1.</summary>
    public int ChangedTick { get; internal set; }
    internal void Hash(ref Fnv1a hash)
    { hash.Add((int)Gate); hash.Add(StateTicks); hash.Add(Opened); hash.Add(ProximityTicks); hash.Add(Activated); hash.Add(Interacted); hash.Add(ChangedTick); }
}

// Compiled outside ticks for authored placements and definition defaults.
internal readonly struct ObjectRules
{
    internal readonly int Width, Order;
    internal readonly bool AlongX, InitiallyOpen, Interactable;
    internal readonly float Radius;
    internal readonly string Loot;
    internal ObjectRules(EntityKind kind, JsonElement props)
    {
        Width = 2; Order = 1; AlongX = true; InitiallyOpen = Interactable = false; Radius = kind == EntityKind.Checkpoint ? 1.5f : kind == EntityKind.Exit ? 1 : 1.2f; Loot = "";
        if (kind == EntityKind.Gate)
        {
            if (props.TryGetProperty("width", out JsonElement width)) { Width = width.GetInt32(); }
            if (props.TryGetProperty("axis", out JsonElement axis)) { AlongX = axis.GetString() == "x"; }
            if (props.TryGetProperty("state", out JsonElement state)) { InitiallyOpen = state.GetString() == "open"; }
            if (props.TryGetProperty("interactable", out JsonElement interactable)) { Interactable = interactable.GetBoolean(); }
        }
        if (kind == EntityKind.Checkpoint && props.TryGetProperty("order", out JsonElement order)) { Order = order.GetInt32(); }
        if (kind is EntityKind.Checkpoint or EntityKind.Exit && props.TryGetProperty("radius", out JsonElement radius)) { Radius = radius.GetSingle(); }
        if (kind == EntityKind.Chest && props.TryGetProperty("loot", out JsonElement loot)) { Loot = loot.GetString()!; }
    }
    internal void Hash(ref Fnv1a hash)
    { hash.Add(Width); hash.Add(Order); hash.Add(AlongX); hash.Add(InitiallyOpen); hash.Add(Interactable); hash.Add(BitConverter.SingleToInt32Bits(Radius)); hash.Add(Loot.Length); hash.Add(Loot); }
}
