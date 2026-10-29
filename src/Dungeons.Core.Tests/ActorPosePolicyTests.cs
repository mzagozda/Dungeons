using Dungeons.Core.Content;
using Dungeons.Core.Sim;
using Dungeons.Game.Rendering;
using Xunit;

namespace Dungeons.Core.Tests;

[Collection("Performance")]
public sealed class ActorPosePolicyTests
{
    private static Entity Hero() => new ScenarioBuilder(20, 20).Entity(MissionAssets.Content.Entities["core:hero"], new(5, 5)).Build().Session.World.Players[0];

    [Fact]
    public void EveryComboPhaseUsesItsOwnTickDurationAndInterpolationFraction()
    {
        Entity hero = Hero();
        for (int combo = 1; combo <= 3; combo++)
        {
            var swing = hero.Def.Weapons.Melee!.Swings[combo - 1];
            foreach (var (phase, pose, ticks) in new[] { (AttackPhase.Windup, ActorPose.Windup, swing.Windup), (AttackPhase.Active, ActorPose.Active, swing.Active), (AttackPhase.Recovery, ActorPose.Recovery, swing.Recovery) })
            {
                hero.ActionData = new() { Phase = phase, Swing = combo, Remaining = ticks, StartedTick = 123 };
                ActorPoseFrame first = ActorPosePolicy.Select(hero, 1, false);
                Assert.Equal(pose, first.Pose); Assert.Equal(ticks * GameConstants.Dt, first.Duration); Assert.Equal(0, first.Age); Assert.Equal(123, first.Cycle);
                hero.ActionData.Remaining = 1;
                Assert.Equal((ticks - 1.5f) * GameConstants.Dt, ActorPosePolicy.Select(hero, .5f, false).Age, 6);
            }
        }
    }

    [Fact]
    public void RangedAndScaledRecoveryFollowTheResolvedWeaponRules()
    {
        Entity hero = Hero(); hero.Stats = new(100, 4.5f, cooldownMultiplier: .73f);
        var bow = hero.Def.Weapons.Ranged!;
        hero.ActionData = new() { Phase = AttackPhase.Windup, Ranged = true, Remaining = bow.Windup, StartedTick = 7 };
        Assert.Equal(ActorPose.Aim, ActorPosePolicy.Select(hero, 1, false).Pose);
        int recovery = (int)System.Math.Ceiling(bow.Recovery * (double)hero.Stats.CooldownMultiplier);
        hero.ActionData.Phase = AttackPhase.Recovery; hero.ActionData.Remaining = recovery;
        ActorPoseFrame fire = ActorPosePolicy.Select(hero, 1, false);
        Assert.Equal(ActorPose.Fire, fire.Pose); Assert.Equal(recovery * GameConstants.Dt, fire.Duration); Assert.Equal(0, fire.Age);
    }

    [Fact]
    public void DeathRollAndStaggerTakePriorityOverHitAndAttacks()
    {
        Entity hero = Hero(); hero.ActionData = new() { RollRemaining = 24, StaggerTicks = 12, Phase = AttackPhase.Windup, Remaining = 6, Swing = 1 };
        Assert.Equal(ActorPose.Roll, ActorPosePolicy.Select(hero, 1, true).Pose);
        hero.ActionData.RollRemaining = 0; Assert.Equal(ActorPose.Stagger, ActorPosePolicy.Select(hero, 1, true).Pose);
        hero.ActionData.StaggerTicks = 0; Assert.Equal(ActorPose.Hit, ActorPosePolicy.Select(hero, 1, true).Pose);
        hero.Alive = false; hero.ActionData.DeathTicks = GameConstants.DeathTicks - 30;
        ActorPoseFrame death = ActorPosePolicy.Select(hero, 1, true);
        Assert.Equal(ActorPose.Death, death.Pose); Assert.Equal(.5f, death.Age, 6);
    }

    [Fact]
    public void EmergingAndCorpseSnapshotsUseTheirOwnCounters()
    {
        var session = new ScenarioBuilder(20, 20).Entity(MissionAssets.Content.Entities["core:zombie"], new(5, 5)).Build().Session;
        Entity enemy = session.World.Entities[0]; enemy.AiData.Mode = EnemyMode.Emerging; enemy.AiData.StateTicks = 15;
        ActorPoseFrame emerging = ActorPosePolicy.Select(enemy, 1, true);
        Assert.Equal(ActorPose.Emerge, emerging.Pose); Assert.Equal(.25f, emerging.Age, 6); Assert.Equal(.5f, emerging.Duration, 6);
        enemy.Alive = false; enemy.LifetimeTicks = GameConstants.CorpseTicks - 45;
        Assert.Equal(.75f, ActorPosePolicy.Select(enemy, 1, false).Age, 6);
    }

    [Fact]
    public void DeathSinkUsesOnlyTheFinalHalfSecondAndResetsOnRespawn()
    {
        Entity hero = Hero(); Assert.Equal(0, ActorPosePolicy.DeathSink(hero, 1));
        hero.Alive = false; hero.ActionData.DeathTicks = 60; Assert.Equal(0, ActorPosePolicy.DeathSink(hero, 1));
        hero.ActionData.DeathTicks = 30; Assert.Equal(0, ActorPosePolicy.DeathSink(hero, 1));
        hero.ActionData.DeathTicks = 15; Assert.Equal(.5f, ActorPosePolicy.DeathSink(hero, 1), 6);
        Assert.Equal(1 - 15.5f * GameConstants.Dt / .5f, ActorPosePolicy.DeathSink(hero, .5f), 6);
        hero.ActionData.DeathTicks = 0; Assert.Equal(1, ActorPosePolicy.DeathSink(hero, 1));
        hero.Alive = true; Assert.Equal(0, ActorPosePolicy.DeathSink(hero, 1));
    }

    [Fact]
    public void WalkRateFollowsResolvedVelocityAndSelectionNeitherAllocatesNorMutatesCore()
    {
        var session = new ScenarioBuilder(20, 20).Entity(MissionAssets.Content.Entities["core:hero"], new(5, 5)).Build().Session;
        Entity hero = session.World.Players[0]; Assert.Equal(ActorPose.Idle, ActorPosePolicy.Select(hero, 1, false).Pose);
        hero.Vel = new(1.35f, 1.8f); ActorPoseFrame walk = ActorPosePolicy.Select(hero, 1, false);
        Assert.Equal(ActorPose.Walk, walk.Pose); Assert.Equal(.5f, walk.Rate, 6);
        ulong hash = session.Hash(); float sum = 0;
        for (int i = 0; i < 1000; i++) { sum += ActorPosePolicy.Select(hero, .5f, false).Rate; }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) { sum += ActorPosePolicy.Select(hero, .5f, false).Rate; }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(sum > 0); Assert.Equal(0, allocated); Assert.Equal(hash, session.Hash());
    }
}
