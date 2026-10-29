using System.Text.Json.Nodes;
using Dungeons.Core.Content;
using Dungeons.Core.Sim;
using Dungeons.Core.Math;
using Xunit;

namespace Dungeons.Core.Tests;

public sealed class ReplayJsonTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(4)]
    public void SerializedReplayPreservesFloatBitsSlotsIdentitiesAndCheckpoints(int players)
    {
        var builder = new ScenarioBuilder(30, 30);
        var hero = new EntityDef("test:hero", EntityKind.Player, new StatBlock(100, 4.5f));
        for (int p = 0; p < players; p++) { builder.Entity(hero, new(4.5f + p * 4, 15.5f)); }
        builder.Document.SourceHash = new string('a', 64); builder.Document.ContentHash = new string('b', 64);
        GameSession session = builder.Build(seed: ulong.MaxValue, record: true, replayCapacity: 600).Session;
        var rng = new Rng(2873); var inputs = new PlayerInput[players];
        for (int tick = 0; tick < 600; tick++)
        {
            for (int p = 0; p < players; p++) { inputs[p] = new PlayerInput(new(rng.Range(-1, 1), rng.Range(-1, 1)), new(1, 1), true, held: InputButtons.Artifact1); }
            session.Step(inputs.AsSpan());
        }
        var document = new ReplayDocument(session.Recorder!.Capture(), builder.Document.SourceHash, builder.Document.ContentHash);
        string json = ReplayJson.Write(document); ReplayLoadResult loaded = ReplayJson.Read(json);
        Assert.True(loaded.Success, loaded.Error); Assert.Equal(ulong.MaxValue, loaded.Document!.Snapshot.Seed);
        Assert.True(ReplayVerifier.Verify(builder.Build(seed: ulong.MaxValue).Session, loaded.Document).Success);
        Assert.Equal(json, ReplayJson.Write(loaded.Document));
        JsonObject minimal = JsonNode.Parse(json)!.AsObject(); minimal.Remove("tickHashes");
        foreach (JsonNode? frame in minimal["inputs"]!.AsArray()) { frame!.AsObject().Remove("duration"); }
        ReplayLoadResult checkpointOnly = ReplayJson.Read(minimal.ToJsonString());
        Assert.True(checkpointOnly.Success, checkpointOnly.Error); Assert.True(ReplayVerifier.Verify(builder.Build(seed: ulong.MaxValue).Session, checkpointOnly.Document!).Success);
        builder.Document.ContentHash = new string('c', 64);
        Assert.False(ReplayVerifier.Verify(builder.Build(seed: ulong.MaxValue).Session, loaded.Document).Success);
    }

    [Fact]
    public void MalformedReplayIsRejectedAndCorruptedCheckpointReportsItsTick()
    {
        var builder = new ScenarioBuilder(8, 8).Entity(new("test:hero", EntityKind.Player, new StatBlock(100, 4.5f)), new(2.5f, 2.5f));
        builder.Document.SourceHash = new string('a', 64); builder.Document.ContentHash = new string('b', 64);
        Scenario scenario = builder.Build(record: true, replayCapacity: 120); scenario.Run(120, _ => default);
        string valid = ReplayJson.Write(new(scenario.Session.Recorder!.Capture(), builder.Document.SourceHash, builder.Document.ContentHash));
        Action<JsonObject>[] mutations = [r => r["tickCount"] = -1, r => r["formatVersion"] = 9, r => r["levelHash"] = "bad", r => r["contentHash"] = null,
            r => r["inputs"]![0]!["t"] = 1, r => r["inputs"]![0]!["duration"] = 999, r => r["inputs"]![0]!["move"] = new JsonArray(5, 0),
            r => r["inputs"]![0]!["aim"] = new JsonArray(1), r => r["inputs"]![0]!["held"] = 65536, r => r["checkpoints"]!.AsArray().RemoveAt(0),
            r => r["tickHashes"]!.AsArray().RemoveAt(0), r => r["finalHash"] = "0000000000000000"];
        foreach (Action<JsonObject> mutate in mutations) { JsonObject root = JsonNode.Parse(valid)!.AsObject(); mutate(root); Assert.False(ReplayJson.Read(root.ToJsonString()).Success); }
        Assert.False(ReplayJson.Read("bad").Success); Assert.False(ReplayJson.Read("[]").Success); Assert.False(ReplayJson.Read("{}").Success);
        JsonObject changed = JsonNode.Parse(valid)!.AsObject(); changed.Remove("tickHashes"); changed["checkpoints"]![0]!["hash"] = "0000000000000000";
        ReplayLoadResult parsed = ReplayJson.Read(changed.ToJsonString()); Assert.True(parsed.Success);
        ReplayVerification result = ReplayVerifier.Verify(builder.Build().Session, parsed.Document!); Assert.False(result.Success); Assert.Equal(60, result.DivergingTick);
        Scenario capped = builder.Build(record: true, replayCapacity: 1); capped.Run(2, _ => default);
        Assert.Throws<ArgumentException>(() => ReplayJson.Write(new(capped.Session.Recorder!.Capture(), builder.Document.SourceHash, builder.Document.ContentHash)));
    }
}
