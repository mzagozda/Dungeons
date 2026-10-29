using System.Text.Json;
using System.Text.Json.Nodes;
using Dungeons.Core.Content;
using Dungeons.Core.Level;
using Dungeons.Core.Math;
using Dungeons.Core.Sim;
using Xunit;

namespace Dungeons.Core.Tests;

[Collection("Performance")]
public sealed class MissionProgramTests
{
    private static readonly EntityDef Hero = new("test:hero", EntityKind.Player, new(100, 4.5f));
    private static readonly ContentRegistry Content = ContentRegistry.Load(new[] { ("entities/mob.json", """{"id":"test:mob","kind":"Enemy","radius":0.3,"stats":{"hp":1,"speed":0}}""") });
    private static GameSession Create(string json, Action<ScenarioBuilder>? configure = null, DifficultyProfile? difficulty = null, bool record = false)
    {
        var builder = new ScenarioBuilder(20, 20).Entity(Hero, new(2.5f, 3.5f)); configure?.Invoke(builder);
        builder.Document.Source = JsonNode.Parse(json)!.AsObject(); builder.Document.Content = Content;
        return new(builder.Document, 42, record: record, difficulty: difficulty);
    }
    private static void Idle(GameSession session, int ticks)
    { for (int i = 0; i < ticks; i++) { session.Step(default(PlayerInput)); session.DrainEvents(); } }
    private static void Marker(ScenarioBuilder builder) => builder.Entity(new EntityDef("test:marker", EntityKind.SpawnMarker, new(1, 0), 0), new(10.5f, 10.5f), "marker");

    [Fact]
    public void StartupRunsAtZeroAndArrayOrderVersionsConditionsAndIllegalTransitionsAreDeterministic()
    {
        GameSession session = Create("""
        {"flags":{"ready":false},"objectives":[{"id":"goal","text":"Ready","type":"flag","flag":"ready"}],"triggers":[
          {"id":"start","event":{"type":"level_start"},"actions":[{"type":"set_flag","flag":"ready"},{"type":"message","text":"Welcome","seconds":0.2}]},
          {"id":"flagged","event":{"type":"flag","flag":"ready"},"conditions":[{"type":"lives_at_least","lives":3},{"type":"not","condition":{"type":"flag","flag":"ready","value":false}}],"actions":[{"type":"objective","id":"goal","state":"active"}]},
          {"id":"done","event":{"type":"objective","id":"goal","state":"complete"},"actions":[{"type":"give","item":"emerald","amount":3},{"type":"sound","id":"test:chime"},{"type":"music","id":"test:music"},{"type":"camera_shake","strength":0.4,"seconds":0.1},{"type":"objective","id":"goal","state":"active"}]}]}
        """);
        Assert.Equal(0, session.Tick); Assert.True(session.World.Mission.Flag("ready")); Assert.Equal(ObjectiveState.Complete, session.World.Mission.Objectives[0].State);
        Assert.Equal(0, session.World.Players[0].Inventory.Emeralds); Assert.Equal("Welcome", session.World.Mission.Message);
        session.DrainEvents(); session.Step(default(PlayerInput)); SimEvent[] events = session.DrainEvents().ToArray();
        Assert.Equal(3, session.World.Players[0].Inventory.Emeralds); Assert.Contains(events, e => e.Kind == SimEventKind.PlaySound && e.Text == "test:chime");
        Assert.Contains(events, e => e.Kind == SimEventKind.MusicChange); Assert.Contains(events, e => e.Kind == SimEventKind.CameraShake && e.DurationTicks == 6 && e.Value == .4f);
        Assert.Equal(ObjectiveState.Complete, session.World.Mission.Objectives[0].State); Idle(session, 12); Assert.Empty(session.World.Mission.Message); Assert.Equal(3, session.World.Players[0].Inventory.Emeralds);
    }

