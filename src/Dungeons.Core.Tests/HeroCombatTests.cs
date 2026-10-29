using Dungeons.Core.Content;
using Dungeons.Core.Math;
using Dungeons.Core.Sim;
using Xunit;

namespace Dungeons.Core.Tests;

[Collection("Performance")]
public sealed class HeroCombatTests
{
    private static readonly EntityDef Hero = new("test:hero", EntityKind.Player, new(100, 4.5f));
    private static readonly EntityDef Target = new("test:enemy", EntityKind.Enemy, new(100, 0));
    private static GameSession Arena() => new ScenarioBuilder(20, 20).Entity(Hero, new(5, 5)).Entity(Target, new(6.5f, 5)).Build().Session;
    private static void Idle(GameSession session, int ticks) { for (int i = 0; i < ticks; i++) { session.Step(default(PlayerInput)); } }

    [Fact]
    public void ScRoll01DistanceFramesAndCooldownAreExact()
    {
        GameSession session = Arena(); Entity hero = session.World.Players[0];
        // Roll away from contact; hard separation is tested independently below.
        session.Step(new PlayerInput(new(-1, 0), pressed: InputButtons.Roll));
        for (int age = 1; age <= 24; age++)
        {
            Assert.Equal(25 - age, hero.Action.RollRemaining);
            Assert.Equal(age is >= 3 and <= 20, hero.Health.InvulnTicks > 0);
            if (age < 24) { session.Step(default(PlayerInput)); }
        }
        Assert.InRange(hero.Pos.X, 1.9999f, 2.0001f);
        session.Step(new PlayerInput(default, pressed: InputButtons.Roll));
        Assert.Equal(0, hero.Action.RollRemaining); Assert.Equal(36, hero.Action.RollCooldown);
        for (int i = 0; i < 35; i++) { session.Step(new PlayerInput(default, pressed: InputButtons.Roll)); Assert.Equal(0, hero.Action.RollRemaining); }
        session.Step(new PlayerInput(default, pressed: InputButtons.Roll)); Assert.Equal(24, hero.Action.RollRemaining);
    }

    [Fact]
    public void RollRejectsActualIncomingHitsExactlyOnTicksThreeThroughTwenty()
    {
        for (int age = 1; age <= 24; age++)
        {
            GameSession session = Arena(); Entity hero = session.World.Players[0];
            if (age > 1) { session.Step(new PlayerInput(new(-1, 0), pressed: InputButtons.Roll)); Idle(session, age - 2); }
            session.QueueDamage(2, hero.Id, 12, 1, 0, Vec2.Zero);
            session.Step(age == 1 ? new PlayerInput(new(-1, 0), pressed: InputButtons.Roll) : default);
            Assert.Equal(age is >= 3 and <= 20, hero.Health.Current == 100);
        }
    }

    [Fact]
    public void RollCancelsWindupAndRecoveryButNotActiveAndKeepsHardContacts()
    {
        GameSession session = Arena(); Entity hero = session.World.Players[0];
        session.Step(new PlayerInput(default, new(1, 0), true, InputButtons.Attack));
        session.Step(new PlayerInput(new(-1, 0), pressed: InputButtons.Roll));
        Assert.Equal(AttackPhase.Idle, hero.Action.Phase); Assert.Equal(24, hero.Action.RollRemaining);
        session = Arena(); hero = session.World.Players[0];
        session.Step(new PlayerInput(default, new(1, 0), true, InputButtons.Attack)); Idle(session, 6);
        Assert.Equal(AttackPhase.Active, hero.Action.Phase);
        session.Step(new PlayerInput(default, pressed: InputButtons.Roll)); Assert.Equal(0, hero.Action.RollRemaining);
        Idle(session, 5); Assert.Equal(AttackPhase.Recovery, hero.Action.Phase);
        session.Step(new PlayerInput(new(1, 0), pressed: InputButtons.Roll));
        Assert.Equal(24, hero.Action.RollRemaining); Assert.Equal(AttackPhase.Idle, hero.Action.Phase);
        for (int i = 0; i < 15; i++)
        { session.Step(default(PlayerInput)); Assert.True((session.World.Entities[1].Pos - hero.Pos).Length >= .6999f); }
    }

