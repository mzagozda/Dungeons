using Dungeons.Core.Content;
using Dungeons.Core.Math;
using Dungeons.Core.Sim;
using Xunit;

namespace Dungeons.Core.Tests;

[Collection("Performance")]
public sealed class SpawnsAndLootTests
{
    private static readonly EntityDef Hero = new("test:hero", EntityKind.Player, new(100, 4.5f));
    private static readonly EntityDef Monster = new("test:monster", EntityKind.Enemy, new(40, 0));
    private static ContentRegistry LootContent() => ContentRegistry.Load(new[] {
        ("entities/mob.json", """{"id":"test:mob","kind":"Enemy","radius":0.35,"stats":{"hp":1,"speed":0,"xp":12},"loot":"test:drops"}"""),
        ("loot/drops.json", """{"id":"test:drops","rows":[{"item":"emerald","chance":1,"min":3,"max":3},{"item":"food","chance":1},{"item":"arrows","chance":1}]}""") });
    private static void Idle(GameSession session, int ticks) { for (int i = 0; i < ticks; i++) { session.Step(default(PlayerInput)); } }

    [Fact]
    public void AuthoredSurplusMonstersQueueWithTheirImmutablePropertiesAndAuthoredIds()
    {
        var builder = new ScenarioBuilder(40, 40).Entity(Hero, new(2.5f, 2.5f));
        var brain = new EntityDef("test:chaser", EntityKind.Enemy, new(40, 0),
            weapons: new(new WeaponDef("test:claws", new MeleeSwing[] { new(27, 6, 42, 8, 1, 45, 1.4f) })), brain: new(BrainKind.MeleeChaser, 0, 0));
        using var properties = System.Text.Json.JsonDocument.Parse("""{"patrolRadius":0.25}""");
        for (int i = 0; i < 66; i++)
        {
            builder.Document.AddEntity(new(brain, new(5.5f + i % 9 * 3, 5.5f + i / 9 * 3), "enemy_" + i,
                new(1, 0), new[] { "authored" }, properties.RootElement.Clone()));
        }
        GameSession session = builder.Build().Session; Assert.Equal(64, session.World.LiveMonsterCount); Assert.Equal(2, session.World.PendingMonsterSpawns);
        int nextId = session.World.NextEntityId;
        session.QueueDamage(1, 2, 1000, 1, 0, default); session.QueueDamage(1, 3, 1000, 1, 0, default); Idle(session, 2);
        Assert.Equal(64, session.World.LiveMonsterCount); Assert.Equal(0, session.World.PendingMonsterSpawns);
        Entity first = session.World.FindEntity(nextId)!; Assert.Equal("enemy_64", first.LevelId); Assert.Equal("enemy_65", session.World.FindEntity(nextId + 1)!.LevelId);
        Assert.Equal(.25f, first.Ai.PatrolRadius); Assert.Equal(.25f, first.Properties.GetProperty("patrolRadius").GetSingle());
        Assert.Contains("authored", first.Tags); Assert.Equal(new Vec2(1, 0), first.Facing); Assert.Equal(EnemyMode.Emerging, first.Ai.Mode);
    }

    [Fact]
    public void DeferredSettingsParticipateInIdentityAndInvalidFacingNeverEntersTheQueue()
    {
        var builder = new ScenarioBuilder(12, 12).Entity(Hero, new(2.5f, 2.5f)).Entity(Monster, new(5.5f, 5.5f));
        GameSession a = new(builder.Document, difficulty: new(maxMonsters: 1)), b = new(builder.Document, difficulty: new(maxMonsters: 1));
        using var left = System.Text.Json.JsonDocument.Parse("""{"custom":1}"""); using var right = System.Text.Json.JsonDocument.Parse("""{"custom":2}""");
        var one = new SpawnSettings(new Dungeons.Core.Level.EntityPlacement(Monster, new(6.5f, 6.5f), "deferred", Vec2.North, Array.Empty<string>(), left.RootElement.Clone()));
        var two = new SpawnSettings(new Dungeons.Core.Level.EntityPlacement(Monster, new(6.5f, 6.5f), "deferred", Vec2.North, Array.Empty<string>(), right.RootElement.Clone()));
        Assert.True(a.World.RequestMonsterSpawn(Monster, new(6.5f, 6.5f), Vec2.North, settings: one));
        Assert.True(b.World.RequestMonsterSpawn(Monster, new(6.5f, 6.5f), Vec2.North, settings: two)); Assert.NotEqual(a.Hash(), b.Hash());
        Assert.False(a.World.RequestMonsterSpawn(Monster, new(6.5f, 6.5f), new(float.NaN, 0))); Assert.Equal(1, a.World.PendingMonsterSpawns);
    }