    [Fact]
    public void EnterAndExitUseInclusiveEdgesAfterMovementAndDoNotRepeatWhileInside()
    {
        GameSession session = Create("""
        {"triggers":[{"id":"enter","once":false,"event":{"type":"enter","area":{"x":3,"z":3.5,"w":2,"h":2}},"actions":[{"type":"give","item":"emerald","amount":1}]},
        {"id":"exit","event":{"type":"exit","area":{"x":3,"z":3.5,"w":2,"h":2}},"actions":[{"type":"give","item":"arrows","amount":2}]}]}
        """);
        for (int tick = 0; tick < 10; tick++) { session.Step(new PlayerInput(new(1, 0))); }
        Assert.Equal(1, session.World.Players[0].Inventory.Emeralds); Idle(session, 30); Assert.Equal(1, session.World.Players[0].Inventory.Emeralds);
        for (int tick = 0; tick < 30; tick++) { session.Step(new PlayerInput(new(1, 0))); }
        Assert.Equal(32, session.World.Players[0].Inventory.Arrows);
        for (int tick = 0; tick < 10; tick++) { session.Step(new PlayerInput(new(-1, 0))); }
        Assert.Equal(2, session.World.Players[0].Inventory.Emeralds);
    }

    [Fact]
    public void EnableResetsTimerAndDisableSuspendsAnInFlightDelayedCursor()
    {
        GameSession session = Create("""
        {"triggers":[{"id":"start","event":{"type":"level_start"},"actions":[{"type":"enable_trigger","id":"work"}]},
        {"id":"work","enabled":false,"once":false,"event":{"type":"timer","seconds":0.1},"actions":[{"type":"give","item":"emerald","amount":3},{"type":"delay","seconds":0.1},{"type":"give","item":"emerald","amount":5}]},
        {"id":"disable","event":{"type":"timer","seconds":0.15},"actions":[{"type":"disable_trigger","id":"work"}]},
        {"id":"enable","event":{"type":"timer","seconds":0.4},"actions":[{"type":"enable_trigger","id":"work"}]}]}
        """);
        Idle(session, 5); Assert.Equal(0, session.World.Players[0].Inventory.Emeralds); Idle(session, 1); Assert.Equal(3, session.World.Players[0].Inventory.Emeralds);
        session.Pause(); Idle(session, 20); Assert.Equal(6, session.Tick); session.Resume(); Idle(session, 18); Assert.Equal(24, session.Tick);
        Assert.Equal(3, session.World.Players[0].Inventory.Emeralds); Idle(session, 1); Assert.Equal(8, session.World.Players[0].Inventory.Emeralds);
        Idle(session, 5); Assert.Equal(11, session.World.Players[0].Inventory.Emeralds); Idle(session, 6); Assert.Equal(16, session.World.Players[0].Inventory.Emeralds);
    }

