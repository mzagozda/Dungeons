using System.Text.Json;
using System.Text.Json.Nodes;
using Dungeons.Core.Content;
using Dungeons.Core.Level;
using Dungeons.Core.Math;
using Dungeons.Core.Sim;
using Xunit;

namespace Dungeons.Core.Tests;

[Collection("Performance")]
public sealed class SpawnerTests
{
    private static readonly ContentRegistry Content = ContentRegistry.Load(new[] { ("entities/mob.json", """{"id":"test:mob","kind":"Enemy","radius":0.3,"stats":{"hp":1,"speed":0}}""") });
    private static GameSession Create(string activation, int count = 6, int maxAlive = 2, float interval = .5f, DifficultyProfile? difficulty = null, bool record = false)
    {
        var builder = new ScenarioBuilder(20, 20).Entity(new EntityDef("test:hero", EntityKind.Player, new(100, 4.5f)), new(2.5f, 3.5f));
        string props = new JsonObject { ["entity"] = "test:mob", ["count"] = count, ["maxAlive"] = maxAlive, ["interval"] = interval, ["activation"] = activation, ["activationRadius"] = 4, ["radius"] = .8, ["tag"] = "wave", ["delay"] = .1 }.ToJsonString();
        using var document = JsonDocument.Parse(props);
        builder.Document.AddEntity(new(new EntityDef("test:spawner", EntityKind.SpawnMarker, new(1, 0), 0), new(10.5f, 10.5f), "spawner", Vec2.North, Array.Empty<string>(), document.RootElement.Clone()));
        builder.Document.Content = Content;
        builder.Document.Source = JsonNode.Parse("""{"objectives":[{"id":"clear","text":"Clear","type":"kill_tag","tag":"wave","minSpawned":1,"initialState":"active"}],"triggers":[{"id":"activate","event":{"type":"timer","seconds":0.1},"actions":[{"type":"set_state","entity":"spawner","state":"active"}]}]}""")!.AsObject();
        if (activation != "trigger") { builder.Document.Source["triggers"] = new JsonArray(); }
        return new(builder.Document, 42, record: record, difficulty: difficulty);
    }
    private static void Idle(GameSession session, int ticks)
    { for (int i = 0; i < ticks; i++) { session.Step(default(PlayerInput)); session.DrainEvents(); } }
    private static void KillReadyAndTick(GameSession session)
    {
        for (int i = 0; i < session.World.Entities.Count; i++)
        { Entity entity = session.World.Entities[i]; if (entity.Kind == EntityKind.Enemy && entity.Alive && entity.Health.InvulnTicks == 0) { session.QueueDamage(1, entity.Id, 1000, 1, 0, default); } }
        session.Step(default(PlayerInput)); session.DrainEvents();
    }

    [Theory]
    [InlineData("immediate")]
    [InlineData("proximity")]
    [InlineData("sight")]
    [InlineData("trigger")]
    public void ActivationAndInitialDelayUseTheAuthoredStrategy(string activation)
    {
        GameSession session = Create(activation, count: 1); Entity marker = session.World.Entities[1];
        if (activation is "proximity" or "sight")
        {
            Idle(session, 20); Assert.Equal(0, session.World.LiveMonsterCount); Assert.False(session.World.Mission.Spawner(marker.Id).Active);
            session.World.Players[0].Pos = new(8.5f, 10.5f);
            if (activation == "sight")
            { session.World.Grid.SetRuntimeCell(9, 10, Cell.Wall); Idle(session, 20); Assert.False(session.World.Mission.Spawner(marker.Id).Active); session.World.Grid.SetRuntimeCell(9, 10, Cell.Floor); }
            session.Step(default(PlayerInput));
        }
        else if (activation == "trigger") { Idle(session, 6); }
        Assert.True(session.World.Mission.Spawner(marker.Id).Active); int due = session.World.Mission.Spawner(marker.Id).NextTick;
        Assert.Equal(session.Tick + 6, due); Idle(session, 5); Assert.Equal(0, session.World.LiveMonsterCount); session.Step(default(PlayerInput));
        Assert.Equal(1, session.World.LiveMonsterCount); Assert.Equal(0, session.World.Mission.Spawner(marker.Id).Remaining);
        Entity enemy = session.World.Entities.Single(e => e.Kind == EntityKind.Enemy); Assert.Equal(EnemyMode.Emerging, enemy.Ai.Mode);
        Assert.True(session.World.CircleFits(enemy.Pos, enemy.Radius)); Assert.Contains(session.DrainEvents().ToArray(), e => e.Kind == SimEventKind.SpawnerFinished);
    }

    [Fact]
    public void OwnLiveCapIncludesDeferredSpawnsAndDifficultyCountIsAppliedOnce()
    {
        GameSession session = Create("immediate", difficulty: new(extraSpawnsMultiplier: 1.5f, maxMonsters: 1)); Entity marker = session.World.Entities[1];
        Idle(session, 100); Assert.Equal(1, session.World.LiveMonsterCount); Assert.Equal(1, session.World.PendingMonsterSpawns);
        Assert.Equal(7, session.World.Mission.Spawner(marker.Id).Remaining); Assert.Equal(ObjectiveState.Active, session.World.Mission.Objectives[0].State);
        for (int i = 0; i < 800; i++) { KillReadyAndTick(session); Assert.InRange(session.World.LiveMonsterCount + session.World.PendingMonsterSpawns, 0, 2); }
        Assert.Equal(9, session.World.Kills); Assert.Equal(0, session.World.Mission.Spawner(marker.Id).Remaining); Assert.Equal(ObjectiveState.Complete, session.World.Mission.Objectives[0].State);
    }

