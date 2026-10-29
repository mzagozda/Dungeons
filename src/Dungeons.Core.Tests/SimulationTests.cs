using Dungeons.Core.Content;
using Dungeons.Core.Level;
using Dungeons.Core.Math;
using Dungeons.Core.Sim;
using Dungeons.Core.Util;
using Xunit;

namespace Dungeons.Core.Tests;

[Collection("Performance")]
public sealed class SimulationTests
{
    private static readonly EntityDef Hero = new("test:hero", EntityKind.Player, new StatBlock(100, 4.5f));
    private static readonly EntityDef Enemy = new("test:enemy", EntityKind.Enemy, new StatBlock(20, 2));

    [Fact]
    public void ScMov01OneSecondEastPreservesPreviousTransform()
    {
        Scenario scenario = new ScenarioBuilder(10, 10).Entity(Hero, new(2.5f, 2.5f)).Build();
        scenario.Run(60, _ => new PlayerInput(new(1, 0)));
        Entity player = scenario.Session.World.Players[0];
        Assert.InRange(player.Pos.X, 6.99f, 7.01f); Assert.Equal(2.5f, player.Pos.Z);
        Assert.InRange(player.Pos.X - player.PrevPos.X, 0.07499f, 0.07501f);
        Assert.Equal(new Vec2(1, 0), player.Facing); Assert.Equal(60, scenario.Session.Tick);
    }

    [Fact]
    public void ScMov02WallStopsNormalVelocityAndPreservesTangent()
    {
        var builder = new ScenarioBuilder(20, 20).Entity(Hero, new(5.5f, 4.5f));
        for (int z = 0; z < 20; z++) { builder.Cell(7, z, Cell.Wall); }
        Scenario scenario = builder.Build(); scenario.Run(60, _ => new PlayerInput(new(1, 1)));
        Entity player = scenario.Session.World.Players[0];
        Assert.InRange(player.Pos.X, 6.6499f, 6.6501f); Assert.Equal(0, player.Vel.X);
        Assert.InRange(player.Pos.Z - 4.5f, 4.5f / MathF.Sqrt(2) - 0.001f, 4.5f / MathF.Sqrt(2) + 0.001f);
        Assert.InRange(player.Vel.Z, 3.1819f, 3.1821f);
    }

    [Fact]
    public void ScMov03StairsHeightIsMonotonicInBothDirections()
    {
        var builder = new ScenarioBuilder(10, 3).Entity(Hero, new(2.5f, 1.5f)).Cell(3, 1, new Cell(stairsDir: StairsDirection.East));
        for (int x = 4; x < 10; x++) { builder.Cell(x, 1, new Cell(height: 1)); }
        GameSession session = builder.Build().Session; Entity player = session.World.Players[0];
        float previous = 0;
        for (int tick = 0; tick < 40; tick++)
        {
            session.Step(new PlayerInput(new(1, 0))); Assert.True(player.Y >= previous); previous = player.Y;
        }
        Assert.Equal(1, player.Y); Assert.InRange(player.Pos.X, 5.49f, 5.51f);
        for (int tick = 0; tick < 40; tick++)
        {
            session.Step(new PlayerInput(new(-1, 0))); Assert.True(player.Y <= previous); previous = player.Y;
        }
        Assert.Equal(0, player.Y); Assert.InRange(player.Pos.X, 2.49f, 2.51f);
    }

