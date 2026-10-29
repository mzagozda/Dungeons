using Dungeons.Core.Math;

namespace Dungeons.Core.Sim;

/// <summary>Latched action bits; artifact slots are reserved for later content.</summary>
[Flags]
public enum InputButtons : ushort
{
    /// <summary>No action.</summary>
    None = 0,
    /// <summary>Sword attack.</summary>
    Attack = 1,
    /// <summary>Bow attack.</summary>
    Ranged = 2,
    /// <summary>Dodge roll.</summary>
    Roll = 4,
    /// <summary>Healing potion.</summary>
    Potion = 8,
    /// <summary>Use nearby object.</summary>
    Interact = 16,
    /// <summary>Pause request.</summary>
    Pause = 32,
    /// <summary>Reserved first artifact slot.</summary>
    Artifact1 = 64,
    /// <summary>Reserved second artifact slot.</summary>
    Artifact2 = 128,
    /// <summary>Reserved third artifact slot.</summary>
    Artifact3 = 256,
}

/// <summary>One player slot's sanitized world-space command for one tick.</summary>
public readonly struct PlayerInput : IEquatable<PlayerInput>
{
    /// <summary>Constructs input; non-finite vectors become zero and movement is clamped.</summary>
    public PlayerInput(Vec2 move, Vec2 aim = default, bool aimValid = false, InputButtons pressed = InputButtons.None, InputButtons held = InputButtons.None)
    {
        Move = move.IsFinite ? move.ClampLength(1) : Vec2.Zero;
        Aim = aim.IsFinite ? aim.Normalized : Vec2.Zero;
        AimValid = aimValid && Aim != Vec2.Zero;
        Pressed = pressed; Held = held;
    }
    private PlayerInput(Vec2 move, Vec2 aim, bool aimValid, InputButtons pressed, InputButtons held, bool recorded)
    { Move = move; Aim = aim; AimValid = aimValid; Pressed = pressed; Held = held; }
    internal static PlayerInput FromRecorded(Vec2 move, Vec2 aim, bool aimValid, InputButtons pressed, InputButtons held)
    {
        // Re-normalizing a recorded float unit vector can change its bits. Validate then preserve.
        if (!move.IsFinite || !aim.IsFinite || move.LengthSquared > 1.000001f || aim.LengthSquared > 1.000001f ||
            aimValid && (aim.LengthSquared < 0.999999f || aim.LengthSquared > 1.000001f)) { throw new ArgumentException("Replay input is not sanitized."); }
        return new(move, aim, aimValid, pressed, held, true);
    }
    /// <summary>World XZ movement with magnitude at most one.</summary>
    public Vec2 Move { get; }
    /// <summary>Unit aim or zero.</summary>
    public Vec2 Aim { get; }
    /// <summary>Whether explicit aiming is active.</summary>
    public bool AimValid { get; }
    /// <summary>Edges since the last tick.</summary>
    public InputButtons Pressed { get; }
    /// <summary>Currently held buttons.</summary>
    public InputButtons Held { get; }
    /// <inheritdoc />
    public bool Equals(PlayerInput other) => Move == other.Move && Aim == other.Aim && AimValid == other.AimValid && Pressed == other.Pressed && Held == other.Held;
    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is PlayerInput other && Equals(other);
    /// <inheritdoc />
    public override int GetHashCode() => unchecked(((Move.GetHashCode() * 397 ^ Aim.GetHashCode()) * 397 ^ (int)Pressed) * 397 ^ (int)Held ^ (AimValid ? 1 : 0));
}