    [Fact]
    public void ScCmb01FirstActiveTickHitsOnceAndBufferedThirdSwingStaggers()
    {
        GameSession session = Arena(); Entity hero = session.World.Players[0], target = session.World.Entities[1];
        session.DrainEvents();
        session.Step(new PlayerInput(new(1, 0), new(1, 0), true, InputButtons.Attack));
        Idle(session, 5); Assert.Equal(100, target.Health.Current); Assert.Equal(new Vec2(5, 5), hero.Pos);
        session.Step(default(PlayerInput)); Assert.InRange(100 - target.Health.Current, 11, 13);
        int afterFirst = target.Health.Current;
        session.Step(new PlayerInput(default, pressed: InputButtons.Attack)); Idle(session, 4);
        Assert.Equal(afterFirst, target.Health.Current); Assert.Equal(AttackPhase.Active, hero.Action.Phase);
        session.Step(default(PlayerInput)); Assert.Equal(2, hero.Action.Swing); Assert.Equal(AttackPhase.Windup, hero.Action.Phase);
        Idle(session, 6); Assert.Equal(AttackPhase.Active, hero.Action.Phase);
        Assert.InRange(afterFirst - target.Health.Current, 11, 13);
        session.Step(new PlayerInput(default, pressed: InputButtons.Attack)); Idle(session, 5);
        Assert.Equal(3, hero.Action.Swing); Idle(session, 9);
        Assert.Equal(12, target.Action.StaggerTicks);
        Assert.InRange(100 - target.Health.Current, 38, 46);
        SimEvent[] events = session.DrainEvents().ToArray();
        Assert.Equal(3, events.Count(e => e.Kind == SimEventKind.DamageApplied));
        Assert.Equal(new[] { 1, 2, 3 }, events.Where(e => e.Kind == SimEventKind.AttackStarted).Select(e => e.Amount));
        Assert.All(events.Where(e => e.Kind == SimEventKind.DamageApplied), e => Assert.Equal(hero.Id, e.SourceId));
    }

    [Theory]
    [InlineData(6.5f, 5, 0, true)]
    [InlineData(3.5f, 5, 0, false)]
    [InlineData(5, 6.5f, 0, false)]
    [InlineData(7.05f, 5, 0, true)]
    [InlineData(7.06f, 5, 0, false)]
    [InlineData(6.5f, 5, 1, false)]
    public void ScCmb02ArcReachAndHeight(float x, float z, float height, bool expected)
    {
        GameSession session = new ScenarioBuilder(20, 20).Entity(Hero, new(5, 5)).Entity(Target, new(x, z)).Build().Session;
        Entity target = session.World.Entities[1];
        session.Step(new PlayerInput(default, new(1, 0), true, InputButtons.Attack)); Idle(session, 5);
        target.Y = height; session.Step(default(PlayerInput));
        Assert.Equal(expected, target.Health.Current < 100);
    }

    [Fact]
    public void RecoveryMovementComboBufferAndIdleResetFollowTicks()
    {
        GameSession session = Arena(); Entity hero = session.World.Players[0];
        session.Step(new PlayerInput(default, new(1, 0), true, InputButtons.Attack)); Idle(session, 11);
        session.Step(new PlayerInput(new(-1, 0))); Assert.Equal(AttackPhase.Recovery, hero.Action.Phase);
        Assert.InRange(hero.Vel.X, -1.8001f, -1.7999f);
        session.Step(new PlayerInput(default, pressed: InputButtons.Attack)); Assert.Equal(2, hero.Action.Swing);
        Idle(session, 50); session.Step(new PlayerInput(default, pressed: InputButtons.Attack)); Assert.Equal(1, hero.Action.Swing);
        // A press in windup is deliberately not buffered.
        session.Step(new PlayerInput(default, pressed: InputButtons.Attack)); Idle(session, 23); Assert.Equal(AttackPhase.Idle, hero.Action.Phase);
    }

    [Fact]
    public void PotionDuringRollHealsAndOnlyRechargesAfter1800Ticks()
    {
        GameSession session = Arena(); Entity hero = session.World.Players[0]; hero.HealthData.Current = 23;
        session.Step(new PlayerInput(new(-1, 0), pressed: InputButtons.Roll | InputButtons.Potion));
        Assert.Equal(100, hero.Health.Current); Assert.Equal(1800, hero.Action.PotionCooldown);
        hero.HealthData.Current = 50; session.Step(new PlayerInput(default, pressed: InputButtons.Potion)); Assert.Equal(50, hero.Health.Current);
        Idle(session, 1798); Assert.Equal(1, hero.Action.PotionCooldown);
        session.Step(new PlayerInput(default, pressed: InputButtons.Potion)); Assert.Equal(100, hero.Health.Current);
    }

