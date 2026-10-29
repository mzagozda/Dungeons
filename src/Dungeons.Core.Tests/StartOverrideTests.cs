using System.Text.Json.Nodes;
using Dungeons.Core.Content;
using Dungeons.Core.Level;
using Dungeons.Core.Level.Commands;
using Dungeons.Core.Math;
using Dungeons.Core.Sim;
using Xunit;

namespace Dungeons.Core.Tests;

[Collection("Performance")]
public sealed class StartOverrideTests
{
    [Fact]
    public void OverrideSetsTheInitialHeroHeightCameraEventsAndRespawnAnchorWithoutEditingTheDocument()
    {
        LevelDocument document = LevelCommandTests.Create(); string source = document.ToJson(), hash = document.SourceHash; long revision = document.Revision;
        var origin = new StartOverride(new(4.5f, 3.5f)); var session = GameSession.Create(document, MissionAssets.Content, 42, startOverride: origin);
        Entity hero = session.World.Players[0]; Assert.Same(origin, session.StartOverride); Assert.Equal(origin.Pos, hero.Pos); Assert.Equal(origin.Pos, hero.PrevPos);
        Assert.Equal(1, hero.Y); Assert.Equal(1, hero.PrevY); Assert.Equal(origin.Pos, session.World.CameraFocus); Assert.Equal(origin.Pos, session.World.CheckpointPosition); Assert.Equal(0, session.World.CheckpointId);
        Assert.Contains(session.DrainEvents().ToArray(), e => e.Kind == SimEventKind.EntitySpawned && e.EntityId == hero.Id && e.Position == origin.Pos && e.Y == hero.Y && e.Text == hero.Def.Id);
        Respawn(session); Assert.Equal(origin.Pos, hero.Pos); Assert.Equal(1, hero.Y);
        Assert.Equal(source, document.ToJson()); Assert.Equal(hash, document.SourceHash); Assert.Equal(revision, document.Revision);
        Assert.Equal(new Vec2(1.5f, 10.5f), document.Entities.Single(entity => entity.Definition.Id == "core:player_spawn").Position);
        Assert.Equal(new GameSession(document, 42).Hash(), new GameSession(document, 42, startOverride: null).Hash());
    }

    [Fact]
    public void ExplicitCheckpointIsActiveBeforeStartupLogicAndControlsLaterRespawn()
    {
        JsonObject root = LevelCommandTests.Create().ToJsonObject(); root["triggers"]!.AsArray().Add(JsonNode.Parse("""{"id":"checkpoint_start","event":{"type":"checkpoint","id":"cp"},"actions":[{"type":"message","text":"Override checkpoint"}]}"""));
        LevelDocument document = LevelLoader.Load(root.ToJsonString(), MissionAssets.Content).Document!;
        var session = GameSession.Create(document, MissionAssets.Content, 1, startOverride: new(new(8.5f, 8.5f), "cp"));
        Entity checkpoint = session.World.Entities.Single(entity => entity.LevelId == "cp");
        Assert.True(checkpoint.WorldObject.Activated); Assert.Equal(checkpoint.Id, session.World.CheckpointId); Assert.Equal(checkpoint.Pos, session.World.CheckpointPosition);
        Assert.Equal("Override checkpoint", session.World.Mission.Message); Assert.Equal(0, session.Tick);
        Assert.Contains(session.DrainEvents().ToArray(), e => e.Kind == SimEventKind.CheckpointActivated && e.EntityId == checkpoint.Id && e.Tick == 0);
        Respawn(session); Assert.Equal(checkpoint.Pos, session.World.Players[0].Pos); Assert.Equal(checkpoint.Facing, session.World.Players[0].Facing);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("cp")]
    public void RecordedOverrideSurvivesJsonAndEveryTickPlaybackWithTheDifficultyProfile(string? checkpoint)
    {
        LevelDocument level = LevelCommandTests.Create(); var origin = new StartOverride(new(8.5f, 8.5f), checkpoint); var difficulty = new DifficultyProfile(1.3f, 1.2f, 1, 64);
        var recorded = GameSession.Create(level, MissionAssets.Content, 19, difficulty, record: true, replayCapacity: 120, startOverride: origin);
        for (int i = 0; i < 120; i++) { recorded.Step(new PlayerInput(new(-.1f, 0))); }
        string json = ReplayJson.Write(new(recorded.Recorder!.Capture(), level.SourceHash, MissionAssets.Content.Hash)); ReplayLoadResult loaded = ReplayJson.Read(json);
        Assert.True(loaded.Success, loaded.Error); ReplaySnapshot snapshot = loaded.Document!.Snapshot;
        Assert.Equal(origin, snapshot.StartOverride); Assert.Equal(json, ReplayJson.Write(loaded.Document));
        var playback = GameSession.Create(level, MissionAssets.Content, snapshot.Seed, snapshot.Difficulty, startOverride: snapshot.StartOverride);
        Assert.True(ReplayVerifier.Verify(playback, loaded.Document).Success);
        Assert.False(ReplayVerifier.Verify(GameSession.Create(level, MissionAssets.Content, snapshot.Seed, snapshot.Difficulty), loaded.Document).Success);
        JsonObject tampered = JsonNode.Parse(json)!.AsObject(); tampered["startOverride"]!["pos"] = new JsonArray(7.5, 8.5);
        ReplayDocument changed = ReplayJson.Read(tampered.ToJsonString()).Document!;
        Assert.False(ReplayVerifier.Verify(GameSession.Create(level, MissionAssets.Content, snapshot.Seed, snapshot.Difficulty, startOverride: changed.Snapshot.StartOverride), changed).Success);
    }