    [Fact]
    public void PauseSlotsAimAndDocumentSnapshotsAreIndependent()
    {
        var builder = new ScenarioBuilder(12, 12).Entity(Hero, new(2.5f, 2.5f)).Entity(Hero, new(8.5f, 8.5f));
        GameSession session = builder.Build().Session;
        Assert.Equal(new Vec2(5.5f, 5.5f), session.World.CameraFocus);
        builder.Cell(2, 2, Cell.Wall); Assert.True(session.World.Grid.IsWalkable(2, 2));
        Assert.False(session.World.Entities is List<Entity>); Assert.False(session.World.Players is List<Entity>);
        Assert.Throws<ArgumentException>(() => session.Step(default(PlayerInput)));
        ulong initial = session.Hash(); session.Pause(); ulong paused = session.Hash();
        Assert.NotEqual(initial, paused); session.Step(ReadOnlySpan<PlayerInput>.Empty); Assert.Equal(0, session.Tick);
        session.Resume(); Assert.Equal(initial, session.Hash());
        session.Step(new PlayerInput[] { new(new(1, 0), new(0, -2), true), new(new(-1, 0)) });
        Assert.Equal(new Vec2(1, 0), session.World.Players[0].Facing); // §8.2: aim locks facing when an action starts.
        Assert.True(session.World.Players[0].Pos.X > 2.5f); Assert.True(session.World.Players[1].Pos.X < 8.5f);
        session.World.Players[0].Alive = false;
        Assert.Equal(session.World.Players[1].Pos, session.World.CameraFocus);
        session.World.Players[1].Alive = false; Assert.Equal(Vec2.Zero, session.World.CameraFocus);
        Assert.Equal(Enum.GetValues<SystemStage>(), GameSession.SystemOrder.ToArray());
    }

    [Fact]
    public void TimersStableRemovalAndFifoNavigationHaveFixedBudgets()
    {
        GameSession session = new ScenarioBuilder(20, 20).Entity(Hero, new(2.5f, 2.5f))
            .Entity(Enemy, new(5.5f, 5.5f)).Entity(Enemy, new(8.5f, 8.5f)).Build().Session;
        Entity expires = session.World.Entities[1]; expires.LifetimeTicks = 1;
        session.World.Players[0].HealthData.InvulnTicks = 2;
        Assert.Equal(new[] { 1, 2, 3 }, session.DrainEvents().ToArray().Select(e => e.EntityId));
        Assert.False(session.RequestPath(999, new(1, 1)));
        for (int i = 0; i < 25; i++) { Assert.True(session.RequestPath(1, new(i % 10 + 5, 2))); }
        session.Step(default(PlayerInput)); Assert.Equal(5, session.World.PendingPathRequests);
        Assert.Equal(new IntVec2(14, 2), session.World.Players[0].Path[^1]);
        Assert.Null(session.World.FindEntity(2)); Assert.Same(session.World.Entities[1], session.World.FindEntity(3));
        Assert.Equal(new[] { 1, 3 }, session.World.Entities.Select(e => e.Id)); Assert.Equal(4, session.World.NextEntityId);
        Assert.Equal(1, session.World.Players[0].Health.InvulnTicks);
        SimEvent removed = Assert.Single(session.DrainEvents().ToArray()); Assert.Equal(SimEventKind.EntityRemoved, removed.Kind); Assert.Equal(1, removed.Tick);
        session.Step(default(PlayerInput)); Assert.Equal(0, session.World.PendingPathRequests);
        Assert.Equal(new IntVec2(9, 2), session.World.Players[0].Path[^1]);
        Assert.Equal(0, session.World.Players[0].Health.InvulnTicks);
    }

    [Fact]
    public void SeparationUsesHardHeroContactsSoftEnemyContactsAndImmovableProps()
    {
        GameSession hard = new ScenarioBuilder(10, 10).Entity(Hero, new(4, 4)).Entity(Enemy, new(4.2f, 4)).Build().Session;
        hard.Step(default(PlayerInput)); Assert.InRange((hard.World.Entities[1].Pos - hard.World.Entities[0].Pos).Length, 0.6999f, 0.7001f);
        Assert.InRange(hard.World.Players[0].Pos.X, 3.7499f, 3.7501f);
        GameSession soft = new ScenarioBuilder(10, 10).Entity(Enemy, new(4, 4)).Entity(Enemy, new(4.2f, 4)).Build().Session;
        soft.Step(ReadOnlySpan<PlayerInput>.Empty);
        Assert.InRange((soft.World.Entities[1].Pos - soft.World.Entities[0].Pos).Length, 0.5749f, 0.5751f);
        var prop = new EntityDef("test:prop", EntityKind.Prop, new StatBlock(1, 0), solid: true);
        GameSession stat = new ScenarioBuilder(10, 10).Entity(Hero, new(0.5f, 4)).Entity(prop, new(0.6f, 4)).Build().Session;
        stat.Step(default(PlayerInput)); Assert.Equal(new Vec2(0.6f, 4), stat.World.Entities[1].Pos);
        Assert.InRange(stat.World.Players[0].Pos.X, 0.3499f, 0.3501f); // Terrain revalidation prevents a push outside the level.
        GameSession coincident = new ScenarioBuilder(10, 10).Entity(Hero, new(4, 4)).Entity(Enemy, new(4, 4)).Build().Session;
        coincident.Step(default(PlayerInput)); Assert.True(coincident.World.Entities.All(e => e.Pos.IsFinite));
        Assert.True(coincident.World.Entities[0].Pos.X < coincident.World.Entities[1].Pos.X);
    }

