using System.Diagnostics;
using System.Text.Json.Nodes;
using Dungeons.Core.Content;
using Dungeons.Core.Level;
using Dungeons.Core.Level.Commands;
using Dungeons.Core.Math;
using Dungeons.Core.Sim;
using Xunit;

namespace Dungeons.Core.Tests;

[Collection("Performance")]
public sealed class LevelReloadTests
{
    [Fact]
    public void ReloadRebuildsTerrainActorsAndStartupAtTheRetainedHeroOriginWithoutEditingSource()
    {
        LevelDocument level = LevelCommandTests.Create(); var session = new GameSession(level, 73);
        for (int i = 0; i < 20; i++) { session.Step(default(PlayerInput)); }
        World old = session.World; Entity hero = old.Players[0]; hero.Pos = hero.PrevPos = new(8.5f, 8.5f);
        hero.HealthData.Current = 23; hero.InventoryData.Arrows = 4;
        new SetHeight(8, 8, 1).Apply(level);
        new AddEntity(JsonNode.Parse("""{"id":"new_zombie","type":"core:zombie","x":9.5,"z":8.5}""")!.AsObject()).Apply(level);
        new AddTrigger(JsonNode.Parse("""{"id":"reload_start","event":{"type":"level_start"},"actions":[{"type":"message","text":"Reload ready"}]}""")!.AsObject()).Apply(level);
        string source = LevelSerializer.Write(level), identity = level.SourceHash; long revision = level.Revision;
        session.ReloadLevel(level);
        Assert.NotSame(old, session.World); Assert.Equal(1, session.ReloadRevision); Assert.Equal(0, session.Tick); Assert.Null(session.Recorder);
        Entity retained = session.World.Players[0]; Assert.Equal(hero.Pos, retained.Pos); Assert.Equal(retained.Pos, retained.PrevPos);
        Assert.Equal(1, retained.Y); Assert.Equal(retained.Y, retained.PrevY); Assert.Equal(retained.Pos, session.World.CheckpointPosition);
        Assert.Equal(retained.Health.Max, retained.Health.Current); Assert.Equal(retained.Def.Controls.ArrowsStart, retained.Inventory.Arrows);
        Assert.Equal("Reload ready", session.World.Mission.Message); Assert.Contains(session.World.Entities, entity => entity.LevelId == "new_zombie");
        Assert.Contains(session.DrainEvents().ToArray(), e => e.Kind == SimEventKind.EntitySpawned && e.EntityId == retained.Id && e.Position == retained.Pos && e.Y == retained.Y);
        Assert.Equal(0, old.Grid[8, 8].Height); Assert.Equal(23, hero.Health.Current); Assert.Equal(20, old.Tick);
        Assert.Equal(source, LevelSerializer.Write(level)); Assert.Equal(identity, level.SourceHash); Assert.Equal(revision, level.Revision);
        Assert.Equal(new GameSession(level, 73, startOverride: session.StartOverride).Hash(), session.Hash());
    }

    [Fact]
    public void NewWallsAndClosedGatesSelectTheNearestSafeOriginDeterministically()
    {
        LevelDocument level = LevelCommandTests.Create(); var session = new GameSession(level);
        session.World.Players[0].Pos = new(8.5f, 8.5f); new SetTile(8, 8, "core:stone_wall").Apply(level);
        session.ReloadLevel(level); Assert.Equal(new Vec2(8.5f, 7.5f), session.World.Players[0].Pos);
        Assert.Equal(session.World.Players[0].Pos, session.World.CheckpointPosition);
        session.World.Players[0].Pos = new(5.5f, 4.5f); new SetEntityProps("gate", new JsonObject { ["state"] = "closed" }).Apply(level);
        session.ReloadLevel(level); Assert.NotEqual(new Vec2(5.5f, 4.5f), session.World.Players[0].Pos);
        Assert.Equal(2, session.ReloadRevision); Assert.True(session.World.Grid[5, 4].Solid);
        Assert.Equal(new GameSession(level, startOverride: session.StartOverride).Hash(), session.Hash());
    }

