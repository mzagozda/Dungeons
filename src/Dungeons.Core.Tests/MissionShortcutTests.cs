using System.Text.Json.Nodes;
using Dungeons.Core.Level;
using Dungeons.Core.Math;
using Dungeons.Core.Sim;
using Dungeons.Core.Content;
using Dungeons.Core.Util;
using Xunit;

namespace Dungeons.Core.Tests;

[Collection("Performance")]
public sealed class MissionShortcutTests
{
    private static JsonObject ShortcutSource()
    {
        JsonObject source = MissionAssets.Load("game/levels/mossy_hollow/level.json").ToJsonObject();
        source["flags"] = new JsonObject { ["ready"] = false };
        source["objectives"] = JsonNode.Parse("""[{"id":"goal","type":"flag","text":"Open","flag":"ready","initialState":"active"}]""");
        source["completion"]!["requires"] = new JsonArray("goal");
        source["triggers"] = JsonNode.Parse("""[{"id":"ready","event":{"type":"level_start"},"actions":[{"type":"set_flag","flag":"ready"}]}]""");
        source["entities"]!.AsArray().Single(e => e!["id"]!.GetValue<string>() == "gate_main")!["props"]!["openWhen"] = "goal";
        return source;
    }

    [Fact]
    public void ScSpw02LoadTimeShortcutAndExplicitTriggerHaveIdenticalSimulationHashes()
    {
        JsonObject shortcut = ShortcutSource(); LevelLoadResult sugar = LevelLoader.Load(shortcut.ToJsonString(), MissionAssets.Content); Assert.True(sugar.Success);
        JsonObject explicitSource = shortcut.DeepClone().AsObject();
        explicitSource["entities"]!.AsArray().Single(e => e!["id"]!.GetValue<string>() == "gate_main")!["props"]!.AsObject().Remove("openWhen");
        explicitSource["triggers"]!.AsArray().Add(JsonNode.Parse("""{"actions":[{"state":"open","entity":"gate_main","type":"set_state"}],"event":{"state":"complete","id":"goal","type":"objective"},"id":"__gate_gate_main"}"""));
        LevelLoadResult explicitLevel = LevelLoader.Load(explicitSource.ToJsonString(), MissionAssets.Content); Assert.True(explicitLevel.Success);
        Assert.NotEqual(sugar.Document!.SourceHash, explicitLevel.Document!.SourceHash);
        Assert.Equal(2, sugar.Document.MissionSource["triggers"]!.AsArray().Count);
        JsonObject saved = sugar.Document.ToJsonObject(); Assert.Single(saved["triggers"]!.AsArray());
        Assert.Equal("goal", saved["entities"]!.AsArray().Single(e => e!["id"]!.GetValue<string>() == "gate_main")!["props"]!["openWhen"]!.GetValue<string>());
        var a = new GameSession(sugar.Document, 42, record: true); var b = new GameSession(explicitLevel.Document, 42);
        Assert.Equal(a.Hash(), b.Hash());
        for (int i = 0; i < 120; i++) { a.Step(default(PlayerInput)); b.Step(default(PlayerInput)); Assert.Equal(a.Hash(), b.Hash()); }
        Assert.Equal(GatePhase.Open, a.World.Entities.Single(e => e.LevelId == "gate_main").WorldObject.Gate);
        var envelope = new ReplayDocument(a.Recorder!.Capture(), sugar.Document.SourceHash, MissionAssets.Content.Hash);
        Assert.False(ReplayVerifier.Verify(new GameSession(explicitLevel.Document, 42), envelope).Success);
    }

    [Fact]
    public void ShortcutIdsAreValidatedAndCanBeReferencedByTriggerActions()
    {
        JsonObject source = ShortcutSource(); source["triggers"]![0]!["actions"]!.AsArray().Insert(0, JsonNode.Parse("""{"type":"disable_trigger","id":"__gate_gate_main"}"""));
        LevelLoadResult loaded = LevelLoader.Load(source.ToJsonString(), MissionAssets.Content); Assert.True(loaded.Success);
        var session = new GameSession(loaded.Document!); session.Step(default(PlayerInput));
        Assert.Equal(GatePhase.Closed, session.World.Entities.Single(e => e.LevelId == "gate_main").WorldObject.Gate);
        source["triggers"]!.AsArray().Add(JsonNode.Parse("""{"id":"__gate_gate_main","event":{"type":"level_start"},"actions":[{"type":"delay","seconds":0}]}"""));
        LevelLoadResult collision = LevelLoader.Load(source.ToJsonString(), MissionAssets.Content); Assert.False(collision.Success);
        Assert.Contains(collision.Diagnostics, d => d.Code == DiagnosticCode.LVL1020 && d.Message.Contains("collides", StringComparison.Ordinal));
    }

