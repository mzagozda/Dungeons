using System.Text.Json;
using Dungeons.Core.Content;
using Dungeons.Core.Level;
using Dungeons.Core.Math;
using Dungeons.Core.Sim;
using Xunit;

namespace Dungeons.Core.Tests;

[Collection("Performance")]
public sealed class WorldObjectTests
{
    private static readonly EntityDef Hero = new("test:hero", EntityKind.Player, new(100, 4.5f));
    private static readonly EntityDef Gate = new("test:gate", EntityKind.Gate, new(1, 0), 0, true);
    private static readonly EntityDef Checkpoint = new("test:checkpoint", EntityKind.Checkpoint, new(1, 0), 0);
    private static EntityPlacement Placement(EntityDef definition, Vec2 point, string id, string props)
    { using JsonDocument document = JsonDocument.Parse(props); return new(definition, point, id, Vec2.North, Array.Empty<string>(), document.RootElement.Clone()); }
    private static void Idle(GameSession session, int ticks)
    { for (int i = 0; i < ticks; i++) { session.Step(default(PlayerInput)); session.DrainEvents(); } }

    [Theory]
    [InlineData("x")]
    [InlineData("z")]
    public void GatesShareCollisionNavigationAndProjectileFlagsAndPushActorsOnClosing(string axis)
    {
        var builder = new ScenarioBuilder(12, 12).Entity(Hero, new(2.5f, 2.5f));
        builder.Document.AddEntity(Placement(Gate, new(5.5f, 5.5f), "gate", $$"""{"axis":"{{axis}}","width":2,"state":"closed"}"""));
        GameSession session = builder.Build().Session; Entity gate = session.World.Entities[1];
        int firstX = axis == "x" ? 4 : 5, firstZ = axis == "z" ? 4 : 5;
        Assert.True(session.World.Grid[firstX, firstZ].Solid); Assert.True(session.World.Grid[firstX, firstZ].BlocksProjectiles);
        Assert.False(session.World.Grid.IsWalkable(firstX, firstZ)); Assert.False(session.World.Grid[firstX, firstZ].BlocksVision);
        Assert.True(session.World.SetGate(gate, true)); Assert.False(session.World.SetGate(gate, true));
        Assert.Equal(GatePhase.Opening, gate.WorldObject.Gate); Assert.True(session.World.Grid.IsWalkable(firstX, firstZ));
        Assert.False(session.World.Grid[firstX, firstZ].BlocksProjectiles); Idle(session, 44); Assert.Equal(GatePhase.Opening, gate.WorldObject.Gate);
        Idle(session, 1); Assert.Equal(GatePhase.Open, gate.WorldObject.Gate);
        Entity hero = session.World.Players[0]; hero.Pos = new(firstX + .5f, firstZ + .5f);
        Assert.True(session.World.SetGate(gate, false)); Assert.Equal(GatePhase.Closing, gate.WorldObject.Gate);
        Assert.True(session.World.CircleFits(hero.Pos, hero.Radius)); Assert.NotEqual(new Vec2(firstX + .5f, firstZ + .5f), hero.Pos);
        Assert.Equal(hero.Pos, hero.PrevPos); Idle(session, 45); Assert.Equal(GatePhase.Closed, gate.WorldObject.Gate);
    }

    [Fact]
    public void GateClosingDisplacesCollectibleCirclesBeforeBlockingTheCells()
    {
        var builder = new ScenarioBuilder(12, 12).Entity(Hero, new(2.5f, 2.5f));
        builder.Document.AddEntity(Placement(Gate, new(5.5f, 5.5f), "gate", """{"axis":"x","width":1,"state":"open"}"""));
        var emerald = new EntityDef("test:emerald", EntityKind.Pickup, new(1, 0), .2f, pickup: new(PickupKind.Emerald));
        builder.Entity(emerald, new(5.5f, 5.5f));
        GameSession session = builder.Build().Session;
        Assert.Null(Dungeons.Tools.SimulationFuzz.Inspect(session.World));
        Assert.True(session.World.SetGate(session.World.Entities[1], false));
        Assert.Null(Dungeons.Tools.SimulationFuzz.Inspect(session.World));
        Assert.True(session.World.CircleFits(session.World.Entities[2].Pos, session.World.Entities[2].Radius));
    }

