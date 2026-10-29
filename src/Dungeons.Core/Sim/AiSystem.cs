using Dungeons.Core.Content;
using Dungeons.Core.Math;

namespace Dungeons.Core.Sim;

public sealed partial class GameSession
{
    private void AiSystem()
    {
        World.ServeDeferredSpawns();
        for (int i = 0; i < World.Active.Count; i++)
        {
            Entity e = World.Active[i]; BrainDef? brain = e.Def.Brain;
            if (!e.Alive || e.Kind != EntityKind.Enemy) { continue; }
            ref AiState ai = ref e.AiData; e.Vel = Vec2.Zero;
            if (ai.Mode == EnemyMode.Emerging)
            {
                if (ai.StateTicks > 0) { continue; }
                Entity? nearest = TargetSelector.NearestPlayer(World, e);
                ai.TargetId = nearest?.Id ?? 0; ai.LastSeenTick = World.Tick; ai.LastKnownPosition = nearest?.Pos ?? e.Pos;
                SetEnemyMode(e, brain is null ? EnemyMode.Idle : EnemyMode.Alert, brain is null ? 0 : GameConstants.AlertTicks);
            }
            if (brain is null) { continue; }
            if (World.Tick % GameConstants.PerceptionInterval == e.Id % GameConstants.PerceptionInterval) { Perceive(e, brain); }
            if (e.ActionData.StaggerTicks > 0) { SetEnemyMode(e, EnemyMode.Stagger); continue; }
            if (ai.Mode == EnemyMode.Stagger) { SetEnemyMode(e, ai.TargetId == 0 ? EnemyMode.Return : Pursuit(brain)); }
            Entity? target = World.FindEntity(ai.TargetId);
            if (ai.TargetId != 0 && (target is null || !target.Alive || World.Tick - ai.LastSeenTick >= GameConstants.LostTargetTicks))
            { ai.TargetId = 0; e.ActionData.Phase = AttackPhase.Idle; e.ActionData.Remaining = 0; SetEnemyMode(e, EnemyMode.Return); target = null; }
            if (ai.Mode == EnemyMode.Alert)
            {
                if (ai.StateTicks > 0) { continue; }
                SetEnemyMode(e, target is null ? EnemyMode.Return : Pursuit(brain));
            }
            if (ai.Mode == EnemyMode.Idle) { Wander(e); continue; }
            if (ai.Mode == EnemyMode.Return)
            {
                if (FollowRoute(e, ai.SpawnPosition, 1))
                { SetEnemyMode(e, EnemyMode.Idle); ai.Wandering = false; ai.WanderTicks = World.Randoms.Ai.NextInt(180, 361); }
                continue;
            }
            if (target is null) { SetEnemyMode(e, EnemyMode.Return); continue; }
            if (ai.Mode is EnemyMode.Attack or EnemyMode.Aim or EnemyMode.Recovery)
            {
                // Decide the next cycle on the tick that recovery expires, without
                // adding an extra idle tick between the authored 36+84 archer phases.
                if (e.ActionData.Phase == AttackPhase.Recovery && e.ActionData.Remaining <= 1)
                { e.ActionData.Phase = AttackPhase.Idle; e.ActionData.Remaining = 0; }
                if (e.ActionData.Phase == AttackPhase.Idle)
                { ai.RetreatSpent = false; SetEnemyMode(e, Pursuit(brain)); }
                else
                {
                    if (e.ActionData.Phase == AttackPhase.Windup && World.Tick - e.ActionData.StartedTick < brain.TrackingTicks)
                    { Vec2 direction = (target.Pos - e.Pos).Normalized; if (direction != Vec2.Zero) { e.Facing = e.ActionData.Direction = direction; } }
                    if (brain.Kind == BrainKind.Archer && e.ActionData.Phase == AttackPhase.Recovery)
                    {
                        SetEnemyMode(e, EnemyMode.Recovery);
                        if (!ai.RetreatSpent && Vec2.DistanceSquared(e.Pos, target.Pos) < brain.RetreatBelow * brain.RetreatBelow)
                        { e.ActionData.Phase = AttackPhase.Idle; e.ActionData.Remaining = 0; StartRetreat(e, brain); }
                    }
                    if (ai.Mode != EnemyMode.Retreat) { continue; }
                }
            }
            if (brain.Kind == BrainKind.MeleeChaser) { ChaseMelee(e, target, brain); }
            else { PositionArcher(e, target, brain); }
        }
    }