    [Fact]
    public void ScSpw01ShippedArenaWaitsForProximityAndFinishesExactlySixSpawns()
    {
        LevelDocument level = MissionAssets.Load("src/Dungeons.Core.Tests/TestLevels/test_spawner_arena/level.json"); var source = level.ToJsonObject(); source["objectives"]![0]!["minSpawned"] = 1;
        LevelLoadResult loaded = LevelLoader.Load(source.ToJsonString(), MissionAssets.Content); Assert.True(loaded.Success);
        var session = new GameSession(loaded.Document!, 1); Entity spawner = session.World.Entities.Single(e => e.Def.Id == "core:spawner");
        Idle(session, 60); Assert.Equal(0, session.World.LiveMonsterCount);
        for (int tick = 0; tick < 30; tick++) { session.Step(new PlayerInput(new(1, 0))); session.DrainEvents(); }
        Idle(session, 80); Assert.Equal(2, session.World.LiveMonsterCount); Assert.Equal(4, session.World.Mission.Spawner(spawner.Id).Remaining);
        Assert.Equal(ObjectiveState.Active, session.World.Mission.Objectives[0].State);
        int finished = 0;
        for (int tick = 0; tick < 600; tick++)
        {
            for (int i = 0; i < session.World.Entities.Count; i++)
            { Entity enemy = session.World.Entities[i]; if (enemy.Kind == EntityKind.Enemy && enemy.Alive && enemy.Health.InvulnTicks == 0) { session.QueueDamage(session.World.Players[0].Id, enemy.Id, 1000, 1, 0, default); } }
            session.Step(default(PlayerInput)); foreach (SimEvent ev in session.DrainEvents()) { if (ev.Kind == SimEventKind.SpawnerFinished) { finished++; } }
            Assert.InRange(session.World.LiveMonsterCount, 0, 2);
        }
        Assert.Equal(6, session.World.Kills); Assert.Equal(1, finished); Assert.Equal(ObjectiveState.Complete, session.World.Mission.Objectives[0].State);
    }

    [Fact]
    public void TriggerCreatedSpawnerInstancesKeepIndependentCapsAndAllocateNothing()
    {
        JsonObject source = MissionAssets.Load("src/Dungeons.Core.Tests/TestLevels/test_spawner_arena/level.json").ToJsonObject();
        JsonObject marker = source["entities"]!.AsArray().Single(e => e!["id"]!.GetValue<string>() == "wave")!.AsObject();
        marker["type"] = "core:spawn_marker"; marker.Remove("props");
        source["triggers"] = JsonNode.Parse("""[{"id":"start","event":{"type":"timer","seconds":0.1},"actions":[{"type":"spawn","at":"wave","count":2,"entity":{"type":"core:spawner","props":{"entity":"core:zombie","count":3,"interval":0,"maxAlive":1,"activation":"immediate","tag":"arena"}}}]}]""");
        source["objectives"]![0]!["minSpawned"] = 1;
        LevelLoadResult loaded = LevelLoader.Load(source.ToJsonString(), MissionAssets.Content); Assert.True(loaded.Success, string.Join('\n', loaded.Diagnostics));
        GameSession CreateRuntime() => new(loaded.Document!, 42, record: true);
        GameSession warm = CreateRuntime(); for (int i = 0; i < 600; i++) { KillReadyAndTick(warm); }
        GameSession session = CreateRuntime(); long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 7; i++) { KillReadyAndTick(session); }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Entity[] spawners = session.World.Entities.Where(e => e.Def.Id == "core:spawner").ToArray();
        Assert.Equal(2, spawners.Length); Assert.Equal(2, session.World.LiveMonsterCount);
        Assert.All(spawners, e => Assert.Equal(2, session.World.Mission.Spawner(e.Id).Remaining));
        Assert.True(ReplayVerifier.Verify(CreateRuntime(), session.Recorder!.Capture()).Success);
        for (int i = 0; i < 600; i++)
        {
            KillReadyAndTick(session);
            foreach (Entity spawner in spawners) { Assert.InRange(session.World.CountOwnedMonsters(spawner.Id), 0, 1); }
            if (session.World.Kills < 6) { Assert.Equal(ObjectiveState.Active, session.World.Mission.Objectives[0].State); }
        }
        Assert.Equal(6, session.World.Kills); Assert.Equal(ObjectiveState.Complete, session.World.Mission.Objectives[0].State);
        Assert.All(spawners, e => Assert.Equal(0, session.World.Mission.Spawner(e.Id).Remaining));
    }

    [Fact]
    public void SpawnRegistrationDeathCountersAndCompletionChecksAllocateNothing()
    {
        GameSession warm = Create("immediate", record: true); for (int tick = 0; tick < 600; tick++) { KillReadyAndTick(warm); }
        GameSession session = Create("immediate", record: true); long before = GC.GetAllocatedBytesForCurrentThread();
        for (int tick = 0; tick < 600; tick++) { KillReadyAndTick(session); }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before); Assert.Equal(6, session.World.Kills);
    }
}
