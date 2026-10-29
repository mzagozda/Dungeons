using Dungeons.Core.Content;
using Dungeons.Core.Math;
using Dungeons.Core.Sim;
using Xunit;

namespace Dungeons.Core.Tests;

public sealed class EnemyAiTests
{
    private static readonly EntityDef Hero = new("test:hero", EntityKind.Player, new(100, 4.5f));
    private static readonly WeaponLoadout Claws = new(new WeaponDef("test:claws", new MeleeSwing[] { new(27, 6, 42, 8, 1, 45, 1.4f) }));
    private static readonly EntityDef Zombie = new("test:zombie", EntityKind.Enemy, new(40, 2.6f), weapons: Claws, brain: new(BrainKind.MeleeChaser, 7, 10));
    private static void Idle(GameSession session, int ticks)
    { for (int i = 0; i < ticks; i++) { if (session.World.Players.Count == 0) { session.Step(ReadOnlySpan<PlayerInput>.Empty); } else { session.Step(default(PlayerInput)); } } }
    private static ContentRegistry ArcherContent() => ContentRegistry.Load(new[] {
        ("entities/archer.json", """{"id":"test:archer","kind":"Enemy","radius":0.3,"faction":"Monster","stats":{"hp":25,"speed":2.8,"aggroRadius":10,"hearingRadius":12},"brain":"core:brain_archer","attack":{"kind":"ranged","projectile":"test:arrow","aim":0.6,"recovery":1.4,"minRange":5,"maxRange":8,"retreatBelow":3.5,"retreatMax":1.5}}"""),
        ("entities/arrow.json", """{"id":"test:arrow","kind":"Projectile","radius":0.1,"faction":"Monster","projectile":{"damage":6,"speed":11,"lifetimeTicks":90}}""") });
    private static GameSession ArcherArena(Vec2 hero, Vec2 archer, Action<ScenarioBuilder>? terrain = null)
    {
        ContentRegistry content = ArcherContent(); Assert.True(content.IsValid);
        var builder = new ScenarioBuilder(30, 20).Entity(Hero, hero).Entity(content.Entities["test:archer"], archer);
        terrain?.Invoke(builder); builder.Document.Content = content; return builder.Build().Session;
    }

    [Fact]
    public void ScAi01ZombieAlertsWithinSixTicksReachesAttackAndDealsTelegraphedDamage()
    {
        GameSession session = new ScenarioBuilder(30, 20).Entity(Hero, new(9.4f, 10)).Entity(Zombie, new(2.5f, 10)).Build().Session;
        Entity hero = session.World.Players[0], zombie = session.World.Entities[1];
        Idle(session, 6); Assert.Equal(EnemyMode.Alert, zombie.Ai.Mode); Assert.InRange(zombie.Ai.LastAlertTick, 1, 6);
        int alert = zombie.Ai.LastAlertTick;
        while (session.Tick < 180 && zombie.Action.Phase == AttackPhase.Idle) { session.Step(default(PlayerInput)); }
        Assert.Equal(AttackPhase.Windup, zombie.Action.Phase); Assert.Equal(EnemyMode.Attack, zombie.Ai.Mode);
        Assert.True(session.Tick >= alert + 18); Assert.True(session.Tick <= 180);
        Assert.Equal(100, hero.Health.Current); Idle(session, 26); Assert.Equal(100, hero.Health.Current);
        session.Step(default(PlayerInput)); Assert.InRange(100 - hero.Health.Current, 7, 9);
        Assert.Equal(AttackPhase.Active, zombie.Action.Phase);
    }

    [Fact]
    public void ScAi02LostTargetReturnsToSpawnAndIdles()
    {
        GameSession session = new ScenarioBuilder(40, 40).Entity(Hero, new(9.4f, 5.5f)).Entity(Zombie, new(2.5f, 5.5f)).Build().Session;
        Idle(session, 30); Entity hero = session.World.Players[0], zombie = session.World.Entities[1]; Assert.Equal(EnemyMode.Chase, zombie.Ai.Mode);
        hero.Pos = new(35, 35); Idle(session, 400);
        Assert.Equal(EnemyMode.Idle, zombie.Ai.Mode); Assert.Equal(0, zombie.Ai.TargetId);
        Assert.InRange((zombie.Pos - zombie.Ai.SpawnPosition).Length, 0, .301f);
    }