    private static EnemyMode Pursuit(BrainDef brain) => brain.Kind == BrainKind.Archer ? EnemyMode.Position : EnemyMode.Chase;

    private void SetEnemyMode(Entity e, EnemyMode mode, int ticks = 0)
    {
        ref AiState ai = ref e.AiData; if (ai.Mode == mode) { return; }
        ai.Mode = mode; ai.StateTicks = ticks; ai.HasRoute = false; ai.Waypoint = 0; ai.RepathTicks = ai.RetryTicks = ai.ProgressTicks = 0;
        ai.ProgressPosition = e.Pos; e.PathCount = 0;
        if (mode == EnemyMode.Alert)
        { ai.LastAlertTick = World.Tick; ai.Wandering = false; World.Emit(new(World.Tick, SimEventKind.EnemyAlerted, e.Id, e.Pos, y: e.Y, text: e.Def.Id)); }
        World.Emit(new(World.Tick, SimEventKind.EnemyStateChanged, e.Id, e.Pos, amount: (int)mode));
    }

    private void Perceive(Entity e, BrainDef brain)
    {
        Entity? selected = null; float best = float.MaxValue;
        for (int p = 0; p < World.Players.Count; p++)
        {
            Entity player = World.Players[p]; if (!player.Alive || player.Def.Faction == e.Def.Faction) { continue; }
            float squared = Vec2.DistanceSquared(e.Pos, player.Pos);
            bool sight = squared <= brain.AggroRadius * brain.AggroRadius && EnemySight(e, player);
            bool noise = squared <= brain.HearingRadius * brain.HearingRadius && player.ActionData.Phase != AttackPhase.Idle && World.Tick - player.ActionData.StartedTick < GameConstants.PerceptionInterval;
            if ((sight || noise) && squared < best) { selected = player; best = squared; }
        }
        if (selected is null)
        {
            for (int i = 0; i < World.Active.Count; i++)
            {
                Entity ally = World.Active[i]; int age = World.Tick - ally.AiData.LastAlertTick;
                if (ally.Id == e.Id || !ally.Alive || ally.Def.Brain is null || ally.Def.Faction != e.Def.Faction || ally.AiData.LastAlertTick < 0 || age < GameConstants.PerceptionInterval || age >= GameConstants.PerceptionInterval * 2 || Vec2.DistanceSquared(e.Pos, ally.Pos) > 36) { continue; }
                Entity? heard = World.FindEntity(ally.AiData.TargetId); if (heard is null || !heard.Alive) { continue; }
                selected = heard; break;
            }
        }
        if (selected is null) { return; }
        ref AiState ai = ref e.AiData; ai.TargetId = selected.Id; ai.LastSeenTick = World.Tick; ai.LastKnownPosition = selected.Pos;
        if (ai.Mode is EnemyMode.Idle or EnemyMode.Return) { SetEnemyMode(e, EnemyMode.Alert, GameConstants.AlertTicks); }
    }

    private bool EnemySight(Entity source, Entity target)
    {
        IntVec2 from = IntVec2.FromPosition(source.Pos), to = IntVec2.FromPosition(target.Pos);
        if (MathF.Abs(source.Y - target.Y) > 1 && World.Grid[from.X, from.Z].StairsDir == StairsDirection.None && World.Grid[to.X, to.Z].StairsDir == StairsDirection.None) { return false; }
        return TerrainSight.Visible(World.Grid, source.Pos, source.Y + .9f, target.Pos, target.Y + .9f);
    }