    [Fact]
    public void InvalidDocumentsDifferentIdsAndFailedConstructionLeaveTheOldSessionIntact()
    {
        LevelDocument level = LevelCommandTests.Create(); var session = new GameSession(level); session.Step(default(PlayerInput));
        World old = session.World; ulong hash = session.Hash();
        Assert.Throws<ArgumentNullException>(() => session.ReloadLevel(null!));
        Assert.Throws<ArgumentException>(() => session.ReloadLevel(new(8, 8, "another")));
        new RemoveEntity("exit").Apply(level); Assert.True(level.HasErrors); Assert.Throws<ArgumentException>(() => session.ReloadLevel(level));
        var invalid = new ScenarioBuilder(2, 2, old.LevelId).Entity(new("test:hero", EntityKind.Player, new(100, 4)), new(.5f, .5f));
        invalid.Cell(0, 0, Cell.Wall); Assert.Throws<ArgumentException>(() => session.ReloadLevel(invalid.Document));
        Assert.Same(old, session.World); Assert.Equal(hash, session.Hash()); Assert.Equal(0, session.ReloadRevision); Assert.Equal(1, session.Tick);
    }

    [Fact]
    public void RecordedSessionsRejectReloadWithoutCorruptingTheirReplay()
    {
        LevelDocument level = LevelCommandTests.Create(); var session = new GameSession(level, 42, record: true, replayCapacity: 60);
        for (int i = 0; i < 10; i++) { session.Step(default(PlayerInput)); }
        World world = session.World; ulong hash = session.Hash();
        Assert.Throws<InvalidOperationException>(() => session.ReloadLevel(level));
        Assert.Same(world, session.World); Assert.Equal(hash, session.Hash()); Assert.Equal(0, session.ReloadRevision);
        Assert.True(ReplayVerifier.Verify(new GameSession(level, 42), session.Recorder!.Capture()).Success);
    }

    [Fact]
    public void PausedReloadStartsANewEpochWithTheSameSeedDifficultyAndNewPrototypes()
    {
        var builder = new ScenarioBuilder(8, 8).Entity(new("test:hero", EntityKind.Player, new(100, 4)), new(1.5f, 1.5f));
        var session = new GameSession(builder.Document, 987); session.World.Players[0].Pos = new(4.5f, 4.5f); session.Pause();
        var changed = new ScenarioBuilder(8, 8).Entity(new("test:hero", EntityKind.Player, new(250, 5)), new(1.5f, 1.5f));
        var difficulty = session.World.Difficulty; session.ReloadLevel(changed.Document);
        Assert.Equal(SessionState.Paused, session.State); Assert.Equal(987UL, session.World.Seed); Assert.Same(difficulty, session.World.Difficulty);
        Assert.Equal(250, session.World.Players[0].Health.Max); Assert.Equal(new Vec2(4.5f, 4.5f), session.World.Players[0].Pos);
        session.Step(default(PlayerInput)); Assert.Equal(0, session.Tick); session.Resume(); session.Step(default(PlayerInput)); Assert.Equal(1, session.Tick);
    }

    [Fact]
    public void DeadHeroAndOtherReservedSlotsUseTheNewAuthoredOrigins()
    {
        var hero = new EntityDef("test:hero", EntityKind.Player, new(100, 4));
        var before = new ScenarioBuilder(8, 8).Entity(hero, new(1.5f, 1.5f)).Entity(hero, new(6.5f, 6.5f));
        var after = new ScenarioBuilder(8, 8).Entity(hero, new(2.5f, 2.5f)).Entity(hero, new(3.5f, 3.5f));
        var session = new GameSession(before.Document); session.World.Players[0].Pos = new(4.5f, 4.5f);
        session.ReloadLevel(after.Document); Assert.Equal(new Vec2(4.5f, 4.5f), session.World.Players[0].Pos); Assert.Equal(new Vec2(3.5f, 3.5f), session.World.Players[1].Pos);
        session.World.Players[0].Alive = false; session.ReloadLevel(after.Document);
        Assert.Null(session.StartOverride); Assert.True(session.World.Players[0].Alive); Assert.Equal(new Vec2(2.5f, 2.5f), session.World.Players[0].Pos);
        Assert.Equal(new GameSession(after.Document).Hash(), session.Hash());
    }

    [Fact, Trait("Category", "Performance")]
    public void MossyHollowReloadRemainsInsideThePrototypeBudget()
    {
        LevelDocument level = MissionAssets.Load("game/levels/mossy_hollow/level.json"); var session = new GameSession(level);
        session.ReloadLevel(level); session.ReloadLevel(level);
        for (int i = 0; i < 8; i++)
        {
            long start = Stopwatch.GetTimestamp(); session.ReloadLevel(level); double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            Console.WriteLine($"M9_RELOAD_CORE milliseconds={elapsed:F3}"); Assert.True(elapsed < 500, $"Reload took {elapsed:F3} ms.");
        }
    }
}