    [Fact]
    public void AttackNoiseCrossesVisionWallAndAlertsPropagateAtMostOneHopPerWindow()
    {
        GameSession session = new ScenarioBuilder(20, 20).Entity(Hero, new(8, 5)).Entity(Zombie, new(3, 5)).Cell(5, 5, Cell.Wall).Build().Session;
        Idle(session, 6); Assert.Equal(EnemyMode.Idle, session.World.Entities[1].Ai.Mode);
        session.Step(new PlayerInput(default, new(1, 0), true, InputButtons.Attack)); Idle(session, 5);
        Assert.Equal(EnemyMode.Alert, session.World.Entities[1].Ai.Mode);
        var shortSight = new EntityDef("test:guard", EntityKind.Enemy, new(40, 2.6f), weapons: Claws, brain: new(BrainKind.MeleeChaser, 5, 0));
        session = new ScenarioBuilder(30, 30).Entity(Hero, new(10, 1)).Entity(shortSight, new(10, 5)).Entity(shortSight, new(10, 10)).Entity(shortSight, new(10, 15)).Build().Session;
        Idle(session, 6); Assert.Equal(EnemyMode.Alert, session.World.Entities[1].Ai.Mode); Assert.Equal(EnemyMode.Idle, session.World.Entities[2].Ai.Mode);
        Idle(session, 6); Assert.Equal(EnemyMode.Alert, session.World.Entities[2].Ai.Mode); Assert.Equal(EnemyMode.Idle, session.World.Entities[3].Ai.Mode);
        Idle(session, 6); Assert.Equal(EnemyMode.Alert, session.World.Entities[3].Ai.Mode);
        Assert.True(session.World.Entities[2].Ai.LastAlertTick - session.World.Entities[1].Ai.LastAlertTick >= 6);
        Assert.True(session.World.Entities[3].Ai.LastAlertTick - session.World.Entities[2].Ai.LastAlertTick >= 6);
    }

    [Fact]
    public void ZombieTracksOnlyFirstTwelveWindupTicksAndStaggerCancelsAttack()
    {
        GameSession session = new ScenarioBuilder(20, 20).Entity(Hero, new(7, 5)).Entity(Zombie, new(5.5f, 5)).Build().Session;
        Entity hero = session.World.Players[0], zombie = session.World.Entities[1];
        while (session.Tick < 60 && zombie.Action.Phase != AttackPhase.Windup) { session.Step(default(PlayerInput)); }
        Assert.Equal(AttackPhase.Windup, zombie.Action.Phase);
        hero.Pos = zombie.Pos + new Vec2(0, -1.5f); session.Step(default(PlayerInput)); Assert.Equal(Vec2.North, zombie.Facing);
        Idle(session, 11); Vec2 locked = zombie.Facing; hero.Pos = zombie.Pos + new Vec2(0, 1.5f); session.Step(default(PlayerInput)); Assert.Equal(locked, zombie.Facing);
        session.QueueDamage(hero.Id, zombie.Id, 1, 1, 2.5f, new(1, 0)); session.Step(default(PlayerInput));
        Assert.Equal(AttackPhase.Idle, zombie.Action.Phase); Assert.Equal(12, zombie.Action.StaggerTicks);
        session.Step(default(PlayerInput)); Assert.Equal(EnemyMode.Stagger, zombie.Ai.Mode);
    }

    [Fact]
    public void IdleWanderUsesAiStreamHalfSpeedAndSpawnRadius()
    {
        GameSession session = new ScenarioBuilder(20, 20).Entity(Zombie, new(10, 10)).Build().Session; Entity zombie = session.World.Entities[0];
        RngState combat = session.World.Randoms.Combat.State; bool moved = false;
        for (int tick = 0; tick < 600; tick++)
        {
            Idle(session, 1); moved |= zombie.Vel.LengthSquared > 0;
            Assert.InRange(zombie.Vel.Length, 0, 1.3001f); Assert.InRange((zombie.Pos - zombie.Ai.SpawnPosition).Length, 0, 2.001f);
        }
        Assert.True(moved); Assert.Equal(combat.S0, session.World.Randoms.Combat.State.S0);
    }