    private void ChaseMelee(Entity e, Entity target, BrainDef brain)
    {
        Vec2 offset = target.Pos - e.Pos; float distance = offset.Length; MeleeSwing swing = e.Def.Weapons.Melee!.Swings[0];
        // Trigger distance is an outer bound; close far enough for a stationary target to be in the authored hit arc.
        bool range = distance - e.Radius - target.Radius <= brain.TriggerDistance && distance - target.Radius <= swing.Reach;
        if (range && EnemySight(e, target))
        {
            Vec2 direction = distance > 0 ? offset / distance : e.Facing;
            bool facing = Vec2.Dot(e.Facing, direction) >= .8660254f; e.Facing = direction;
            if (facing) { BeginAttack(e, false, 1, direction); SetEnemyMode(e, EnemyMode.Attack); }
            return;
        }
        FollowRoute(e, e.AiData.LastKnownPosition, 1);
    }

    private void StartRetreat(Entity e, BrainDef brain)
    { e.AiData.RetreatSpent = true; SetEnemyMode(e, EnemyMode.Retreat, brain.RetreatTicks); }

    private void PositionArcher(Entity e, Entity target, BrainDef brain)
    {
        ref AiState ai = ref e.AiData; Vec2 offset = target.Pos - e.Pos; float distance = offset.Length; bool visible = EnemySight(e, target);
        ai.SightLostTicks = visible ? 0 : ai.SightLostTicks + 1;
        if (ai.Mode == EnemyMode.Retreat)
        {
            if (ai.StateTicks > 0 && distance < brain.MinimumRange) { RetreatVelocity(e, target); return; }
            if (visible) { BeginAttack(e, true, 0, offset.Normalized); SetEnemyMode(e, EnemyMode.Aim); return; }
            SetEnemyMode(e, EnemyMode.Position);
        }
        if (distance < brain.RetreatBelow && !ai.RetreatSpent) { StartRetreat(e, brain); RetreatVelocity(e, target); return; }
        if (visible && distance >= brain.MinimumRange && distance <= brain.MaximumRange)
        { BeginAttack(e, true, 0, offset.Normalized); SetEnemyMode(e, EnemyMode.Aim); return; }
        if (visible && distance < brain.MinimumRange) { RetreatVelocity(e, target); return; }
        if (ai.SightLostTicks >= 60) { ai.RepathTicks = 0; ai.SightLostTicks = 0; ai.HasRoute = false; }
        FollowRoute(e, ai.LastKnownPosition, 1);
    }

    private void RetreatVelocity(Entity e, Entity target)
    {
        Vec2 direction = (e.Pos - target.Pos).Normalized; if (direction == Vec2.Zero) { direction = new(1, 0); }
        Vec2 best = direction; float gain = Travel(direction);
        if (gain < .0004f)
        {
            Vec2 left = new(-direction.Z, direction.X); float leftGain = Travel(left), rightGain = Travel(-left);
            if (leftGain > gain) { best = left; gain = leftGain; } if (rightGain > gain) { best = -left; }
        }
        e.Vel = best * e.Stats.Speed; Vec2 facing = (target.Pos - e.Pos).Normalized; if (facing != Vec2.Zero) { e.Facing = facing; }
        float Travel(Vec2 candidate)
        { Vec2 velocity = candidate * e.Stats.Speed; return Vec2.DistanceSquared(e.Pos, Collision.Move(World.Grid, e.Pos, ref velocity, e.Radius)); }
    }

    private void Wander(Entity e)
    {
        ref AiState ai = ref e.AiData;
        if (ai.Wandering)
        {
            if (FollowRoute(e, ai.WanderPosition, .5f) || ai.RetryTicks > 0)
            { ai.Wandering = false; ai.WanderTicks = World.Randoms.Ai.NextInt(180, 361); ai.HasRoute = false; e.PathCount = 0; }
            return;
        }
        if (ai.WanderTicks > 0 || ai.PatrolRadius == 0) { return; }
        ai.WanderTicks = World.Randoms.Ai.NextInt(180, 361);
        for (int attempt = 0; attempt < 8; attempt++)
        {
            Vec2 sample = new(World.Randoms.Ai.Range(-1, 1), World.Randoms.Ai.Range(-1, 1)); if (sample.LengthSquared > 1) { continue; }
            Vec2 point = ai.SpawnPosition + sample * ai.PatrolRadius; IntVec2 cell = IntVec2.FromPosition(point);
            if (!World.Grid.IsWalkable(cell.X, cell.Z)) { continue; }
            ai.WanderPosition = point; ai.Wandering = true; ai.HasRoute = false; ai.RepathTicks = 0; FollowRoute(e, point, .5f); break;
        }
    }

