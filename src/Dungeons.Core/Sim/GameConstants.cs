namespace Dungeons.Core.Sim;

/// <summary>Engine-wide limits and timings from Appendix A; per-kind stats live in definitions.</summary>
public static class GameConstants
{
    /// <summary>Fixed ticks per second.</summary>
    public const int TickRate = 60;
    /// <summary>Fixed simulation step.</summary>
    public const float Dt = 1f / TickRate;
    /// <summary>Maximum simulation steps in a rendered frame.</summary>
    public const int MaxTicksPerFrame = 5;
    /// <summary>Maximum accepted rendered-frame duration.</summary>
    public const double MaxFrameDelta = 0.25;
    /// <summary>Maximum ground speed, including impulses.</summary>
    public const float MaxSpeed = 12;
    /// <summary>Fallback spawn-marker clearance when a code fixture omits the hero prototype.</summary>
    public const float DefaultHeroRadius = .35f;
    /// <summary>Squared-distance tolerance for grazing terrain contacts (metres squared).</summary>
    public const float CollisionTangencyEpsilon = 0.000001f;
    /// <summary>Entity storage cap.</summary>
    public const int MaxEntities = 8192;
    /// <summary>Live monster cap.</summary>
    public const int MaxMonsters = 64;
    /// <summary>Projectile cap.</summary>
    public const int MaxProjectiles = 512;
    /// <summary>Bounded deferred hit queue; overflow fails visibly rather than dropping damage.</summary>
    public const int MaxPendingDamage = 65536;
    /// <summary>Combo input retention.</summary>
    public const int ComboBufferTicks = 15;
    /// <summary>Consecutive voluntarily moving ticks between footstep events (Appendix E).</summary>
    public const int FootstepIntervalTicks = TickRate / 4;
    /// <summary>Hero grace after taking a hit.</summary>
    public const int InvulnAfterHitTicks = 15;
    /// <summary>Enemy reaction duration after a strong impulse.</summary>
    public const int StaggerTicks = 12;
    /// <summary>Enemy corpse lifetime.</summary>
    public const int CorpseTicks = 90;
    /// <summary>Hero death animation before the lives system resolves respawn/failure.</summary>
    public const int DeathTicks = 120;
    /// <summary>Staggered perception interval.</summary>
    public const int PerceptionInterval = 6;
    /// <summary>Alert telegraph.</summary>
    public const int AlertTicks = 18;
    /// <summary>Ticks without sensing a target before returning home.</summary>
    public const int LostTargetTicks = 180;
    /// <summary>Minimum moving-target repath interval.</summary>
    public const int RepathTicks = 15;
    /// <summary>Blocked movement observation window.</summary>
    public const int ProgressTicks = 20;
    /// <summary>Unreachable route retry delay.</summary>
    public const int PathRetryTicks = 60;
    /// <summary>Reserved co-op player slots.</summary>
    public const int MaxPlayers = 4;
    /// <summary>Lives at mission start.</summary>
    public const int InitialLives = 3;
    /// <summary>Events retained between presentation drains.</summary>
    public const int MaxEvents = 4096;
    /// <summary>Spatial pair cap.</summary>
    public const int MaxSeparationPairs = 16384;
    /// <summary>Spatial bucket size in metres.</summary>
    public const float SpatialHashCell = 2;
    /// <summary>Separation passes.</summary>
    public const int SeparationIterations = 2;
    /// <summary>Maximum serviced path requests per tick.</summary>
    public const int MaxPathRequestsPerTick = 20;
    /// <summary>Waypoints retained in an entity path.</summary>
    public const int MaxPathWaypoints = 256;
    /// <summary>Diagonal grid travel cost.</summary>
    public const float DiagonalCost = 1.41421f;
    /// <summary>Per-tick knockback retention.</summary>
    public const float KnockbackDecay = 0.85f;
    /// <summary>Impulse speed below which knockback is removed.</summary>
    public const float KnockbackStop = 0.05f;
    /// <summary>Hash units per metre.</summary>
    public const int HashScale = 1024;
    /// <summary>Recorded hashes between published replay checkpoints.</summary>
    public const int ReplayCheckpointInterval = 60;
    /// <summary>Default replay capacity, ten minutes.</summary>
    public const int DefaultReplayTicks = TickRate * 60 * 10;
    /// <summary>Maximum level width or height.</summary>
    public const int MaxLevelSize = 256;
    /// <summary>Normal cursor snap radius in metres.</summary>
    public const float CursorSnapRange = 1.2f;
    /// <summary>Strong assist cursor snap radius in metres.</summary>
    public const float StrongCursorSnapRange = 1.8f;
    /// <summary>Strong assist halves angular cost while preserving ranges and line of sight.</summary>
    public const float StrongAimAngleWeight = .5f;
}
