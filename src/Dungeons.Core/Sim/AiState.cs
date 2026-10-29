using Dungeons.Core.Math;
using Dungeons.Core.Util;

namespace Dungeons.Core.Sim;

/// <summary>Shared runtime enemy modes; strategy-specific transitions remain in the AI system.</summary>
public enum EnemyMode
{
    /// <summary>Wait or wander near spawn.</summary>
    Idle,
    /// <summary>Initial alert telegraph.</summary>
    Alert,
    /// <summary>Melee pursuit.</summary>
    Chase,
    /// <summary>Melee attack cycle.</summary>
    Attack,
    /// <summary>Find an archer firing position.</summary>
    Position,
    /// <summary>Archer aim telegraph.</summary>
    Aim,
    /// <summary>Archer recovery.</summary>
    Recovery,
    /// <summary>Move away before firing.</summary>
    Retreat,
    /// <summary>Return to authored spawn.</summary>
    Return,
    /// <summary>Interrupted by a strong hit.</summary>
    Stagger,
    /// <summary>Rise from the ground, without collision or damage.</summary>
    Emerging,
    /// <summary>Corpse awaiting removal.</summary>
    Dead,
}

/// <summary>Value snapshot of all future-affecting enemy decisions and navigation cursors.</summary>
public struct AiState
{
    /// <summary>Current behaviour mode.</summary>
    public EnemyMode Mode { get; internal set; }
    /// <summary>Selected runtime target id, or zero.</summary>
    public int TargetId { get; internal set; }
    /// <summary>Ticks remaining in an alert/emergence/retreat mode.</summary>
    public int StateTicks { get; internal set; }
    /// <summary>Authored or dynamic spawn point.</summary>
    public Vec2 SpawnPosition { get; internal set; }
    /// <summary>Last sensed target position.</summary>
    public Vec2 LastKnownPosition { get; internal set; }
    /// <summary>Last tick on which sight or hearing refreshed the target.</summary>
    public int LastSeenTick { get; internal set; }
    /// <summary>Tick of the most recent alert, for bounded one-hop propagation.</summary>
    public int LastAlertTick { get; internal set; }
    /// <summary>Waypoint cursor into the entity route.</summary>
    public int Waypoint { get; internal set; }
    /// <summary>Whether a request is still waiting for the shared A* budget.</summary>
    public bool PathPending { get; internal set; }
    /// <summary>Whether a serviced route is available.</summary>
    public bool HasRoute { get; internal set; }
    /// <summary>Cell most recently requested.</summary>
    public IntVec2 RouteGoal { get; internal set; }
    /// <summary>Remaining minimum interval before a moving-target repath.</summary>
    public int RepathTicks { get; internal set; }
    /// <summary>Unreachable-route backoff.</summary>
    public int RetryTicks { get; internal set; }
    /// <summary>Remaining delay until the next wander choice.</summary>
    public int WanderTicks { get; internal set; }
    /// <summary>Whether a wander point is currently being followed.</summary>
    public bool Wandering { get; internal set; }
    /// <summary>Current idle destination.</summary>
    public Vec2 WanderPosition { get; internal set; }
    /// <summary>Validated per-instance wander radius.</summary>
    public float PatrolRadius { get; internal set; }
    /// <summary>Walking ticks accumulated for the progress test.</summary>
    public int ProgressTicks { get; internal set; }
    /// <summary>Position at the previous progress test.</summary>
    public Vec2 ProgressPosition { get; internal set; }
    /// <summary>Consecutive archer ticks without firing LOS.</summary>
    public int SightLostTicks { get; internal set; }
    /// <summary>Whether this firing cycle has already used its bounded retreat.</summary>
    public bool RetreatSpent { get; internal set; }
    internal void Hash(ref Fnv1a hash)
    {
        hash.Add((int)Mode); hash.Add(TargetId); hash.Add(StateTicks); Vector(ref hash, SpawnPosition); Vector(ref hash, LastKnownPosition);
        hash.Add(LastSeenTick); hash.Add(LastAlertTick); hash.Add(Waypoint); hash.Add(PathPending); hash.Add(HasRoute); hash.Add(RouteGoal.X); hash.Add(RouteGoal.Z);
        hash.Add(RepathTicks); hash.Add(RetryTicks); hash.Add(WanderTicks); hash.Add(Wandering); Vector(ref hash, WanderPosition); hash.Add(BitConverter.SingleToInt32Bits(PatrolRadius));
        hash.Add(ProgressTicks); Vector(ref hash, ProgressPosition); hash.Add(SightLostTicks); hash.Add(RetreatSpent);
    }
    private static void Vector(ref Fnv1a hash, Vec2 vector) { hash.Add(MathUtil.Quantize(vector.X)); hash.Add(MathUtil.Quantize(vector.Z)); }
}

/// <summary>Deterministic target selection through player slots, ready for multiple heroes.</summary>
public static class TargetSelector
{
    /// <summary>Finds the nearest living hostile player, keeping slot/id order for equal distances.</summary>
    public static Entity? NearestPlayer(World world, Entity source)
    {
        Entity? selected = null; float best = float.MaxValue;
        for (int i = 0; i < world.Players.Count; i++)
        {
            Entity target = world.Players[i]; if (!target.Alive || target.Def.Faction == source.Def.Faction) { continue; }
            float squared = Vec2.DistanceSquared(source.Pos, target.Pos);
            if (squared < best) { best = squared; selected = target; }
        }
        return selected;
    }
}