    [Fact]
    public void UnplaceableCollectibleRollsBackGateAndDoesNotConsumeIntersectingShots()
    {
        var builder = new ScenarioBuilder(7, 7);
        for (int z = 0; z < 7; z++) { for (int x = 0; x < 7; x++) { builder.Cell(x, z, Cell.Wall); } }
        // One-cell-wide passages fit the hero; closing their centre leaves no
        // ground that can hold a wider collectible's complete collision circle.
        for (int x = 1; x < 6; x++) { builder.Cell(x, 3, Cell.Floor); }
        for (int z = 1; z < 6; z++) { builder.Cell(3, z, Cell.Floor); }
        builder.Entity(Hero, new(1.5f, 3.5f));
        builder.Document.AddEntity(Placement(Gate, new(3.5f, 3.5f), "gate", """{"width":1,"state":"open"}"""));
        var pickup = new EntityDef("test:wide-pickup", EntityKind.Pickup, new(1, 0), .6f, pickup: new(PickupKind.Emerald));
        builder.Entity(pickup, new(3.5f, 3.5f)); GameSession session = builder.Build().Session;
        var arrow = new EntityDef("test:arrow", EntityKind.Projectile, new(1, 0), .15f, projectile: new(10, 14));
        Entity shot = session.World.SpawnProjectile(session.World.Players[0], arrow, new(1, 0))!; shot.Pos = shot.PrevPos = new(3.5f, 3.5f);
        session.DrainEvents(); ulong before = session.Hash();
        Assert.False(session.World.SetGate(session.World.Entities[1], false));
        Assert.Equal(before, session.Hash()); Assert.True(shot.Alive); Assert.Equal(0, session.DrainEvents().Length);
    }

