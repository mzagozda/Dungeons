using Dungeons.Core.Content;
using Dungeons.Core.Math;
using Dungeons.Core.Sim;
using Xunit;

namespace Dungeons.Core.Tests;

public sealed class DifficultyTests
{
    [Fact]
    public void DifficultyAppliesToInitialAndDynamicEnemiesAndKeepsHeroesUnmodified()
    {
        var hero = new EntityDef("test:hero", EntityKind.Player, new(100, 4.5f)); var monster = new EntityDef("test:mob", EntityKind.Enemy, new(40, 2.6f));
        var builder = new ScenarioBuilder(20, 20).Entity(hero, new(3, 3)).Entity(monster, new(10, 10));
        var profile = new DifficultyProfile(1.5f, 2, 1.5f, 2); var session = new GameSession(builder.Document, difficulty: profile);
        Assert.Equal(100, session.World.Players[0].Health.Max); Assert.Equal(60, session.World.Entities[1].Health.Max); Assert.Equal(2, session.World.Entities[1].Stats.DamageMultiplier);
        Entity extra = session.World.SpawnEntity(monster, new(15, 15), Vec2.North)!; Assert.Equal(60, extra.Health.Current);
        Assert.True(session.World.RequestMonsterSpawn(monster, new(17, 17), Vec2.North)); Assert.Equal(1, session.World.PendingMonsterSpawns);
        Assert.Equal(6, profile.ScaleSpawnCount(4)); Assert.Equal(0, profile.ScaleSpawnCount(0));
        Assert.Throws<ArgumentException>(() => new DifficultyProfile(float.NaN)); Assert.Throws<ArgumentOutOfRangeException>(() => profile.ScaleSpawnCount(int.MaxValue));
        Assert.NotEqual(new GameSession(builder.Document).Hash(), new GameSession(builder.Document, difficulty: profile).Hash());
    }

    [Fact]
    public void CustomDifficultyRoundTripsWithRecordedInputsAndLegacyDefaultsRemainValid()
    {
        var hero = new EntityDef("test:hero", EntityKind.Player, new(100, 4.5f));
        var builder = new ScenarioBuilder(20, 20).Entity(hero, new(3, 3)); builder.Document.SourceHash = new string('a', 64); builder.Document.ContentHash = new string('b', 64);
        var profile = new DifficultyProfile(1.5f, 2, 1.5f, 32);
        var session = new GameSession(builder.Document, 42, record: true, difficulty: profile);
        for (int i = 0; i < 120; i++) { session.Step(new PlayerInput(new(1, 0))); }
        var replay = new ReplayDocument(session.Recorder!.Capture(), builder.Document.SourceHash, builder.Document.ContentHash);
        string json = ReplayJson.Write(replay); ReplayLoadResult loaded = ReplayJson.Read(json); Assert.True(loaded.Success, loaded.Error);
        Assert.Equal(1.5f, loaded.Document!.Snapshot.Difficulty.HpMultiplier); Assert.Equal(32, loaded.Document.Snapshot.Difficulty.MaxMonsters);
        Assert.True(ReplayVerifier.Verify(new GameSession(builder.Document, 42, difficulty: loaded.Document.Snapshot.Difficulty), loaded.Document).Success);
        Assert.False(ReplayVerifier.Verify(new GameSession(builder.Document, 42), loaded.Document).Success);
        Assert.Equal(json, ReplayJson.Write(loaded.Document));
        Assert.False(ReplayJson.Read(json.Replace("\"maxMonsters\": 32", "\"maxMonsters\": 0", StringComparison.Ordinal)).Success);
    }

    [Fact]
    public void ExplicitFactoryBindsRegistryWithoutMutatingTheEditableDocument()
    {
        ContentRegistry content = ContentRegistry.Load(Array.Empty<(string, string)>());
        var level = new ScenarioBuilder(10, 10).Document; GameSession session = GameSession.Create(level, content, 1);
        Assert.Equal(content.Hash, session.World.ContentHash); Assert.Null(level.Content); Assert.Same(DifficultyProfile.Default, session.World.Difficulty);
    }
}
