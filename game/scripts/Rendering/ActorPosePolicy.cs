using Dungeons.Core.Sim;

namespace Dungeons.Game.Rendering;

public enum ActorPose { Idle, Walk, Windup, Active, Recovery, Aim, Fire, Roll, Hit, Stagger, Death, Emerge }
public readonly record struct ActorPoseFrame(ActorPose Pose, float Duration, float Age, float Rate = 1, int Cycle = -1);

/// <summary>Read-only snapshot selection; presentation timing never changes authoritative actions.</summary>
public static class ActorPosePolicy
{
    public const float TransitionSeconds = .03f, WalkReferenceSpeed = 4.5f;
    /// <summary>Sink only during the final half-second; interpolation never changes the Core lifetime.</summary>
    public static float DeathSink(Entity entity, float alpha)
    {
        if (entity.Alive) { return 0; }
        int remaining = entity.Kind == EntityKind.Player ? entity.Action.DeathTicks : entity.LifetimeTicks;
        return System.Math.Clamp(1 - (remaining + 1 - alpha) * GameConstants.Dt / .5f, 0, 1);
    }
    public static ActorPoseFrame Select(Entity entity, float alpha, bool hit)
    {
        ActionState action = entity.Action;
        ActorPoseFrame Timed(ActorPose pose, int duration, int remaining, int cycle = -1) =>
            new(pose, duration * GameConstants.Dt, MathF.Max(0, (duration - remaining - 1 + alpha) * GameConstants.Dt), Cycle: cycle);
        if (!entity.Alive)
        {
            int elapsed = entity.Kind == EntityKind.Player ? GameConstants.DeathTicks - action.DeathTicks : GameConstants.CorpseTicks - entity.LifetimeTicks;
            return new(ActorPose.Death, 0, MathF.Max(0, (elapsed - 1 + alpha) * GameConstants.Dt));
        }
        if (entity.Kind == EntityKind.Enemy && entity.Ai.Mode == EnemyMode.Emerging) { return Timed(ActorPose.Emerge, 30, entity.Ai.StateTicks); }
        if (action.RollRemaining > 0) { return Timed(ActorPose.Roll, entity.Def.Controls.RollTicks, action.RollRemaining); }
        if (action.StaggerTicks > 0 || entity.Kind == EntityKind.Enemy && entity.Ai.Mode == EnemyMode.Stagger)
        { return Timed(ActorPose.Stagger, GameConstants.StaggerTicks, action.StaggerTicks); }
        if (hit) { return new(ActorPose.Hit, .1f, 0); }
        if (action.Phase != AttackPhase.Idle)
        {
            if (action.Ranged && entity.Def.Weapons.Ranged is { } bow)
            { return action.Phase == AttackPhase.Windup ? Timed(ActorPose.Aim, bow.Windup, action.Remaining, action.StartedTick) : Timed(ActorPose.Fire, Cooldown(bow.Recovery, entity), action.Remaining, action.StartedTick); }
            if (entity.Def.Weapons.Melee is { } weapon)
            {
                var swing = weapon.Swings[System.Math.Clamp(action.Swing - 1, 0, weapon.Swings.Length - 1)];
                return action.Phase switch
                {
                    AttackPhase.Windup => Timed(ActorPose.Windup, swing.Windup, action.Remaining, action.StartedTick),
                    AttackPhase.Active => Timed(ActorPose.Active, swing.Active, action.Remaining, action.StartedTick),
                    _ => Timed(ActorPose.Recovery, Cooldown(swing.Recovery, entity), action.Remaining, action.StartedTick)
                };
            }
        }
        return entity.Vel.LengthSquared > 0 ? new(ActorPose.Walk, 0, 0, entity.Vel.Length / WalkReferenceSpeed) : new(ActorPose.Idle, 0, 0);
    }
    private static int Cooldown(int ticks, Entity entity) => (int)System.Math.Min(int.MaxValue, System.Math.Ceiling(ticks * (double)entity.Stats.CooldownMultiplier));
}
