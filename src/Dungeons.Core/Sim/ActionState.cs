using Dungeons.Core.Math;

namespace Dungeons.Core.Sim;

/// <summary>Deterministic attack phases, independent of animation playback.</summary>
public enum AttackPhase
{
    /// <summary>No attack.</summary>
    Idle,
    /// <summary>Telegraph before a hit.</summary>
    Windup,
    /// <summary>Attack frames; collision is checked only on entry.</summary>
    Active,
    /// <summary>Return to movement or chain another swing.</summary>
    Recovery,
}

/// <summary>Value snapshot of action timers. Core alone mutates these fields.</summary>
public struct ActionState
{
    /// <summary>Current attack phase.</summary>
    public AttackPhase Phase { get; internal set; }
    /// <summary>One-based melee combo index; zero for ranged.</summary>
    public int Swing { get; internal set; }
    /// <summary>Whether the current action is ranged.</summary>
    public bool Ranged { get; internal set; }
    /// <summary>Ticks remaining in the current attack phase.</summary>
    public int Remaining { get; internal set; }
    /// <summary>Tick at which the action was requested.</summary>
    public int StartedTick { get; internal set; }
    /// <summary>Remaining combo input buffer.</summary>
    public int BufferTicks { get; internal set; }
    /// <summary>Facing captured when the action began.</summary>
    public Vec2 Direction { get; internal set; }
    /// <summary>Remaining roll ticks, including the current movement tick.</summary>
    public int RollRemaining { get; internal set; }
    /// <summary>Direction captured when rolling began.</summary>
    public Vec2 RollDirection { get; internal set; }
    /// <summary>Cooldown measured from roll completion.</summary>
    public int RollCooldown { get; internal set; }
    /// <summary>Potion recharge timer.</summary>
    public int PotionCooldown { get; internal set; }
    /// <summary>Bow recharge timer measured from firing.</summary>
    public int BowCooldown { get; internal set; }
    /// <summary>Remaining enemy stagger.</summary>
    public int StaggerTicks { get; internal set; }
    /// <summary>Remaining hero death animation.</summary>
    public int DeathTicks { get; internal set; }
    internal Vec2 Move;
}

internal readonly struct PendingDamage(int sourceId, int targetId, int damage, float multiplier, float knockback, Vec2 direction, int meleeSwing)
{
    public int SourceId { get; } = sourceId;
    public int TargetId { get; } = targetId;
    public int Damage { get; } = damage;
    public float Multiplier { get; } = multiplier;
    public float Knockback { get; } = knockback;
    public Vec2 Direction { get; } = direction;
    public int MeleeSwing { get; } = meleeSwing;
}

/// <summary>Launch-time ownership and velocity; later owner movement has no effect.</summary>
public struct ProjectileState
{
    /// <summary>Source entity id, preserved after its death/removal.</summary>
    public int OwnerId { get; internal set; }
    /// <summary>Launch allegiance.</summary>
    public Content.Faction Faction { get; internal set; }
    /// <summary>Constant ground-plane velocity.</summary>
    public Vec2 Velocity { get; internal set; }
    /// <summary>Launch-time gear damage multiplier.</summary>
    public float DamageMultiplier { get; internal set; }
}