    [Theory]
    [InlineData(5.5f, 5.5f, true)]
    [InlineData(4.9f, 5.5f, true)]
    [InlineData(4.85f, 5.5f, false)]
    [InlineData(4.9f, 4.9f, true)]
    [InlineData(4.85f, 4.85f, false)]
    public void ClosingGateHitsOnlyShotsWhoseCircleOverlapsItsFootprint(float x, float z, bool hit)
    {
        var builder = new ScenarioBuilder(12, 12).Entity(Hero, new(2.5f, 2.5f));
        builder.Document.AddEntity(Placement(Gate, new(5.5f, 5.5f), "gate", """{"width":1,"state":"open"}"""));
        GameSession session = builder.Build().Session;
        var arrow = new EntityDef("test:arrow", EntityKind.Projectile, new(1, 0), .15f, projectile: new(10, 14));
        Entity shot = session.World.SpawnProjectile(session.World.Players[0], arrow, new(1, 0))!; shot.Pos = shot.PrevPos = new(x, z);
        session.DrainEvents(); Assert.True(session.World.SetGate(session.World.Entities[1], false));
        Assert.Equal(!hit, shot.Alive);
        SimEvent[] impacts = session.DrainEvents().ToArray().Where(e => e.Kind == SimEventKind.ProjectileHitTerrain).ToArray();
        if (hit) { Assert.Equal(0, shot.LifetimeTicks); Assert.Single(impacts); Assert.Equal(shot.Id, impacts[0].EntityId); Assert.Equal(new(5, 5), impacts[0].Cell); }
        else { Assert.Empty(impacts); }
        Assert.Null(Dungeons.Tools.SimulationFuzz.Inspect(session.World));
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void GateCollectibleDisplacementAndShotImpactAllocateNothing()
    {
        var builder = new ScenarioBuilder(12, 12).Entity(Hero, new(2.5f, 2.5f));
        builder.Document.AddEntity(Placement(Gate, new(5.5f, 5.5f), "gate", """{"width":1,"state":"open"}"""));
        builder.Entity(new("test:emerald", EntityKind.Pickup, new(1, 0), .2f, pickup: new(PickupKind.Emerald)), new(5.5f, 5.5f));
        GameSession session = builder.Build().Session; Entity gate = session.World.Entities[1], pickup = session.World.Entities[2];
        Entity shot = session.World.SpawnProjectile(session.World.Players[0], new("test:arrow", EntityKind.Projectile, new(1, 0), .15f, projectile: new(10, 14)), new(1, 0))!;
        void Close()
        {
            session.World.SetGate(gate, true); pickup.Pos = pickup.PrevPos = new(5.5f, 5.5f);
            shot.Alive = true; shot.LifetimeTicks = 100; shot.Pos = shot.PrevPos = new(5.5f, 5.5f);
            if (!session.World.SetGate(gate, false) || shot.Alive || !session.World.CircleFits(pickup.Pos, pickup.Radius)) { throw new InvalidOperationException("Gate volume transition failed."); }
            session.DrainEvents(); _ = session.Hash();
        }
        for (int count = 0; count < 100; count++) { Close(); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int count = 0; count < 200; count++) { Close(); }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void InitiallyOpenGatePreservesTerrainAndOverlappingClosedGateStillBlocks()
    {
        var builder = new ScenarioBuilder(12, 12).Entity(Hero, new(2.5f, 2.5f)).Cell(4, 5, new(moveCost: .9f));
        builder.Document.AddEntity(Placement(Gate, new(5, 5.5f), "open", """{"state":"open","width":2,"axis":"x"}"""));
        builder.Document.AddEntity(Placement(Gate, new(5, 5.5f), "closed", """{"state":"closed","width":2,"axis":"x"}"""));
        GameSession session = builder.Build().Session; Assert.False(session.World.Grid.IsWalkable(4, 5));
        Assert.True(session.World.SetGate(session.World.Entities[2], true)); Assert.True(session.World.Grid.IsWalkable(4, 5)); Assert.Equal(.9f, session.World.Grid[4, 5].MoveCost);
    }

    [Fact]
    public void SpawnedGateRegistersAndRemovalRestoresTerrainBeforeItsPoolSlotIsReused()
    {
        GameSession session = new ScenarioBuilder(12, 12).Entity(Hero, new(2.5f, 2.5f)).Build().Session;
        var settings = new SpawnSettings(Placement(Gate, default, "runtime", """{"axis":"x","width":2,"state":"closed"}"""));
        Entity gate = session.World.SpawnEntity(Gate, new(5, 5.5f), Vec2.North, settings: settings)!;
        Assert.NotNull(gate); Assert.True(session.World.Grid[4, 5].Solid); Assert.False(session.World.Grid.IsWalkable(5, 5));
        Assert.True(session.World.SetGate(gate, true)); Assert.True(session.World.Grid.IsWalkable(4, 5));
        Assert.True(session.World.SetGate(gate, false)); session.World.Despawn(gate); session.World.RemoveExpired();
        Assert.True(session.World.Grid.IsWalkable(4, 5)); Assert.False(session.World.Grid[4, 5].BlocksProjectiles);
        Entity prop = session.World.SpawnEntity(new("test:prop", EntityKind.Prop, new(1, 0), 0), new(8.5f, 8.5f), Vec2.North)!;
        Assert.Same(gate, prop);
        Entity next = session.World.SpawnEntity(Gate, new(5, 5.5f), Vec2.North, settings: settings)!;
        session.World.SetGate(next, true); Assert.True(session.World.Grid.IsWalkable(4, 5));
        Assert.Null(session.World.SpawnEntity(Gate, new(.5f, 5.5f), Vec2.North, settings: settings));
    }

    [Fact]
    public void ClosingWithoutSafeGroundRollsBackGateTerrainAndActorsIncludingDeadPlayerAnchor()
    {
        var builder = new ScenarioBuilder(4, 4).Entity(Hero, new(1.5f, 1.5f));
        for (int z = 0; z < 4; z++) { for (int x = 0; x < 4; x++) { builder.Cell(x, z, new(solid: true)); } }
        builder.Cell(1, 1, new(walkable: true));
        builder.Document.AddEntity(Placement(Gate, new(1.5f, 1.5f), "gate", """{"width":1,"state":"open"}"""));
        GameSession session = builder.Build().Session; Entity hero = session.World.Players[0], gate = session.World.Entities[1];
        ulong before = session.Hash();
        Assert.False(session.World.SetGate(gate, false)); Assert.Equal(before, session.Hash());
        Assert.Equal(GatePhase.Open, gate.WorldObject.Gate); Assert.True(session.World.Grid.IsWalkable(1, 1)); Assert.Equal(new(1.5f, 1.5f), hero.Pos);
        hero.Alive = false;
        Assert.False(session.World.SetGate(gate, false)); Assert.True(session.World.Grid.IsWalkable(1, 1));
    }

    [Fact]
    public void DeferredMonsterRevalidatesPositionWhenGateClosesWhileWaiting()
    {
        var enemy = new EntityDef("test:mob", EntityKind.Enemy, new(100, 0));
        var builder = new ScenarioBuilder(12, 12).Entity(Hero, new(2.5f, 2.5f)).Entity(enemy, new(8.5f, 8.5f));
        builder.Document.AddEntity(Placement(Gate, new(5, 5.5f), "gate", """{"axis":"x","width":2,"state":"open"}"""));
        GameSession session = new(builder.Document, difficulty: new(maxMonsters: 1));
        Assert.True(session.World.RequestMonsterSpawn(enemy, new(4.5f, 5.5f), Vec2.North));
        Assert.Equal(1, session.World.PendingMonsterSpawns); session.World.SetGate(session.World.Entities[2], false);
        session.World.Despawn(session.World.Entities[1]); session.World.RemoveExpired(); session.World.ServeDeferredSpawns();
        Assert.Equal(0, session.World.PendingMonsterSpawns); Entity spawned = Assert.Single(session.World.Entities, e => e.Kind == EntityKind.Enemy);
        Assert.True(session.World.CircleFits(spawned.Pos, spawned.Radius)); Assert.NotEqual(new Vec2(4.5f, 5.5f), spawned.Pos);
    }

    [Fact]
    public void ChestRequiresTwentyConsecutiveNearbyTicksAndGrantsLootOnlyOnce()
    {
        ContentRegistry content = ContentRegistry.Load(new[] { ("loot/chest.json", """{"id":"test:chest_loot","rows":[{"item":"emerald","chance":1,"min":3,"max":3}]}""") }); Assert.True(content.IsValid);
        var builder = new ScenarioBuilder(12, 12).Entity(Hero, new(4.5f, 5.5f)); builder.Document.Content = content;
        var chestDef = new EntityDef("test:chest", EntityKind.Chest, new(1, 0), .35f, true);
        builder.Document.AddEntity(Placement(chestDef, new(5.5f, 5.5f), "chest", """{"loot":"test:chest_loot"}"""));
        GameSession session = builder.Build().Session; Entity chest = session.World.Entities[1], hero = session.World.Players[0];
        Idle(session, 19); Assert.False(chest.WorldObject.Opened); hero.Pos = new(2.5f, 2.5f); Idle(session, 1); Assert.Equal(0, chest.WorldObject.ProximityTicks);
        hero.Pos = new(4.5f, 5.5f); Idle(session, 19); Assert.False(chest.WorldObject.Opened);
        session.Step(default(PlayerInput)); Assert.True(chest.WorldObject.Opened);
        Assert.Single(session.DrainEvents().ToArray(), e => e.Kind == SimEventKind.ChestOpened);
        Assert.Equal(3, session.World.Entities.Count(e => e.Kind == EntityKind.Pickup)); Assert.False(session.World.OpenChest(chest));
        Idle(session, 90); Assert.Equal(3, hero.Inventory.Emeralds); Assert.Equal(2, session.World.Entities.Count);
    }

    [Fact]
    public void CheckpointsRespectOrderAndRespawnPreservesProgressWithGraceAndEnemyDisplacement()
    {
        var enemyDef = new EntityDef("test:enemy", EntityKind.Enemy, new(100, 0));
        var builder = new ScenarioBuilder(20, 20).Entity(Hero, new(2.5f, 2.5f)).Entity(enemyDef, new(10.5f, 10.5f));
        builder.Document.AddEntity(Placement(Checkpoint, new(10.5f, 10.5f), "high", """{"order":2,"radius":1.5}"""));
        builder.Document.AddEntity(Placement(Checkpoint, new(2.5f, 2.5f), "low", """{"order":1,"radius":1.5}"""));
        GameSession session = builder.Build().Session; Entity hero = session.World.Players[0], enemy = session.World.Entities[1];
        session.Step(default(PlayerInput)); Assert.Equal(1, session.World.CheckpointOrder);
        Assert.True(session.World.ActivateCheckpoint(session.World.Entities[2])); Assert.False(session.World.ActivateCheckpoint(session.World.Entities[3]));
        hero.InventoryData.Emeralds = 7; hero.InventoryData.Arrows = 1; hero.Experience = 20; hero.ActionData.PotionCooldown = 100; enemy.HealthData.Current = 50;
        session.QueueDamage(enemy.Id, hero.Id, 1000, 1, 0, default); session.Step(default(PlayerInput));
        int died = session.Tick; Assert.False(hero.Alive); Idle(session, 119); Assert.False(hero.Alive); session.Step(default(PlayerInput));
        Assert.Equal(died + 120, session.Tick); Assert.True(hero.Alive); Assert.Equal(100, hero.Health.Current); Assert.Equal(60, hero.Health.InvulnTicks);
        Assert.Equal(10, hero.Inventory.Arrows); Assert.Equal(7, hero.Inventory.Emeralds); Assert.Equal(20, hero.Experience); Assert.Equal(0, hero.Action.PotionCooldown);
        Assert.Equal(session.World.CheckpointPosition, hero.Pos); Assert.Equal(hero.Pos, hero.PrevPos); Assert.Equal(2, session.World.Lives); Assert.Equal(1, session.World.Deaths);
        Assert.Equal(50, enemy.Health.Current); Assert.True((enemy.Pos - hero.Pos).Length >= 3); Assert.Equal(EnemyMode.Idle, enemy.Ai.Mode); Assert.Equal(0, enemy.Ai.TargetId);
        Assert.Contains(session.DrainEvents().ToArray(), e => e.Kind == SimEventKind.PlayerRespawned);
    }

    [Fact]
    public void LastLifeFailsOnlyAfterTheDeathAnimationAndCannotAdvanceAgain()
    {
        GameSession session = new ScenarioBuilder(12, 12).Entity(Hero, new(5.5f, 5.5f)).Build().Session;
        session.World.Lives = 1; session.QueueDamage(0, session.World.Players[0].Id, 1000, 1, 0, default); session.Step(default(PlayerInput));
        Idle(session, 119); Assert.Equal(SessionState.Running, session.State); session.Step(default(PlayerInput)); Assert.Equal(SessionState.Failed, session.State);
        Assert.Contains(session.DrainEvents().ToArray(), e => e.Kind == SimEventKind.MissionFailed); int tick = session.Tick; Idle(session, 20); Assert.Equal(tick, session.Tick);
    }

    [Fact, Trait("Category", "Performance")]
    public void ObjectTransitionsAndRespawnAllocateNothingAfterWarmupAndAffectHashes()
    {
        var builder = new ScenarioBuilder(12, 12).Entity(Hero, new(2.5f, 2.5f)); builder.Document.AddEntity(Placement(Gate, new(5.5f, 5.5f), "gate", "{}"));
        GameSession session = builder.Build().Session; Entity gate = session.World.Entities[1];
        void Cycle()
        {
            session.World.SetGate(gate, true); Idle(session, 45); session.World.SetGate(gate, false); Idle(session, 45);
            session.World.Lives = 3; Entity hero = session.World.Players[0]; hero.HealthData.InvulnTicks = 0;
            session.QueueDamage(0, hero.Id, 1000, 1, 0, default); session.Step(default(PlayerInput)); Idle(session, 120); session.Hash();
        }
        Cycle(); Cycle(); long before = GC.GetAllocatedBytesForCurrentThread(); Cycle(); Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        ulong original = session.Hash(); gate.ObjectData.ProximityTicks++; Assert.NotEqual(original, session.Hash());
    }

    [Fact]
    public void MutableObjectSnapshotsParticipateInWorldIdentity()
    {
        var builder = new ScenarioBuilder(12, 12).Entity(Hero, new(2.5f, 2.5f)); builder.Document.AddEntity(Placement(Gate, new(5.5f, 5.5f), "gate", "{}"));
        GameSession session = builder.Build().Session; Entity gate = session.World.Entities[1]; ulong before = session.Hash();
        gate.ObjectData.ProximityTicks++; Assert.NotEqual(before, session.Hash()); gate.ObjectData.ProximityTicks--; Assert.Equal(before, session.Hash());
    }
}
