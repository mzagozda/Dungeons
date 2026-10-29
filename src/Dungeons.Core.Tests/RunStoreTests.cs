using System.Text.Json.Nodes;
using Dungeons.Core.Content;
using Dungeons.Core.Sim;
using Dungeons.Game.Persistence;
using Xunit;

namespace Dungeons.Core.Tests;

[Collection("Performance")]
public sealed class RunStoreTests
{
    private static string DirectoryForTest() => Path.Combine(MissionAssets.Root, "artifacts/m6/store-tests", Guid.NewGuid().ToString("N"));
    private static GameSession Session(ulong seed, bool failed = false, int emeralds = 7, float delay = .1f)
    {
        var builder = new ScenarioBuilder(8, 8, "test.run").Entity(new EntityDef("test:hero", EntityKind.Player, new(100, 4.5f)), new(2.5f, 2.5f));
        builder.Document.Content = MissionAssets.Content;
        builder.Document.Source = new JsonObject { ["triggers"] = new JsonArray(new JsonObject { ["id"] = "end", ["event"] = new JsonObject { ["type"] = "timer", ["seconds"] = delay }, ["actions"] = new JsonArray(new JsonObject { ["type"] = "give", ["item"] = "emerald", ["amount"] = emeralds }, new JsonObject { ["type"] = failed ? "fail_level" : "complete_level" }) }) };
        return new(builder.Document, seed, record: true);
    }
    private static void Finish(RunStore store, RunTicket ticket, bool failed = false, int emeralds = 7, float delay = .1f)
    {
        GameSession session = Session(ticket.Seed, failed, emeralds, delay);
        for (int tick = 0; tick < 120 && session.State == SessionState.Running; tick++) { session.Step(default(PlayerInput)); }
        Assert.NotEqual(SessionState.Running, session.State); store.Finish(ticket, session, session.World.SourceHash, session.World.ContentHash);
        store.Finish(ticket, session, session.World.SourceHash, session.World.ContentHash);
    }

    [Fact]
    public void AttemptsPersistAtLaunchSeedPrecedenceAndResultsAreAppliedExactlyOnce()
    {
        string directory = DirectoryForTest(); var store = new RunStore(directory); RunTicket first = store.Begin("test.run");
        Assert.Equal(0x9E3779B97F4A7C15UL, first.Seed); Assert.Equal(1, new RunStore(directory).Profile.Levels["test.run"].Attempts);
        Finish(store, first); Assert.Equal(7, store.Profile.TotalEmeralds); Assert.Equal(6, store.Profile.Levels["test.run"].BestTimeTicks);
        RunTicket retry = store.Begin("test.run", overrideSeed: first.Seed); Assert.Equal(first.Seed, retry.Seed); Finish(store, retry, failed: true, emeralds: 3);
        RunTicket fixedSeed = store.Begin("test.run", authoredSeed: 123); Assert.Equal(123UL, fixedSeed.Seed); Finish(store, fixedSeed, emeralds: 10, delay: .2f);
        RunTicket abandoned = store.Begin("test.run", authoredSeed: 123, overrideSeed: 77); Assert.Equal(77UL, abandoned.Seed);
        GameSession partial = Session(abandoned.Seed); store.Abandon(abandoned, partial, partial.World.SourceHash, partial.World.ContentHash); store.Abandon(abandoned, partial, partial.World.SourceHash, partial.World.ContentHash);
        PlayerProfile loaded = new RunStore(directory).Profile; Assert.Equal(4UL, loaded.RunCounter); Assert.Equal(20, loaded.TotalEmeralds); Assert.Equal(4, loaded.Levels["test.run"].Attempts);
        Assert.True(loaded.Levels["test.run"].Completed); Assert.Equal(6, loaded.Levels["test.run"].BestTimeTicks); Assert.Equal(10, loaded.Levels["test.run"].BestEmeralds); Assert.Equal(77UL, loaded.Levels["test.run"].LastSeed);
        Assert.Empty(Directory.GetFiles(directory, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public void CorruptProfileIsBackedUpAndRecreatedWithoutLosingTheOriginalBytes()
    {
        string directory = DirectoryForTest(); Directory.CreateDirectory(directory); const string corrupt = "{ broken profile"; File.WriteAllText(Path.Combine(directory, "profile.json"), corrupt);
        var warnings = new List<string>(); var store = new RunStore(directory, warnings.Add); Assert.Equal(0UL, store.Profile.RunCounter); Assert.NotEmpty(warnings);
        string backup = Assert.Single(Directory.GetFiles(directory, "profile.json.corrupt-*")); Assert.Equal(corrupt, File.ReadAllText(backup));
        Assert.Equal(1, JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "profile.json")))!["formatVersion"]!.GetValue<int>());
        Assert.Equal(0UL, new RunStore(directory).Profile.RunCounter);
    }

    [Fact]
    public void LastFiveAutomaticReplaysRemainVerifiableAndManualFilesArePreserved()
    {
        string directory = DirectoryForTest(); var store = new RunStore(directory);
        for (int i = 0; i < 7; i++) { Finish(store, store.Begin("test.run")); }
        string replayDirectory = Path.Combine(directory, "replays"); File.WriteAllText(Path.Combine(replayDirectory, "manual.dreplay"), "manual evidence");
        Finish(store, store.Begin("test.run")); string[] replays = Directory.GetFiles(replayDirectory, "run-*.dreplay"); Assert.Equal(5, replays.Length);
        Assert.Equal("manual evidence", File.ReadAllText(Path.Combine(replayDirectory, "manual.dreplay")));
        foreach (string path in replays)
        {
            ReplayLoadResult read = ReplayJson.Read(File.ReadAllText(path)); Assert.True(read.Success, read.Error);
            ReplaySnapshot replay = read.Document!.Snapshot; ReplayVerification result = ReplayVerifier.Verify(Session(replay.Seed), replay); Assert.True(result.Success, result.Message);
        }
    }

    [Fact]
    public void InstantTerminalProgramsProduceVerifiableZeroInputReplays()
    {
        GameSession session = Session(1, delay: 0); Assert.Equal(SessionState.Won, session.State); ReplaySnapshot replay = session.Recorder!.Capture(); Assert.Equal(0, replay.TickCount);
        Assert.True(ReplayVerifier.Verify(Session(1, delay: 0), replay).Success);
    }
}
