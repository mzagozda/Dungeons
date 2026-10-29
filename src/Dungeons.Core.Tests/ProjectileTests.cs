using Dungeons.Core.Content;
using Dungeons.Core.Math;
using Dungeons.Core.Sim;
using Xunit;

namespace Dungeons.Core.Tests;

[Collection("Performance")]
public sealed class ProjectileTests
{
    private static readonly EntityDef Hero = new("test:hero", EntityKind.Player, new(100, 4.5f));
    private static readonly EntityDef Enemy = new("test:enemy", EntityKind.Enemy, new(100, 0));
    private static void Idle(GameSession session, int ticks) { for (int i = 0; i < ticks; i++) { session.Step(default(PlayerInput)); } }
    private static void Fire(GameSession session) { session.Step(new PlayerInput(default, new(1, 0), true, InputButtons.Ranged)); Idle(session, 6); }

    [Fact]
    public void BowConsumesOnFireLocksAimAndUsesFullRecoveryMovement()
    {
        GameSession session = new ScenarioBuilder(30, 10).Entity(Hero, new(2.5f, 5)).Build().Session;
        Entity hero = session.World.Players[0]; session.DrainEvents();
        session.Step(new PlayerInput(new(0, 1), new(1, 0), true, InputButtons.Ranged)); Idle(session, 5);
        Assert.Equal(30, hero.Inventory.Arrows); Assert.Single(session.World.Entities); Assert.Equal(new Vec2(2.5f, 5), hero.Pos);
        session.Step(new PlayerInput(new(0, 1), Vec2.North, true));
        Entity shot = Assert.Single(session.World.Entities, e => e.Kind == EntityKind.Projectile);
        Assert.Equal(29, hero.Inventory.Arrows); Assert.Equal(30, hero.Action.BowCooldown);
        Assert.Equal(AttackPhase.Recovery, hero.Action.Phase); Assert.Equal(new Vec2(1, 0), hero.Facing);
        Assert.Equal(new Vec2(14, 0), shot.Projectile.Velocity); Assert.Equal(1, shot.Y); Assert.Equal(90, shot.LifetimeTicks);
        Assert.Equal(hero.Id, shot.Projectile.OwnerId); Assert.InRange(hero.Pos.Z, 5.0749f, 5.0751f);
        Assert.InRange(shot.Pos.X, 2.7332f, 2.7334f); Assert.Equal(5, shot.Pos.Z);
        Idle(session, 12); Assert.Equal(AttackPhase.Idle, hero.Action.Phase);
        session.Step(new PlayerInput(default, pressed: InputButtons.Ranged)); Assert.Equal(AttackPhase.Idle, hero.Action.Phase);
        Idle(session, 17); Assert.Equal(0, hero.Action.BowCooldown);
        hero.InventoryData.Arrows = 0; session.Step(new PlayerInput(default, pressed: InputButtons.Ranged)); Assert.Equal(AttackPhase.Idle, hero.Action.Phase);
        Assert.Single(session.DrainEvents().ToArray(), e => e.Kind == SimEventKind.ProjectileSpawned);
    }

