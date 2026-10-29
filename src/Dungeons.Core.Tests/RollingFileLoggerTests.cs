using System.Text;
using System.Text.Json;
using Dungeons.Core.Content;
using Dungeons.Core.Sim;
using Dungeons.Core.Util;
using Dungeons.Game.Logging;
using Xunit;

namespace Dungeons.Core.Tests;

[Collection("Performance")]
public sealed class RollingFileLoggerTests
{
    private static string Folder() => Path.Combine(MissionAssets.Root, "artifacts/m8/log-tests", Guid.NewGuid().ToString("N"));
    private static JsonElement[] Read(string folder) => Directory.GetFiles(folder, "dungeons*.log").SelectMany(path => File.ReadAllLines(path, new UTF8Encoding(false, true)))
        .Select(line => { using JsonDocument json = JsonDocument.Parse(line); return json.RootElement.Clone(); }).ToArray();

    [Fact]
    public void QueuedUtf8RecordsRotateAtFiveBoundedFilesAndPreserveManualFiles()
    {
        string folder = Folder(); Directory.CreateDirectory(folder); string manual = Path.Combine(folder, "manual.log"); File.WriteAllText(manual, "keep");
        using var logger = new RollingFileLogger(folder, maximumBytes: 1024);
        for (int i = 0; i < 50; i++) { logger.Log(LogLevel.Info, "View", $"{i:D3} " + new string('x', 250)); Assert.True(logger.Flush(), $"Rotation iteration {i}; directory {folder}; {logger.LastError}"); }
        string[] files = Directory.GetFiles(folder, "dungeons*.log"); Assert.Equal(5, files.Length);
        Assert.All(files, path => Assert.InRange(new FileInfo(path).Length, 1, 1024));
        Assert.Equal("keep", File.ReadAllText(manual)); Assert.Contains(Read(folder), entry => entry.GetProperty("message").GetString()!.StartsWith("049", StringComparison.Ordinal));
    }