    [Fact]
    public void DeferredDamageUsesArmorGraceAndIndependentCombatRng()
    {
        GameSession session = Arena(); Entity hero = session.World.Players[0]; hero.HealthData.Armor = 100;
        RngState loot = session.World.Randoms.Loot.State;
        session.QueueDamage(2, hero.Id, 12, 1, 1, new(-1, 0)); session.QueueDamage(2, hero.Id, 12, 1, 1, new(-1, 0));
        session.Step(default(PlayerInput)); Assert.InRange(100 - hero.Health.Current, 5, 7); Assert.Equal(15, hero.Health.InvulnTicks);
        Assert.Equal(loot.S0, session.World.Randoms.Loot.State.S0);
        int hp = hero.Health.Current;
        for (int i = 0; i < 14; i++) { session.QueueDamage(2, hero.Id, 12, 1, 0, Vec2.Zero); session.Step(default(PlayerInput)); Assert.Equal(hp, hero.Health.Current); }
        session.QueueDamage(2, hero.Id, 12, 1, 0, Vec2.Zero); session.Step(default(PlayerInput)); Assert.True(hero.Health.Current < hp);
        hero.HealthData.InvulnTicks = 0; session.QueueDamage(2, hero.Id, int.MaxValue, float.MaxValue, 0, Vec2.Zero); session.Step(default(PlayerInput));
        Assert.False(hero.Alive); Assert.Equal(2, session.World.Lives); Assert.Equal(120, hero.Action.DeathTicks);
        session.Step(new PlayerInput(default, pressed: InputButtons.Potion | InputButtons.Roll | InputButtons.Attack)); Assert.Equal(0, hero.Health.Current);
    }

    [Fact]
    public void ScPick01MagnetCollectsEmeraldWithinOneSecondAndHonorsFoodAmmoCaps()
    {
        var emerald = new EntityDef("test:emerald", EntityKind.Pickup, new(1, 0), pickup: new(PickupKind.Emerald, 5));
        var food = new EntityDef("test:food", EntityKind.Pickup, new(1, 0), pickup: new(PickupKind.Food));
        var arrows = new EntityDef("test:arrows", EntityKind.Pickup, new(1, 0), pickup: new(PickupKind.Arrows, 10));
        GameSession session = new ScenarioBuilder(20, 20).Entity(Hero, new(5, 5)).Entity(emerald, new(7.4f, 5)).Entity(food, new(5, 5)).Entity(arrows, new(5, 5)).Build().Session;
        Entity hero = session.World.Players[0]; hero.InventoryData.Arrows = 99;
        Idle(session, 30); Assert.Equal(5, hero.Inventory.Emeralds); Assert.Equal(3, session.World.Entities.Count);
        hero.InventoryData.Arrows = 98; hero.HealthData.Current = 90; session.Step(default(PlayerInput));
        Assert.Single(session.World.Entities); Assert.Equal(100, hero.Health.Current); Assert.Equal(99, hero.Inventory.Arrows);
    }

    [Fact]
    public void ActionReplayIsDeterministicAndWarmedCombatAllocatesNothing()
    {
        var durableTarget = new EntityDef("test:durable", EntityKind.Enemy, new(int.MaxValue, 0));
        var builder = new ScenarioBuilder(20, 20).Entity(Hero, new(5, 5)).Entity(durableTarget, new(6.5f, 5));
        GameSession a = builder.Build(seed: 42).Session, b = builder.Build(seed: 42).Session;
        for (int tick = 0; tick < 600; tick++)
        {
            var input = new PlayerInput(default, new(1, 0), true, tick % 60 == 0 ? InputButtons.Attack : tick % 60 == 8 ? InputButtons.Attack : InputButtons.None);
            a.Step(input); b.Step(input); if (tick % 7 == 0) { a.DrainEvents(); } Assert.Equal(a.Hash(), b.Hash());
        }
        a.DrainEvents(); int hpBefore = a.World.Entities[1].Health.Current;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int tick = 0; tick < 600; tick++)
        {
            a.World.Entities[1].Pos = new(6.5f, 5); a.World.Entities[1].Knockback = Vec2.Zero;
            a.Step(new PlayerInput(default, new(1, 0), true, tick % 30 == 0 ? InputButtons.Attack : InputButtons.None)); a.DrainEvents(); _ = a.Hash();
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(a.World.Entities[1].Health.Current < hpBefore);
    }
}