    [Fact]
    public void ScTrg01DeferredWaveCannotClearEarlyAndDeathsRemainCountedAfterCorpsesDisappear()
    {
        GameSession session = Create("""
        {"objectives":[{"id":"clear","text":"Clear","type":"kill_tag","tag":"wave","minSpawned":2,"initialState":"active"}],"triggers":[
        {"id":"start","event":{"type":"level_start"},"actions":[{"type":"spawn","at":"marker","count":2,"entity":{"type":"test:mob","tags":["wave"]}}]},
        {"id":"clear","once":false,"event":{"type":"entities_dead","tag":"wave","min":2},"actions":[{"type":"give","item":"emerald","amount":1},{"type":"set_state","entity":"gate","state":"open"}]}]}
        """, builder => { Marker(builder); builder.Entity(new EntityDef("test:gate", EntityKind.Gate, new(1, 0), 0, true), new(8, 5.5f), "gate"); }, new(maxMonsters: 1));
        Assert.Equal(1, session.World.LiveMonsterCount); Assert.Equal(1, session.World.PendingMonsterSpawns); Idle(session, 30);
        Entity first = session.World.Entities.Single(e => e.Kind == EntityKind.Enemy && e.Alive); session.QueueDamage(1, first.Id, 1000, 1, 0, default); session.Step(default(PlayerInput));
        Assert.Equal(ObjectiveState.Active, session.World.Mission.Objectives[0].State); Assert.Equal(0, session.World.Players[0].Inventory.Emeralds);
        Idle(session, 31); Entity second = session.World.Entities.Single(e => e.Kind == EntityKind.Enemy && e.Alive);
        session.QueueDamage(1, second.Id, 1000, 1, 0, default); session.Step(default(PlayerInput));
        Assert.Equal(ObjectiveState.Complete, session.World.Mission.Objectives[0].State); Assert.Equal(1, session.World.Players[0].Inventory.Emeralds); Assert.True(session.World.Grid.IsWalkable(7, 5));
        Idle(session, 100); Assert.DoesNotContain(session.World.Entities, e => e.Kind == EntityKind.Enemy);
        Assert.NotNull(session.World.SpawnEntity(Content.Entities["test:mob"], new(10.5f, 10.5f), Vec2.North, new[] { "wave" })); session.Step(default(PlayerInput));
        Entity third = session.World.Entities.Single(e => e.Kind == EntityKind.Enemy); session.QueueDamage(1, third.Id, 1000, 1, 0, default); session.Step(default(PlayerInput));
        Assert.Equal(2, session.World.Players[0].Inventory.Emeralds);
    }

    [Fact]
    public void SpawnGroupDifficultyScalesOnceAndKeepsAuthoredProperties()
    {
        GameSession session = Create("""{"triggers":[{"id":"start","event":{"type":"level_start"},"actions":[{"type":"spawn","at":"marker","count":2,"entity":{"type":"test:mob","tags":["scaled"]}}]}]}""", Marker, new(extraSpawnsMultiplier: 1.5f));
        Assert.Equal(3, session.World.LiveMonsterCount); Assert.All(session.World.Entities.Where(e => e.Kind == EntityKind.Enemy), e => Assert.Contains("scaled", e.Tags));
    }

    [Fact]
    public void SurviveRestartsFromRespawnAndCompletionFreezesExactResults()
    {
        GameSession session = Create("""
        {"objectives":[{"id":"hold","text":"Hold","type":"survive","seconds":5,"initialState":"active"}],"triggers":[],"completion":{"type":"exit","exit":"exit","requires":["hold"]}}
        """, builder => builder.Entity(new EntityDef("test:exit", EntityKind.Exit, new(1, 0), 0), new(2.5f, 3.5f), "exit"));
        Assert.True(session.World.Mission.ExitLocked); Idle(session, 179); Entity hero = session.World.Players[0]; hero.InventoryData.Emeralds = 7; hero.Experience = 12;
        session.QueueDamage(0, hero.Id, 1000, 1, 0, default); session.Step(default(PlayerInput)); Assert.Equal(180, session.Tick); Idle(session, 120);
        Assert.True(hero.Alive); Assert.Equal(300, session.World.Mission.Objectives[0].ActivatedTick); Idle(session, 299); Assert.Equal(SessionState.Running, session.State);
        session.Step(default(PlayerInput)); Assert.Equal(SessionState.Won, session.State); Assert.False(session.World.Mission.ExitLocked);
        MissionResults results = session.World.Mission.Results!.Value; Assert.Equal(600, results.Ticks); Assert.Equal(7, results.Emeralds); Assert.Equal(12, results.Xp); Assert.Equal(1, results.Deaths);
        Assert.Contains(session.DrainEvents().ToArray(), e => e.Kind == SimEventKind.MissionComplete && e.Results.Ticks == 600); Idle(session, 10); Assert.Equal(600, session.Tick);
    }