    [Fact]
    public void OverflowRetainsLatestEntriesAndRecordsTheDroppedCount()
    {
        string folder = Folder(); using var logger = new RollingFileLogger(folder, queueCapacity: 4);
        for (int i = 0; i < 10; i++) { logger.Log(LogLevel.Warning, "Ai", i.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
        Assert.False(Directory.Exists(folder)); Assert.Equal(4, logger.PendingCount); Assert.Equal(6, logger.DroppedEntries);
        Assert.True(logger.Flush()); JsonElement[] entries = Read(folder);
        Assert.Equal(new[] { "6", "7", "8", "9" }, entries.Select(entry => entry.GetProperty("message").GetString()));
        Assert.All(entries, entry => Assert.Equal(6, entry.GetProperty("dropped").GetInt64()));
    }

    [Fact]
    public void StorageFailureRetainsPendingRecordAndRecoversWithoutThrowing()
    {
        string folder = Folder(); Directory.CreateDirectory(Path.GetDirectoryName(folder)!); File.WriteAllText(folder, "blocked");
        using var logger = new RollingFileLogger(folder); logger.Log(LogLevel.Error, "Io", "first");
        Assert.False(logger.Flush()); Assert.NotNull(logger.LastError); Assert.Equal(1, logger.PendingCount);
        logger.Log(LogLevel.Info, "Io", "second"); File.Delete(folder);
        Assert.True(logger.Flush()); Assert.Null(logger.LastError); Assert.Equal(0, logger.PendingCount);
        Assert.Equal(new[] { "first", "second" }, Read(folder).Select(entry => entry.GetProperty("message").GetString()));
    }

    [Fact]
    public void ControlCharactersLongUnicodeAndMetadataCannotEscapeTheRecordOrSizeLimit()
    {
        string folder = Folder(); using var logger = new RollingFileLogger(folder, maximumBytes: 1024);
        logger.Write(LogLevel.Info, "invalid\ncategory", "line\nquote\" Zażółć 🙂", new("test:level", ulong.MaxValue, 42)); Assert.True(logger.Flush());
        JsonElement first = Assert.Single(Read(folder)); Assert.Equal("line\nquote\" Zażółć 🙂", first.GetProperty("message").GetString()); Assert.Equal("Io", first.GetProperty("category").GetString());
        logger.Write(LogLevel.Info, "Level", new string('ą', 20000) + "🙂", new(new string('\n', 4000), ulong.MaxValue, int.MinValue)); Assert.True(logger.Flush());
        Assert.All(Read(folder), entry => Assert.Equal(JsonValueKind.Object, entry.ValueKind));
        Assert.Contains(Read(folder), entry => entry.GetProperty("truncated").GetBoolean());
        Assert.All(Directory.GetFiles(folder, "dungeons*.log"), path => Assert.InRange(new FileInfo(path).Length, 1, 1024));
    }

    [Fact]
    public void ClippingAtUtf16BoundariesPreservesCompleteUnicodeScalars()
    {
        string folder = Folder(); using var logger = new RollingFileLogger(folder);
        logger.Write(LogLevel.Warning, "Io", new string('x', 4095) + "🙂", new(new string('a', 63) + "🙂", 1, 0)); Assert.True(logger.Flush());
        JsonElement entry = Assert.Single(Read(folder)); Assert.Equal(new string('x', 4095), entry.GetProperty("message").GetString());
        Assert.Equal(new string('a', 63), entry.GetProperty("levelId").GetString()); Assert.True(entry.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public void DisposeFlushesAndTheNextProcessAppendsExistingRecords()
    {
        string folder = Folder(); var first = new RollingFileLogger(folder); first.Log(LogLevel.Info, "Io", "old"); first.Dispose(); first.Dispose();
        first.Log(LogLevel.Info, "Io", "after-dispose");
        using (var second = new RollingFileLogger(folder)) { second.Log(LogLevel.Info, "Io", "new"); }
        Assert.Equal(new[] { "old", "new" }, Read(folder).Select(entry => entry.GetProperty("message").GetString()));
    }

    [Fact]
    public void ReopeningDiscardsOnlyAnInterruptedFinalRecordAndKeepsCompleteHistory()
    {
        string folder = Folder();
        using (var first = new RollingFileLogger(folder)) { first.Log(LogLevel.Info, "Io", "complete"); }
        string path = Path.Combine(folder, "dungeons.log"); File.AppendAllText(path, "{\"message\":\"interrupted", new UTF8Encoding(false));
        using var logger = new RollingFileLogger(folder); logger.Log(LogLevel.Warning, "Io", "recovered"); Assert.True(logger.Flush());
        Assert.Equal(new[] { "complete", "recovered" }, Read(folder).Select(entry => entry.GetProperty("message").GetString()));
        Assert.True(logger.RecoveredTailBytes > 0); Assert.Contains(Read(folder), entry => entry.GetProperty("recoveredTailBytes").GetInt64() > 0);
    }

    [Fact]
    public async Task ConcurrentEnqueueingKeepsCompleteJsonRecords()
    {
        string folder = Folder(); using var logger = new RollingFileLogger(folder, queueCapacity: 512);
        await Task.WhenAll(Enumerable.Range(0, 4).Select(worker => Task.Run(() => { for (int i = 0; i < 100; i++) { logger.Log(LogLevel.Info, "Io", worker + ":" + i); } })));
        Assert.True(logger.Flush(512)); Assert.Equal(400, Read(folder).Length); Assert.Equal(0, logger.DroppedEntries);
    }

    [Fact]
    public void CoreWarningsCaptureSessionSeedLevelAndTheActualTick()
    {
        string folder = Folder(); using var logger = new RollingFileLogger(folder);
        var sink = new SessionLog(logger, "test:logging", 99);
        var hero = new EntityDef("test:hero", EntityKind.Player, new(100, 4.5f));
        var builder = new ScenarioBuilder(10, 10, "test:logging").Entity(hero, new(5, 5));
        GameSession session = builder.Build(seed: 99, log: sink).Session; sink.Bind(session); session.DrainEvents(); session.Step(default(PlayerInput));
        ulong hash = session.Hash(); for (int i = 0; i <= GameConstants.MaxEvents; i++) { session.World.Emit(default); }
        Assert.Equal(hash, session.Hash()); Assert.True(logger.Flush()); JsonElement entry = Assert.Single(Read(folder));
        Assert.Equal("Sim", entry.GetProperty("category").GetString()); Assert.Equal("test:logging", entry.GetProperty("levelId").GetString());
        Assert.Equal(99UL, entry.GetProperty("seed").GetUInt64()); Assert.Equal(1, entry.GetProperty("tick").GetInt32());
    }

    [Fact, Trait("Category", "Performance")]
    public void EnqueueingStaticDiagnosticsAllocatesNothingEvenWhenTheRingOverflows()
    {
        string folder = Folder(); using var logger = new RollingFileLogger(folder, queueCapacity: 4); var sink = new SessionLog(logger, "test:logging", 99);
        for (int i = 0; i < 100; i++) { sink.Log(LogLevel.Warning, "Sim", "Static diagnostic."); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) { sink.Log(LogLevel.Warning, "Sim", "Static diagnostic."); }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated); Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void DebugLoggingWrapperHonorsTheBuildConfiguration()
    {
        string folder = Folder(); using var logger = new RollingFileLogger(folder); int evaluated = 0;
        string Message() { evaluated++; return "debug"; }
        logger.Debug("Sim", Message()); logger.Log(LogLevel.Debug, "Sim", "direct");
#if DEBUG
        Assert.Equal(1, evaluated); Assert.Equal(2, logger.PendingCount);
#else
        Assert.Equal(0, evaluated); Assert.Equal(0, logger.PendingCount);
#endif
    }
}
