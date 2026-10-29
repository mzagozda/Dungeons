using Dungeons.Core.Content;
using Xunit;

namespace Dungeons.Core.Tests;

public sealed class BrainDefinitionTests
{
    [Fact]
    public void AuthoredStrategiesResolveTypedPerceptionAndExactTickWeapons()
    {
        var melee = ContentRegistry.Load(new[] { ("entities/mob.json", """{"id":"test:mob","kind":"Enemy","brain":"core:brain_melee_chaser","stats":{"aggroRadius":7,"hearingRadius":10},"attack":{"kind":"melee","windup":0.45,"active":0.1,"recovery":0.7,"damage":8,"reach":1.4,"halfAngleDeg":45,"knockback":1,"triggerDistance":1.3}}""") });
        Assert.True(melee.IsValid); EntityDef zombie = melee.Entities["test:mob"];
        Assert.Equal(BrainKind.MeleeChaser, zombie.Brain!.Kind); Assert.Equal(7, zombie.Brain.AggroRadius); Assert.Equal(10, zombie.Brain.HearingRadius);
        Assert.Equal(12, zombie.Brain.TrackingTicks); Assert.Equal(1.3f, zombie.Brain.TriggerDistance);
        MeleeSwing swing = zombie.Weapons.Melee!.Swings[0]; Assert.Equal((27, 6, 42, 8), (swing.Windup, swing.Active, swing.Recovery, swing.Damage));
        var ranged = ContentRegistry.Load(new[] {
            ("entities/archer.json", """{"id":"test:archer","kind":"Enemy","brain":"core:brain_archer","attack":{"kind":"ranged","projectile":"test:arrow","aim":0.6,"recovery":1.4,"minRange":5,"maxRange":8,"retreatBelow":3.5,"retreatMax":1.5}}"""),
            ("entities/arrow.json", """{"id":"test:arrow","kind":"Projectile","projectile":{"damage":6,"speed":11}}""") });
        Assert.True(ranged.IsValid); EntityDef skeleton = ranged.Entities["test:archer"];
        Assert.Equal(BrainKind.Archer, skeleton.Brain!.Kind); Assert.Equal(24, skeleton.Brain.TrackingTicks);
        Assert.Equal((5f, 8f, 3.5f, 90), (skeleton.Brain.MinimumRange, skeleton.Brain.MaximumRange, skeleton.Brain.RetreatBelow, skeleton.Brain.RetreatTicks));
        Assert.Equal((36, 84), (skeleton.Weapons.Ranged!.Windup, skeleton.Weapons.Ranged.Recovery));
    }

    [Fact]
    public void IncompatibleStrategyAttacksAndMalformedPerceptionAreRejected()
    {
        Assert.False(ContentRegistry.Load(new[] { ("entities/bad.json", """{"id":"test:bad","kind":"Enemy","brain":"core:brain_melee_chaser","attack":{"kind":"ranged","projectile":"test:arrow"}}""") }).IsValid);
        Assert.False(ContentRegistry.Load(new[] { ("entities/bad.json", """{"id":"test:bad","kind":"Enemy","brain":"core:brain_melee_chaser","stats":{"aggroRadius":"seven"},"attack":{"kind":"melee"}}""") }).IsValid);
        Assert.Throws<ArgumentException>(() => new BrainDef(BrainKind.Archer, 10, 12, minimumRange: 9, maximumRange: 8));
    }
}