    [Fact]
    public void CompiledActionsFlagsCountersAndDelaysReplayWithoutAllocations()
    {
        const string json = """
        {"flags":{"done":false},"objectives":[{"id":"goal","text":"Goal","type":"flag","flag":"done","initialState":"active"}],"triggers":[
        {"id":"start","event":{"type":"level_start"},"actions":[{"type":"delay","seconds":0.1},{"type":"give","item":"emerald","amount":4},{"type":"set_flag","flag":"done"}]}]}
        """;
        GameSession warm = Create(json, record: true); Idle(warm, 600);
        GameSession recorded = Create(json, record: true); long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 600; i++) { recorded.Step(default(PlayerInput)); recorded.DrainEvents(); }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        GameSession replay = Create(json); ReplayVerification result = ReplayVerifier.Verify(replay, recorded.Recorder!.Capture()); Assert.True(result.Success);
        Assert.Equal(recorded.Hash(), replay.Hash()); Assert.Equal(4, replay.World.Players[0].Inventory.Emeralds);
    }

    [Fact]
    public void ObjectActionsConditionsImplicitGateAndForcedFailureShareOneProgram()
    {
        GameSession session = Create("""
        {"flags":{"ready":false},"entities":[{"id":"gate","props":{"openWhen":"goal"}}],
        "objectives":[{"id":"goal","text":"Ready","type":"flag","flag":"ready","initialState":"active"},{"id":"chest_goal","text":"Open","type":"interact","target":"chest","initialState":"active"}],
        "triggers":[{"id":"start","event":{"type":"level_start"},"actions":[{"type":"checkpoint","id":"checkpoint"},{"type":"set_state","entity":"chest","state":"open"},{"type":"remove_entity","id":"prop"}]},
        {"id":"checkpoint_event","event":{"type":"checkpoint","id":"checkpoint"},"actions":[{"type":"message","text":"Checkpoint","seconds":0.5}]},
        {"id":"interacted","event":{"type":"interact","id":"chest"},"actions":[{"type":"give","item":"emerald","amount":2}]},
        {"id":"heal","event":{"type":"timer","seconds":0.1},"conditions":[{"type":"entities_dead","tag":"removed"},{"type":"objective","id":"chest_goal","state":"complete"}],"actions":[{"type":"heal_player","percent":50},{"type":"give","item":"arrows","amount":2147483647},{"type":"set_flag","flag":"ready"}]},
        {"id":"fail","event":{"type":"timer","seconds":0.2},"actions":[{"type":"fail_level"},{"type":"give","item":"emerald","amount":1000}]}]}
        """, builder =>
        {
            builder.Entity(new EntityDef("test:gate", EntityKind.Gate, new(1, 0), 0, true), new(8, 5.5f), "gate");
            builder.Entity(new EntityDef("test:chest", EntityKind.Chest, new(1, 0), .35f, true), new(6.5f, 6.5f), "chest");
            builder.Entity(new EntityDef("test:checkpoint", EntityKind.Checkpoint, new(1, 0), 0), new(15.5f, 15.5f), "checkpoint");
            builder.Document.AddEntity(new(new EntityDef("test:prop", EntityKind.Prop, new(1, 0)), new(4.5f, 6.5f), "prop", Vec2.North, new[] { "removed" }, DefinitionData.Empty.Json));
        });
        Assert.Equal(1, session.World.CheckpointOrder); Assert.Equal("Checkpoint", session.World.Mission.Message); Assert.Equal(2, session.World.Players[0].Inventory.Emeralds);
        session.World.Players[0].HealthData.Current = 40; Idle(session, 6); Assert.Equal(90, session.World.Players[0].Health.Current); Assert.Equal(99, session.World.Players[0].Inventory.Arrows);
        Assert.Equal(ObjectiveState.Complete, session.World.Mission.Objectives[0].State); session.Step(default(PlayerInput)); Assert.True(session.World.Grid.IsWalkable(7, 5));
        Assert.DoesNotContain(session.World.Entities, entity => entity.LevelId == "prop"); Idle(session, 5); Assert.Equal(SessionState.Failed, session.State);
        Assert.Equal(2, session.World.Players[0].Inventory.Emeralds); Assert.Equal(12, session.World.Mission.Results!.Value.Ticks);
    }
}