    [Fact]
    public void LiveCapDefersInOrderAndDeathReleasesCapacityBeforeCorpseRemoval()
    {
        GameSession session = new ScenarioBuilder(40, 40).Entity(Hero, new(2, 2)).Build().Session; World world = session.World;
        for (int i = 0; i < 64; i++) { Assert.NotNull(world.SpawnEntity(Monster, new(5.5f + i % 8 * 2, 5.5f + i / 8 * 2), Vec2.North)); }
        Assert.Equal(64, world.LiveMonsterCount); ulong before = session.Hash();
        Assert.True(world.RequestMonsterSpawn(Monster, new(25, 25), Vec2.North, new[] { "wave" }));
        Assert.Equal(1, world.PendingMonsterSpawns); Assert.True(world.HasPendingTag("wave")); Assert.NotEqual(before, session.Hash());
        int victim = world.Entities[1].Id, next = world.NextEntityId;
        session.QueueDamage(1, victim, 100, 1, 0, Vec2.Zero); session.Step(default(PlayerInput));
        Assert.False(world.FindEntity(victim)!.Alive); Assert.Equal(63, world.LiveMonsterCount); Assert.Equal(1, world.PendingMonsterSpawns);
        session.Step(default(PlayerInput));
        Entity spawned = world.FindEntity(next)!; Assert.NotNull(spawned); Assert.Equal(64, world.LiveMonsterCount);
        Assert.Equal(0, world.PendingMonsterSpawns); Assert.False(world.HasPendingTag("wave")); Assert.Contains("wave", spawned.Tags);
        Assert.Equal(EnemyMode.Emerging, spawned.Ai.Mode); Assert.Equal(30, spawned.Ai.StateTicks); Assert.Equal(30, spawned.Health.InvulnTicks);
        Assert.NotNull(world.FindEntity(victim)); // Corpse is retained for its authored 90 ticks.
    }

    [Fact]
    public void EmergingMonsterHasThirtyTicksWithoutCollisionOrDamage()
    {
        GameSession session = new ScenarioBuilder(20, 20).Entity(Hero, new(10, 10)).Build().Session;
        Assert.True(session.World.RequestMonsterSpawn(Monster, new(10, 10), Vec2.North)); Entity emerging = session.World.Entities[1];
        for (int tick = 1; tick < 30; tick++)
        {
            session.QueueDamage(1, emerging.Id, 10, 1, 0, Vec2.Zero); session.Step(default(PlayerInput));
            Assert.Equal(40, emerging.Health.Current); Assert.Equal(new Vec2(10, 10), emerging.Pos); Assert.Equal(new Vec2(10, 10), session.World.Players[0].Pos);
        }
        session.QueueDamage(1, emerging.Id, 10, 1, 0, Vec2.Zero); session.Step(default(PlayerInput));
        Assert.Equal(EnemyMode.Idle, emerging.Ai.Mode); Assert.InRange(emerging.Health.Current, 29, 31);
        Assert.InRange((emerging.Pos - session.World.Players[0].Pos).Length, .6999f, .7001f);
    }

    [Fact]
    public void EnemyDeathRollsIndependentRowsScattersIndividualEmeraldsAndAwardsXpOnce()
    {
        ContentRegistry content = LootContent(); Assert.True(content.IsValid);
        var builder = new ScenarioBuilder(30, 30).Entity(Hero, new(10, 10)).Entity(content.Entities["test:mob"], new(10.8f, 10)); builder.Document.Content = content;
        GameSession session = builder.Build().Session; Entity hero = session.World.Players[0], target = session.World.Entities[1]; hero.HealthData.Current = 50;
        session.QueueDamage(hero.Id, target.Id, 10, 1, 0, Vec2.Zero); session.QueueDamage(hero.Id, target.Id, 10, 1, 0, Vec2.Zero); session.Step(default(PlayerInput));
        Assert.Equal(1, session.World.Kills); Assert.Equal(12, hero.Experience); Assert.False(target.Alive);
        Assert.Equal(1, session.World.KillsByType["test:mob"]);
        Entity[] drops = session.World.Entities.Where(e => e.Kind == EntityKind.Pickup).ToArray(); Assert.Equal(5, drops.Length);
        Assert.Equal(3, drops.Count(e => e.Def.Pickup!.Kind == PickupKind.Emerald));
        foreach (Entity drop in drops) { Assert.Equal(24, drop.PickupArmTicks); Assert.InRange((drop.Pos - target.Pos).Length, .2999f, .9001f); }
        Assert.All(drops.Where(e => e.Def.Pickup!.Kind == PickupKind.Emerald), e => Assert.Equal(1, e.PickupAmount));
        Idle(session, 23); Assert.Equal(0, hero.Inventory.Emeralds); Assert.Equal(50, hero.Health.Current);
        Idle(session, 30); Assert.Equal(3, hero.Inventory.Emeralds); Assert.Equal(75, hero.Health.Current); Assert.Equal(40, hero.Inventory.Arrows);
        Assert.DoesNotContain(session.World.Entities, e => e.Kind == EntityKind.Pickup); Assert.Equal(1, session.World.Kills);
    }