    [Fact]
    public void RuntimeCreatedGateShortcutUsesCompiledObjectiveAndSetStateWithoutAllocation()
    {
        JsonObject source = ShortcutSource(); JsonNode gate = source["entities"]!.AsArray().Single(e => e!["id"]!.GetValue<string>() == "gate_main")!;
        gate["props"]!.AsObject().Remove("openWhen");
        source["triggers"] = JsonNode.Parse("""[{"id":"new_gate","event":{"type":"timer","seconds":0.1},"actions":[{"type":"spawn","at":"sm_1","entity":{"type":"core:gate","props":{"width":1,"openWhen":"goal"}}}]},{"id":"ready","event":{"type":"timer","seconds":0.2},"actions":[{"type":"set_flag","flag":"ready"}]}]""");
        LevelLoadResult loaded = LevelLoader.Load(source.ToJsonString(), MissionAssets.Content); Assert.True(loaded.Success);
        GameSession Create() => new(loaded.Document!, 42, record: true);
        GameSession warm = Create(); for (int i = 0; i < 60; i++) { warm.Step(default(PlayerInput)); warm.DrainEvents(); }
        GameSession session = Create(); long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 6; i++) { session.Step(default(PlayerInput)); session.DrainEvents(); }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Entity spawned = Assert.Single(session.World.Entities, e => e.Kind == EntityKind.Gate && e.SpawnTick == 6);
        Assert.Equal(GatePhase.Closed, spawned.WorldObject.Gate);
        for (int i = 0; i < 7; i++) { session.Step(default(PlayerInput)); session.DrainEvents(); }
        Assert.Equal(GatePhase.Opening, spawned.WorldObject.Gate); Assert.True(ReplayVerifier.Verify(Create(), session.Recorder!.Capture()).Success);
        source["triggers"]![0]!["actions"]![0]!["entity"]!["props"]!["openWhen"] = "missing";
        LevelLoadResult bad = LevelLoader.Load(source.ToJsonString(), MissionAssets.Content); Assert.False(bad.Success); Assert.Contains(bad.Diagnostics, d => d.Code == DiagnosticCode.LVL1025);
    }

    [Fact]
    public void TerminalObjectiveStatesCannotBeReactivatedOrHidden()
    {
        var builder = new ScenarioBuilder(8, 8).Entity(new EntityDef("test:hero", EntityKind.Player, new(100, 0)), new(2.5f, 2.5f));
        builder.Document.Source = JsonNode.Parse("""{"objectives":[{"id":"failed","type":"survive","seconds":1,"text":"Hold","initialState":"active"},{"id":"active","type":"flag","text":"Stay","initialState":"active"}],"triggers":[{"id":"try","event":{"type":"level_start"},"actions":[{"type":"objective","id":"failed","state":"failed"},{"type":"objective","id":"failed","state":"active"},{"type":"objective","id":"active","state":"hidden"}]}]}""")!.AsObject();
        var log = new Warnings(); var session = new GameSession(builder.Document, log: log);
        Assert.Equal(ObjectiveState.Failed, session.World.Mission.Objectives[0].State); Assert.Equal(ObjectiveState.Active, session.World.Mission.Objectives[1].State);
        Assert.Equal(2, log.Count);
    }

    [Fact]
    public void SimultaneousMonsterAreaEntriesAreCoalescedToOneFirePerTick()
    {
        var builder = new ScenarioBuilder(8, 8).Entity(new EntityDef("test:hero", EntityKind.Player, new(100, 0)), new(2.5f, 2.5f));
        var monster = new EntityDef("test:mob", EntityKind.Enemy, new(100, 0)); builder.Entity(monster, new(4.5f, 4.5f)).Entity(monster, new(5.5f, 4.5f));
        builder.Document.Source = JsonNode.Parse("""{"triggers":[{"id":"entry","once":false,"event":{"type":"enter","who":"monster","area":{"x":4,"z":4,"w":2,"h":2}},"actions":[{"type":"give","item":"emerald","amount":1}]}]}""")!.AsObject();
        var session = new GameSession(builder.Document); Assert.Equal(1, session.World.Players[0].Inventory.Emeralds);
        session.Step(default(PlayerInput)); Assert.Equal(1, session.World.Players[0].Inventory.Emeralds);
    }

    [Fact]
    public void InitiallyClosedGateOverlappingPlayerIsRejectedByLoaderAndScenarioBoundary()
    {
        JsonObject source = ShortcutSource(); JsonNode gate = source["entities"]!.AsArray().Single(e => e!["id"]!.GetValue<string>() == "gate_main")!;
        gate["x"] = 4.5; gate["z"] = 36.5;
        LevelLoadResult blocked = LevelLoader.Load(source.ToJsonString(), MissionAssets.Content); Assert.False(blocked.Success);
        Assert.Contains(blocked.Diagnostics, d => d.Code == DiagnosticCode.LVL1012 && d.Message.Contains("initially closed", StringComparison.Ordinal));
        gate["props"]!["state"] = "open"; Assert.True(LevelLoader.Load(source.ToJsonString(), MissionAssets.Content).Success);
        var builder = new ScenarioBuilder(8, 8).Entity(new EntityDef("test:hero", EntityKind.Player, new(100, 0)), new(4.5f, 4.5f));
        builder.Entity(new EntityDef("test:gate", EntityKind.Gate, new(1, 0), 0, true), new(5, 4.5f));
        Assert.Throws<ArgumentException>(() => builder.Build());
    }

    [Fact]
    public void ClosedGateStillHashesOriginalTerrainNeededWhenItOpens()
    {
        GameSession Create(bool projectileBlock)
        {
            var builder = new ScenarioBuilder(8, 8).Entity(new EntityDef("test:hero", EntityKind.Player, new(100, 0)), new(2.5f, 2.5f));
            builder.Cell(4, 4, new(blocksProjectiles: projectileBlock)); builder.Entity(new EntityDef("test:gate", EntityKind.Gate, new(1, 0), 0, true), new(5, 4.5f));
            return builder.Build().Session;
        }
        GameSession a = Create(false), b = Create(true); Assert.Equal(a.World.Grid[4, 4].BlocksProjectiles, b.World.Grid[4, 4].BlocksProjectiles);
        Assert.NotEqual(a.Hash(), b.Hash()); a.World.SetGate(a.World.Entities[1], true); b.World.SetGate(b.World.Entities[1], true);
        Assert.False(a.World.Grid[4, 4].BlocksProjectiles); Assert.True(b.World.Grid[4, 4].BlocksProjectiles);
    }

    private sealed class Warnings : ILogger
    { internal int Count; public void Log(LogLevel level, string category, string message) { if (level == LogLevel.Warning) { Count++; } } }
}
