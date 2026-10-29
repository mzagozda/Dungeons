using Dungeons.Core.Math;

namespace Dungeons.Core.Sim;

/// <summary>Immutable editor play-test origin. Checkpoint is an optional authored checkpoint id, never a runtime id.</summary>
public sealed class StartOverride : IEquatable<StartOverride>
{
    /// <summary>Creates an origin; the session also checks its actual level bounds and hero collision footprint.</summary>
    public StartOverride(Vec2 pos, string? checkpoint = null)
    {
        if (!pos.IsFinite || pos.X < 0 || pos.Z < 0 || pos.X >= GameConstants.MaxLevelSize || pos.Z >= GameConstants.MaxLevelSize) { throw new ArgumentOutOfRangeException(nameof(pos)); }
        if (checkpoint is not null && (checkpoint.Length is < 1 or > 64 || checkpoint.Any(c => c is not (>= 'a' and <= 'z') and not (>= 'A' and <= 'Z') and not (>= '0' and <= '9') and not '_')))
        { throw new ArgumentException("Checkpoint must be an authored id.", nameof(checkpoint)); }
        Pos = pos; Checkpoint = checkpoint;
    }
    /// <summary>Exact first-player position; blocked/outside footprints are rejected instead of silently relocated.</summary>
    public Vec2 Pos { get; }
    /// <summary>Checkpoint to activate before startup logic. Null makes Pos the initial respawn anchor.</summary>
    public string? Checkpoint { get; }
    /// <inheritdoc/>
    public bool Equals(StartOverride? other) => other is not null && Pos == other.Pos && StringComparer.Ordinal.Equals(Checkpoint, other.Checkpoint);
    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is StartOverride other && Equals(other);
    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Pos, Checkpoint);
}

public sealed partial class World
{
    private void ApplyStartOverride(StartOverride origin)
    {
        if (_players.Count == 0) { throw new ArgumentException("A start override requires a player.", nameof(origin)); }
        Entity hero = _players[0];
        if (!CircleFits(hero.Pos, hero.Radius)) { throw new ArgumentException("Start override does not fit on traversable terrain.", nameof(origin)); }
        if (origin.Checkpoint is null) { return; }
        Entity? checkpoint = FindAuthoredEntity(origin.Checkpoint);
        if (checkpoint is not { Kind: EntityKind.Checkpoint, Alive: true }) { throw new ArgumentException("Start override checkpoint is missing or has the wrong kind.", nameof(origin)); }
        if (!ActivateCheckpoint(checkpoint)) { throw new ArgumentException("Start override checkpoint cannot be activated.", nameof(origin)); }
    }
}