    [Fact]
    public void EventOverflowIsBoundedWarnsOnceAndDoesNotAffectHash()
    {
        var logger = new CountingLogger(); GameSession session = new(new LevelDocument(2, 2), log: logger);
        ulong initial = session.Hash();
        for (int i = 0; i < GameConstants.MaxEvents + 5; i++) { session.World.Emit(new SimEvent(0, SimEventKind.EntitySpawned, i, default)); }
        Assert.Equal(1, logger.Warnings); Assert.Equal(initial, session.Hash());
        ReadOnlySpan<SimEvent> events = session.DrainEvents(); Assert.Equal(GameConstants.MaxEvents, events.Length);
        Assert.Equal(GameConstants.MaxEvents - 1, events[^1].EntityId); Assert.Empty(session.DrainEvents().ToArray());
        session.World.Emit(default); Assert.Single(session.DrainEvents().ToArray()); Assert.Equal(initial, session.Hash());
    }

    [Fact]
    public void ScenarioDriverUsesProductionRoutesAndSanitizesInput()
    {
        Scenario scenario = ScenarioBuilder.FromAscii("........\n..#.....\n..#.....\n..#.....\n........")
            .Entity(Hero, new(1.5f, 2.5f)).Build();
        Assert.True(scenario.MoveToward(new(5.5f, 2.5f), 10));
        Assert.InRange((scenario.Session.World.Players[0].Pos - new Vec2(5.5f, 2.5f)).Length, 0, Hero.Stats.Speed * GameConstants.Dt);
        int tick = scenario.Session.Tick; scenario.Advance(0.1f); scenario.Press(InputButtons.Attack); Assert.Equal(tick + 7, scenario.Session.Tick);
        scenario.Session.Step(new PlayerInput(new(float.NaN, 1))); Assert.Equal(Vec2.Zero, scenario.Session.World.Players[0].Vel);
        Assert.Throws<ArgumentOutOfRangeException>(() => scenario.Run(-1, _ => default));
        Assert.Throws<ArgumentNullException>(() => scenario.Run(1, null!));
        Assert.Throws<ArgumentException>(() => ScenarioBuilder.FromAscii("..\n."));
        Assert.Throws<ArgumentException>(() => ScenarioBuilder.FromAscii("?"));
        Assert.False(new ScenarioBuilder(2, 2).Build().MoveToward(new(1, 1), 1));
    }

