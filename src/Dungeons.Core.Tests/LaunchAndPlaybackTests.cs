using System.Globalization;
using System.Text.Json.Nodes;
using Dungeons.Core.Level;
using Dungeons.Core.Sim;
using Dungeons.Game;
using Dungeons.Game.Input;
using Xunit;

namespace Dungeons.Core.Tests;

public sealed class LaunchAndPlaybackTests
{
    [Fact]
    public void ApplicationArgumentsSupportBothFormsPathsAndInvariantUnsignedSeeds()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            LaunchOptions parsed = LaunchOptions.Parse(["--quality=Low", "--level", "a level/level.json", "--seed=18446744073709551615", "--touch-preview"]);
            Assert.Equal("a level/level.json", parsed.Level); Assert.Equal(ulong.MaxValue, parsed.Seed);
            Assert.Equal(0UL, LaunchOptions.Parse(["--level=core.level", "--seed", "0"]).Seed);
            LaunchOptions replay = LaunchOptions.Parse(["--replay", "Résumé Ω.dreplay", "--replay-quit"]); Assert.True(replay.ReplayQuit); Assert.Equal("Résumé Ω.dreplay", replay.Replay);
            Assert.Equal("level.json", LaunchOptions.Parse(["--headless-validate=level.json"]).Validate);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
    [Fact]
    public void MissingDuplicateOverflowAndConflictingArgumentsAreRejected()
    {
        string[][] invalid = [["--level"], ["--level="], ["--level", "--seed", "1"], ["--level=x", "--level=y"], ["--seed=-1", "--level=x"], ["--seed=1,000", "--level=x"], ["--seed=18446744073709551616", "--level=x"], ["--seed=1"], ["--replay-quit"], ["--replay=x", "--replay-quit=true"], ["--headless-validate=x", "--level=y"], ["--headless-validate=x", "--replay=y"], ["--headless-validate=x", "--screenshot-test=y"]];
        foreach (string[] args in invalid) { Assert.Throws<FormatException>(() => LaunchOptions.Parse(args)); }
    }
    [Fact]
    public void RuntimeCursorReplaysRleCommandsAndStopsAtTheRecordedBoundary()
    {
        LevelDocument level = Level(); ReplayDocument replay = Record(level, 120);
        var session = GameSession.Create(level, MissionAssets.Content, replay.Snapshot.Seed); var cursor = new ReplayPlayback(session, replay);
        Assert.True(ReplayVerifier.Verify(GameSession.Create(level, MissionAssets.Content, replay.Snapshot.Seed), replay).Success);
        for (int i = 0; i < 120; i++) { Assert.True(cursor.Step()); }
        Assert.True(cursor.Complete); Assert.False(cursor.Step()); Assert.Equal(120, session.Tick); Assert.Equal(replay.Snapshot.FinalHash, session.Hash()); Assert.Null(session.Recorder);
    }
    [Fact]
    public void CursorRejectsIdentityCheckpointAndFinalFingerprintFailures()
    {
        LevelDocument level = Level(); ReplayDocument replay = Record(level, 120);
        Assert.Throws<InvalidDataException>(() => new ReplayPlayback(GameSession.Create(level, MissionAssets.Content, 8), replay));
        Assert.Throws<InvalidDataException>(() => new ReplayPlayback(GameSession.Create(level, MissionAssets.Content, 7), new(replay.Snapshot, new string('0', 64), replay.ContentHash)));
        JsonObject json = JsonNode.Parse(ReplayJson.Write(replay))!.AsObject(); json.Remove("tickHashes"); json["checkpoints"]![0]!["hash"] = "0000000000000001";
        ReplayDocument badCheckpoint = ReplayJson.Read(json.ToJsonString()).Document!;
        var cursor = new ReplayPlayback(GameSession.Create(level, MissionAssets.Content, 7), badCheckpoint);
        for (int i = 0; i < 59; i++) { cursor.Step(); } Assert.Contains("tick=60", Assert.Throws<InvalidDataException>(() => cursor.Step()).Message, StringComparison.Ordinal);
        json = JsonNode.Parse(ReplayJson.Write(replay))!.AsObject(); json.Remove("tickHashes"); json["finalHash"] = "0000000000000001";
        ReplayLoadResult finalRead = ReplayJson.Read(json.ToJsonString()); Assert.True(finalRead.Success, finalRead.Error);
        var finalCursor = new ReplayPlayback(GameSession.Create(level, MissionAssets.Content, 7), finalRead.Document!);
        for (int i = 0; i < 119; i++) { finalCursor.Step(); } Assert.Contains("tick=120", Assert.Throws<InvalidDataException>(() => finalCursor.Step()).Message, StringComparison.Ordinal);
    }
    [Fact]
    public void ZeroTickReplayCompletesWithoutAdvancingOrChangingState()
    {
        LevelDocument level = Level(); ReplayDocument replay = Record(level, 0); var session = GameSession.Create(level, MissionAssets.Content, 7); ulong before = session.Hash();
        var cursor = new ReplayPlayback(session, replay); Assert.True(cursor.Complete); Assert.False(cursor.Step()); Assert.Equal(before, session.Hash()); Assert.Equal(0, session.Tick);
    }
    private static LevelDocument Level() => MissionAssets.Load("game/levels/mossy_hollow/level.json");
    private static ReplayDocument Record(LevelDocument level, int ticks)
    {
        var session = GameSession.Create(level, MissionAssets.Content, 7, record: true, replayCapacity: System.Math.Max(1, ticks));
        for (int i = 0; i < ticks; i++) { session.Step(i == 0 ? new PlayerInput(default, pressed: InputButtons.Potion) : default); }
        return new(session.Recorder!.Capture(), level.SourceHash, MissionAssets.Content.Hash);
    }
}