    private bool FollowRoute(Entity e, Vec2 goal, float speedFraction)
    {
        ref AiState ai = ref e.AiData;
        if (Vec2.DistanceSquared(e.Pos, goal) <= .09f) { e.Vel = Vec2.Zero; return true; }
        if (ai.RetryTicks > 0) { return false; }
        IntVec2 goalCell = IntVec2.FromPosition(goal); int dx = goalCell.X - ai.RouteGoal.X, dz = goalCell.Z - ai.RouteGoal.Z;
        bool changed = dx * dx + dz * dz > 1;
        if (!ai.PathPending && (!ai.HasRoute || ai.RepathTicks == 0 && changed)) { QueueRoute(e, goalCell); }
        if (!ai.HasRoute) { return false; }
        while (ai.Waypoint < e.PathCount && Vec2.DistanceSquared(e.Pos, e.PathBuffer[ai.Waypoint].Center) <= .09f) { ai.Waypoint++; }
        Vec2 aim;
        if (ai.Waypoint == e.PathCount)
        {
            if (e.PathTruncated) { ai.HasRoute = false; QueueRoute(e, goalCell); return false; }
            if (!e.PathReachedGoal) { ai.HasRoute = false; ai.RetryTicks = GameConstants.PathRetryTicks; return false; }
            aim = goal;
        }
        else
        {
            if (ai.Waypoint + 1 < e.PathCount && WalkableSegment(e, e.PathBuffer[ai.Waypoint + 1].Center)) { ai.Waypoint++; }
            aim = e.PathBuffer[ai.Waypoint].Center;
        }
        Vec2 direction = (aim - e.Pos).Normalized; e.Vel = direction * (e.Stats.Speed * speedFraction); if (direction != Vec2.Zero) { e.Facing = direction; }
        if (++ai.ProgressTicks >= GameConstants.ProgressTicks)
        {
            if (Vec2.DistanceSquared(e.Pos, ai.ProgressPosition) < .0025f && !ai.PathPending)
            {
                var offset = new IntVec2(goalCell.X + World.Randoms.Ai.NextInt(-1, 2), goalCell.Z + World.Randoms.Ai.NextInt(-1, 2));
                QueueRoute(e, World.Grid.IsWalkable(offset.X, offset.Z) ? offset : goalCell);
            }
            ai.ProgressTicks = 0; ai.ProgressPosition = e.Pos;
        }
        return false;
    }

    private void QueueRoute(Entity e, IntVec2 goal)
    {
        if (e.AiData.PathPending || !World.EnqueuePath(e.Id, goal)) { return; }
        e.AiData.PathPending = true; e.AiData.RouteGoal = goal; e.AiData.RepathTicks = GameConstants.RepathTicks;
    }

    private bool WalkableSegment(Entity e, Vec2 target)
    {
        Vec2 displacement = target - e.Pos; float length = displacement.Length;
        if (length > 3) { return false; }
        int samples = System.Math.Max(1, (int)MathF.Ceiling(length / .15f)); Vec2 previous = e.Pos;
        for (int step = 1; step <= samples; step++)
        {
            Vec2 next = e.Pos + displacement * ((float)step / samples), resolved = Collision.Resolve(World.Grid, previous, next, e.Radius);
            if (Vec2.DistanceSquared(resolved, next) > .00000001f) { return false; } previous = next;
        }
        return true;
    }

    private bool EnemyCanFire(Entity e)
    { Entity? target = World.FindEntity(e.AiData.TargetId); return target is not null && target.Alive && EnemySight(e, target); }
}