    [Fact]
    public void LootRandomnessAndDeferredSpawnDefinitionStatsAreHashed()
    {
        ContentRegistry content = LootContent();
        var builder = new ScenarioBuilder(30, 30).Entity(Hero, new(2, 2)); builder.Document.Content = content;
        GameSession a = builder.Build(seed: 42).Session, b = builder.Build(seed: 42).Session;
        RngState combat = a.World.Randoms.Combat.State;
        for (int i = 0; i < 20; i++) { a.World.DropLoot("test:drops", new(15, 15)); b.World.DropLoot("test:drops", new(15, 15)); }
        Assert.Equal(a.Hash(), b.Hash()); Assert.Equal(combat.S0, a.World.Randoms.Combat.State.S0);
        for (int i = 0; i < 64; i++) { a.World.SpawnEntity(Monster, new(5 + i % 8 * 2, 5 + i / 8 * 2), Vec2.North); b.World.SpawnEntity(Monster, new(5 + i % 8 * 2, 5 + i / 8 * 2), Vec2.North); }
        Assert.Equal(a.Hash(), b.Hash());
        var stronger = new EntityDef(Monster.Id, EntityKind.Enemy, new(80, 0));
        a.World.RequestMonsterSpawn(Monster, new(25, 25), Vec2.North); b.World.RequestMonsterSpawn(stronger, new(25, 25), Vec2.North);
        Assert.NotEqual(a.Hash(), b.Hash());
    }

    [Fact]
    public void LootScatterAndMagnetRespectWallsAndCircleBoundsWithoutExtraRandomDraws()
    {
        ContentRegistry content = LootContent();
        var open = new ScenarioBuilder(8, 8).Entity(Hero, new(6.5f, 6.5f)); open.Document.Content = content;
        var blocked = new ScenarioBuilder(8, 8).Entity(Hero, new(6.5f, 6.5f)).Cell(1, 0, Cell.Wall); blocked.Document.Content = content;
        GameSession first = open.Build(seed: 51).Session, second = blocked.Build(seed: 51).Session;
        first.World.DropLoot("test:drops", new(.4f, .4f)); second.World.DropLoot("test:drops", new(.4f, .4f));
        Assert.Equal(first.World.Randoms.Loot.State.S0, second.World.Randoms.Loot.State.S0);
        Assert.Equal(first.World.Randoms.Loot.State.S1, second.World.Randoms.Loot.State.S1);
        Assert.Equal(first.World.Randoms.Loot.State.S2, second.World.Randoms.Loot.State.S2);
        Assert.Equal(first.World.Randoms.Loot.State.S3, second.World.Randoms.Loot.State.S3);
        Assert.Equal(5, second.World.Entities.Count(entity => entity.Kind == EntityKind.Pickup));
        Assert.Null(Dungeons.Tools.SimulationFuzz.Inspect(first.World)); Assert.Null(Dungeons.Tools.SimulationFuzz.Inspect(second.World));

        // Attraction must not pull a collectible through a wall toward a nearby hero.
        var magnet = new ScenarioBuilder(8, 8).Entity(Hero, new(3.5f, 3.5f)); magnet.Document.Content = content;
        for (int z = 0; z < 8; z++) { magnet.Cell(2, z, Cell.Wall); }
        GameSession session = magnet.Build().Session; session.World.DropLoot("test:drops", new(1.5f, 3.5f));
        for (int tick = 0; tick < 180; tick++) { session.Step(default(PlayerInput)); Assert.Null(Dungeons.Tools.SimulationFuzz.Inspect(session.World)); }
        Assert.All(session.World.Entities.Where(entity => entity.Kind == EntityKind.Pickup), entity => Assert.True(entity.Pos.X + entity.Radius <= 2.00001f));
        Assert.Equal(0, session.World.Players[0].Inventory.Emeralds);
    }

    [Fact]
    public void WarmedSpawnDeathLootAndPoolReuseLoopAllocatesZeroBytes()
    {
        ContentRegistry content = LootContent(); var builder = new ScenarioBuilder(30, 30).Entity(Hero, new(2, 2)); builder.Document.Content = content;
        GameSession session = builder.Build().Session; EntityDef prototype = content.Entities["test:mob"];
        void Tick()
        {
            Entity target = session.World.SpawnEntity(prototype, new(15, 15), Vec2.North)!;
            session.QueueDamage(1, target.Id, 10, 1, 0, Vec2.Zero); session.Step(default(PlayerInput)); session.DrainEvents(); _ = session.Hash();
        }
        for (int i = 0; i < 100; i++) { Tick(); }
        long before = GC.GetAllocatedBytesForCurrentThread(); for (int i = 0; i < 120; i++) { Tick(); }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before); Assert.Equal(220, session.World.Kills);
    }
}