    [Fact]
    public void ArcherTelegraphShootsMonsterArrowWithoutConsumingHeroAmmo()
    {
        GameSession session = ArcherArena(new(10, 10), new(4, 10)); Entity archer = session.World.Entities[1];
        while (session.Tick < 100 && archer.Ai.Mode != EnemyMode.Aim) { session.Step(default(PlayerInput)); }
        Assert.Equal(EnemyMode.Aim, archer.Ai.Mode); int start = session.Tick; Idle(session, 35);
        Assert.DoesNotContain(session.World.Entities, e => e.Kind == EntityKind.Projectile);
        session.Step(default(PlayerInput)); Entity arrow = Assert.Single(session.World.Entities, e => e.Kind == EntityKind.Projectile);
        Assert.Equal(start + 36, session.Tick); Assert.Equal(archer.Id, arrow.Projectile.OwnerId); Assert.Equal(Faction.Monster, arrow.Projectile.Faction);
        Assert.Equal(0, archer.Inventory.Arrows); Assert.Equal(30, session.World.Players[0].Inventory.Arrows);
        Idle(session, 35); Assert.InRange(session.World.Players[0].Health.Current, 93, 95);
    }

    [Fact]
    public void ScRng01ArcherRepositionsAroundVisionBlockBeforeFiring()
    {
        GameSession session = ArcherArena(new(10, 5.5f), new(4, 5.5f), builder => builder.Cell(7, 5, Cell.Wall));
        Entity hero = session.World.Players[0], archer = session.World.Entities[1]; session.DrainEvents();
        session.Step(new PlayerInput(default, new(1, 0), true, InputButtons.Attack)); bool fired = false, moved = false;
        for (int tick = 0; tick < 240 && !fired; tick++)
        {
            session.Step(default(PlayerInput)); moved |= archer.Pos != archer.Ai.SpawnPosition;
            foreach (SimEvent ev in session.DrainEvents())
            {
                if (ev.Kind != SimEventKind.ProjectileSpawned || ev.SourceId != archer.Id) { continue; }
                Assert.True(TerrainSight.Visible(session.World.Grid, archer.Pos, archer.Y + .9f, hero.Pos, hero.Y + .9f)); fired = true;
            }
        }
        Assert.True(moved); Assert.True(fired);
    }

    [Fact]
    public void ArcherRetreatIsBoundedAndFiresEvenWhenTooCloseInNarrowDeadEnd()
    {
        GameSession session = ArcherArena(new(7.5f, 5.5f), new(5.5f, 5.5f), builder =>
        {
            for (int x = 4; x < 15; x++) { builder.Cell(x, 4, Cell.Wall); builder.Cell(x, 6, Cell.Wall); }
            builder.Cell(4, 5, Cell.Wall);
        });
        Entity hero = session.World.Players[0], archer = session.World.Entities[1]; bool retreated = false, fired = false;
        for (int tick = 0; tick < 155 && !fired; tick++)
        {
            session.Step(default(PlayerInput)); retreated |= archer.Ai.Mode == EnemyMode.Retreat;
            foreach (SimEvent ev in session.DrainEvents()) { fired |= ev.Kind == SimEventKind.ProjectileSpawned && ev.SourceId == archer.Id; }
        }
        Assert.True(retreated); Assert.True(fired); Assert.True((hero.Pos - archer.Pos).Length < 3.5f);
    }

    [Fact]
    public void ArcherRechecksLineOfSightAtFireAndDoesNotShootThroughNewWall()
    {
        GameSession session = ArcherArena(new(10, 10), new(4, 10)); Entity archer = session.World.Entities[1];
        while (session.Tick < 100 && archer.Ai.Mode != EnemyMode.Aim) { session.Step(default(PlayerInput)); }
        Idle(session, 35); session.World.Grid.SetRuntimeCell(7, 10, Cell.Wall); session.DrainEvents();
        session.Step(default(PlayerInput));
        Assert.Equal(AttackPhase.Idle, archer.Action.Phase); Assert.Equal(EnemyMode.Position, archer.Ai.Mode);
        Assert.DoesNotContain(session.DrainEvents().ToArray(), e => e.Kind == SimEventKind.ProjectileSpawned && e.SourceId == archer.Id);
    }

    [Fact]
    public void StationaryArcherCycleHasExactlyThirtySixAimAndEightyFourRecoveryTicks()
    {
        GameSession session = ArcherArena(new(10, 10), new(4, 10)); int first = 0, second = 0;
        for (int tick = 0; tick < 200 && second == 0; tick++)
        {
            session.Step(default(PlayerInput));
            foreach (SimEvent ev in session.DrainEvents())
            { if (ev.Kind == SimEventKind.ProjectileSpawned && ev.SourceId == 2) { if (first == 0) { first = ev.Tick; } else { second = ev.Tick; } } }
        }
        Assert.True(first > 0); Assert.Equal(120, second - first);
    }
}