    [Fact]
    public void OriginMetadataIsVerifiedEvenWhenTheEffectiveWorldWouldBeIdentical()
    {
        LevelDocument level = LevelCommandTests.Create(); var origin = new StartOverride(new(1.5f, 10.5f));
        var recorded = new GameSession(level, record: true, replayCapacity: 1, startOverride: origin); ReplaySnapshot snapshot = recorded.Recorder!.Capture(); var ordinary = new GameSession(level);
        Assert.Equal(ordinary.Hash(), recorded.Hash()); Assert.False(ReplayVerifier.Verify(ordinary, snapshot).Success);
        Assert.True(ReplayVerifier.Verify(new GameSession(level, startOverride: new(origin.Pos)), snapshot).Success);
        Assert.False(origin.Equals(null)); Assert.False(origin.Equals("other")); Assert.Equal(origin.GetHashCode(), new StartOverride(origin.Pos).GetHashCode());
        Assert.False(origin.Equals(new StartOverride(origin.Pos, "cp")));
    }

    [Fact]
    public void LegacyReplaysOmitTheOptionalFieldAndMalformedOriginsAreRejected()
    {
        LevelDocument level = LevelCommandTests.Create(); var session = new GameSession(level, record: true, replayCapacity: 1);
        string legacy = ReplayJson.Write(new(session.Recorder!.Capture(), level.SourceHash, MissionAssets.Content.Hash)); Assert.DoesNotContain("startOverride", legacy);
        Assert.Null(ReplayJson.Read(legacy).Document!.Snapshot.StartOverride);
        string[] invalid = ["null", "false", "{}", "{\"pos\":[1]}", "{\"pos\":[1,2,3]}", "{\"pos\":[-1,2]}", "{\"pos\":[1e99,2]}", "{\"pos\":[256,2]}",
            "{\"pos\":[1,2],\"checkpoint\":null}", "{\"pos\":[1,2],\"checkpoint\":4}", "{\"pos\":[1,2],\"checkpoint\":\"\"}", "{\"pos\":[1,2],\"checkpoint\":\"bad/id\"}", "{\"pos\":[1,2],\"unknown\":true}"];
        foreach (string value in invalid) { JsonObject root = JsonNode.Parse(legacy)!.AsObject(); root["startOverride"] = JsonNode.Parse(value); Assert.False(ReplayJson.Read(root.ToJsonString()).Success, value); }
    }

    [Fact]
    public void InvalidOriginsClosedGatesUnsafeFootprintsAndWrongCheckpointKindsCannotStart()
    {
        foreach (Vec2 position in new[] { new Vec2(float.NaN, 2), new Vec2(1, float.PositiveInfinity), new Vec2(-1, 2), new Vec2(256, 1) }) { Assert.Throws<ArgumentOutOfRangeException>(() => new StartOverride(position)); }
        foreach (string checkpoint in new[] { "", "bad id", "bad/id", new string('a', 65) }) { Assert.Throws<ArgumentException>(() => new StartOverride(new(1, 1), checkpoint)); }
        LevelDocument level = LevelCommandTests.Create(); string source = level.ToJson();
        Assert.Throws<ArgumentException>(() => new GameSession(level, startOverride: new(new(12.5f, 1))));
        Assert.Throws<ArgumentException>(() => new GameSession(level, startOverride: new(new(.01f, .01f))));
        foreach (string checkpoint in new[] { "missing", "z" }) { Assert.Throws<ArgumentException>(() => new GameSession(level, startOverride: new(new(8.5f, 8.5f), checkpoint))); }
        new SetTile(8, 8, "core:stone_wall").Apply(level); Assert.Throws<ArgumentException>(() => new GameSession(level, startOverride: new(new(8.5f, 8.5f))));
        JsonObject root = JsonNode.Parse(source)!.AsObject(); root["entities"]![4]!["props"]!["state"] = "closed"; level = LevelLoader.Load(root.ToJsonString(), MissionAssets.Content).Document!;
        Assert.Throws<ArgumentException>(() => new GameSession(level, startOverride: new(new(5.5f, 4.5f))));
        Assert.Throws<ArgumentException>(() => new GameSession(new(8, 8), startOverride: new(new(2.5f, 2.5f))));
    }

    [Fact]
    public void CodeScenariosOverrideOnlyTheFirstReservedCoopSlotAndDoNotAllocateDuringRecording()
    {
        var hero = new EntityDef("test:hero", EntityKind.Player, new(100, 4)); var builder = new ScenarioBuilder(8, 8).Entity(hero, new(2.5f, 2.5f)).Entity(hero, new(5.5f, 5.5f));
        var origin = new StartOverride(new(3.5f, 3.5f)); var session = new GameSession(builder.Document, record: true, replayCapacity: 1200, startOverride: origin);
        Assert.Equal(origin.Pos, session.World.Players[0].Pos); Assert.Equal(new Vec2(5.5f, 5.5f), session.World.Players[1].Pos);
        var inputs = new PlayerInput[2]; session.DrainEvents(); for (int i = 0; i < 300; i++) { session.Step(inputs); session.DrainEvents(); }
        long before = GC.GetAllocatedBytesForCurrentThread(); for (int i = 0; i < 300; i++) { session.Step(inputs); session.DrainEvents(); } Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(ReplayVerifier.Verify(new GameSession(builder.Document, startOverride: origin), session.Recorder!.Capture()).Success);
    }
    private static void Respawn(GameSession session)
    { Entity hero = session.World.Players[0]; hero.Alive = false; hero.HealthData.Current = 0; hero.ActionData.DeathTicks = 0; session.Step(default(PlayerInput)); Assert.True(hero.Alive); Assert.Equal(hero.Health.Max, hero.Health.Current); }
}