    [Fact]
    public void ScRng02ShotCrossesWaterAndHitsHostileOnlyOnce()
    {
        var builder = new ScenarioBuilder(30, 10).Entity(Hero, new(2.5f, 5)).Entity(Enemy, new(7, 5));
        for (int x = 4; x <= 5; x++) { builder.Cell(x, 5, new Cell(walkable: false)); }
        GameSession session = builder.Build().Session; Entity target = session.World.Entities[1];
        Fire(session); Idle(session, 30);
        Assert.InRange(100 - target.Health.Current, 9, 11);
        Assert.DoesNotContain(session.World.Entities, e => e.Kind == EntityKind.Projectile);
        Assert.Single(session.DrainEvents().ToArray(), e => e.Kind == SimEventKind.ProjectileHitEntity);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShotStopsAtProjectileBlockingOrRaisedCell(bool raised)
    {
        var builder = new ScenarioBuilder(30, 10).Entity(Hero, new(2.5f, 5)).Entity(Enemy, new(7, 5));
        builder.Cell(5, 5, raised ? new Cell(height: 1) : new Cell(blocksProjectiles: true));
        GameSession session = builder.Build().Session; Entity target = session.World.Entities[1];
        Fire(session); Idle(session, 20);
        Assert.Equal(100, target.Health.Current);
        SimEvent impact = Assert.Single(session.DrainEvents().ToArray(), e => e.Kind == SimEventKind.ProjectileHitTerrain);
        Assert.Equal(new IntVec2(5, 5), impact.Cell); Assert.InRange(impact.Position.X, 4.8499f, 4.8501f);
    }

    [Fact]
    public void FastSweepHitsThinWallAndChoosesNearestVictimAndTerrain()
    {
        var fast = new EntityDef("test:fast", EntityKind.Projectile, new(1, 0), .15f, projectile: new(10, 600), faction: Faction.Hero);
        GameSession session = new ScenarioBuilder(30, 10).Entity(Hero, new(2.5f, 5)).Entity(Enemy, new(8, 5)).Cell(5, 5, Cell.Wall).Build().Session;
        Entity hero = session.World.Players[0]; Assert.NotNull(session.World.SpawnProjectile(hero, fast, new(1, 0)));
        session.Step(default(PlayerInput)); Assert.Equal(100, session.World.Entities[1].Health.Current);
        Assert.DoesNotContain(session.World.Entities, e => e.Kind == EntityKind.Projectile);
        Assert.Contains(session.DrainEvents().ToArray(), e => e.Kind == SimEventKind.ProjectileHitTerrain);
        // Reverse authored order: distance, rather than entity id, decides first impact.
        session = new ScenarioBuilder(30, 10).Entity(Hero, new(2.5f, 5)).Entity(Enemy, new(8, 5)).Entity(Enemy, new(5, 5)).Build().Session;
        session.World.SpawnProjectile(session.World.Players[0], fast, new(1, 0)); session.Step(default(PlayerInput));
        Assert.Equal(100, session.World.Entities[1].Health.Current); Assert.InRange(session.World.Entities[2].Health.Current, 89, 91);
    }

    [Fact]
    public void FriendlyShotsPassAndProjectilesPreserveAltitudeAcrossTerrain()
    {
        var friendly = new EntityDef("test:friend", EntityKind.Enemy, new(100, 0), faction: Faction.Hero);
        GameSession session = new ScenarioBuilder(30, 10).Entity(Hero, new(2.5f, 5)).Entity(friendly, new(4, 5)).Entity(Enemy, new(7, 5)).Build().Session;
        Fire(session); Idle(session, 30);
        Assert.Equal(100, session.World.Entities[1].Health.Current); Assert.InRange(session.World.Entities[2].Health.Current, 89, 91);
        session = new ScenarioBuilder(30, 10).Entity(Hero, new(2.5f, 5)).Build().Session;
        Entity arrow = session.World.SpawnProjectile(session.World.Players[0], session.World.ProjectileDefinition("core:arrow_hero"), new(1, 0))!;
        Idle(session, 89); Assert.Equal(1, arrow.Y); Assert.NotNull(session.World.FindEntity(arrow.Id));
        session.Step(default(PlayerInput)); Assert.Null(session.World.FindEntity(arrow.Id));
    }

    [Fact]
    public void PoolEnforcesCapReusesStorageButNeverIdsAndSpawnLoopAllocatesNothing()
    {
        GameSession session = new ScenarioBuilder(30, 10).Entity(Hero, new(2.5f, 5)).Build().Session;
        Entity hero = session.World.Players[0]; EntityDef definition = session.World.ProjectileDefinition("core:arrow_hero");
        for (int i = 0; i < GameConstants.MaxProjectiles; i++) { Assert.NotNull(session.World.SpawnProjectile(hero, definition, new(1, 0))); }
        Assert.Null(session.World.SpawnProjectile(hero, definition, new(1, 0)));
        foreach (Entity e in session.World.Entities) { if (e.Kind == EntityKind.Projectile) { e.LifetimeTicks = 0; } }
        session.Step(default(PlayerInput)); Assert.Single(session.World.Entities);
        int nextId = session.World.NextEntityId;
        Entity shot = session.World.SpawnProjectile(hero, definition, new(1, 0))!; Assert.Equal(nextId, shot.Id);
        shot.LifetimeTicks = 0; session.Step(default(PlayerInput)); session.DrainEvents();
        for (int i = 0; i < 300; i++)
        { session.World.SpawnProjectile(hero, definition, new(1, 0)); session.Step(default(PlayerInput)); session.DrainEvents(); _ = session.Hash(); }
        // Keep actual launches, four-part sweeps, expiration and hash work in the measured loop.
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 600; i++)
        {
            session.World.SpawnProjectile(hero, definition, new(1, 0)); session.Step(default(PlayerInput)); session.DrainEvents(); _ = session.Hash();
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