    [Fact]
    public void ReplayRleCheckpointsAndFirstDivergenceAreVerified()
    {
        var builder = new ScenarioBuilder(20, 20).Entity(Hero, new(5.5f, 8.5f));
        Scenario recorded = builder.Build(record: true, replayCapacity: 120);
        recorded.Run(120, tick => new PlayerInput(tick < 60 ? new(1, 0) : new(0, -1)));
        ReplaySnapshot replay = recorded.Session.Recorder!.Capture();
        Assert.False(replay.Truncated); Assert.Equal(120, replay.TickCount); Assert.Equal(2, replay.Frames.Length);
        Assert.Equal(60, replay.Frames[0].Duration); Assert.Equal(60, replay.Frames[1].StartTick);
        Assert.Equal(2, replay.Checkpoints.Length); Assert.Equal(60, replay.Checkpoints[0].Tick); Assert.Equal(120, replay.Checkpoints[1].Tick);
        Assert.Equal(recorded.Session.Hash(), replay.FinalHash);
        Assert.True(ReplayVerifier.Verify(builder.Build().Session, replay).Success);
        Assert.False(ReplayVerifier.Verify(builder.Build(seed: 2).Session, replay).Success);
        ReplayFrame[] frames = [new(new PlayerInput[] { new(new(1, 0)) }, 0, 30), new(new PlayerInput[] { new(new(-1, 0)) }, 30, 90)];
        var changed = new ReplaySnapshot(replay.Seed, replay.LevelId, replay.InitialHash, frames, replay.TickHashes.ToArray(), replay.Checkpoints.ToArray(), false);
        ReplayVerification result = ReplayVerifier.Verify(builder.Build().Session, changed);
        Assert.False(result.Success); Assert.Equal(31, result.DivergingTick); Assert.NotEqual(result.ExpectedHash, result.ActualHash);
        Scenario capped = builder.Build(record: true, replayCapacity: 5); capped.Run(6, _ => default);
        Assert.True(capped.Session.Recorder!.Overflowed); Assert.False(ReplayVerifier.Verify(builder.Build().Session, capped.Session.Recorder.Capture()).Success);
        ReplaySnapshot empty = builder.Build(record: true).Session.Recorder!.Capture();
        Assert.Equal(empty.InitialHash, empty.FinalHash); Assert.True(ReplayVerifier.Verify(builder.Build().Session, empty).Success);
        Assert.Throws<ArgumentOutOfRangeException>(() => replay.Frames[0][-1]);
    }

    [Fact]
    public void HashIncludesFutureAffectingStateButIgnoresPresentationHistory()
    {
        var builder = new ScenarioBuilder(8, 8).Entity(Hero, new(2.5f, 2.5f));
        GameSession a = builder.Build().Session, b = builder.Build().Session;
        Assert.Equal(a.Hash(), b.Hash()); a.DrainEvents(); a.World.Players[0].PrevPos = new(7, 7); Assert.Equal(a.Hash(), b.Hash());
        a.World.Players[0].InventoryData.Souls++; Assert.NotEqual(a.Hash(), b.Hash()); a.World.Players[0].InventoryData.Souls--;
        a.World.Randoms.Ai.NextU32(); Assert.NotEqual(a.Hash(), b.Hash()); b.World.Randoms.Ai.NextU32(); Assert.Equal(a.Hash(), b.Hash());
        a.World.Lives--; Assert.NotEqual(a.Hash(), b.Hash()); a.World.Lives++;
        a.RequestPath(1, new(6, 6)); Assert.NotEqual(a.Hash(), b.Hash());
        b.RequestPath(1, new(6, 6)); Assert.Equal(a.Hash(), b.Hash());
        for (int tick = 0; tick < 600; tick++)
        {
            PlayerInput input = new(new(tick % 120 < 60 ? 1 : -1, tick % 80 < 40 ? 0.5f : -0.5f));
            a.Step(input); b.Step(input); if (tick % 7 == 0) { a.DrainEvents(); } Assert.Equal(a.Hash(), b.Hash());
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void WarmedEmptyAndPopulatedRecordedTicksAllocateZeroBytes(bool populated)
    {
        var builder = new ScenarioBuilder(30, 30);
        if (populated)
        {
            builder.Entity(Hero, new(1.5f, 1.5f));
            for (int i = 0; i < 64; i++) { builder.Entity(Enemy, new(5.5f + i % 8 * 2, 5.5f + i / 8 * 2)); }
        }
        GameSession session = builder.Build(record: true, replayCapacity: 2000).Session;
        var inputs = new PlayerInput[populated ? 1 : 0];
        for (int i = 0; i < 1000; i++) { session.Step(inputs.AsSpan()); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 600; i++) { session.Step(inputs.AsSpan()); }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated); Assert.False(session.Recorder!.Overflowed);
    }

    private sealed class CountingLogger : ILogger
    {
        public int Warnings { get; private set; }
        public void Log(LogLevel level, string category, string message) { if (level == LogLevel.Warning) { Warnings++; } }
    }
}
