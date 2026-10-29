using System.IO.Compression;
using Dungeons.Core.Level;
using Dungeons.Core.Sim;
using Dungeons.Game.Input;
using Dungeons.Game.Persistence;
using Xunit;

namespace Dungeons.Core.Tests;

public sealed class MobileServicesTests
{
    private static string Destination() => Path.Combine(MissionAssets.Root, "artifacts/m7/import-tests", Guid.NewGuid().ToString("N"));
    private static MemoryStream Package()
    {
        byte[] level = File.ReadAllBytes(Path.Combine(MissionAssets.Root, "game/levels/mossy_hollow/level.json"));
        LevelPackage package = LevelPackage.Create([("level.json", (ReadOnlyMemory<byte>)level)]).Package!;
        var stream = new MemoryStream(); package.Write(stream); stream.Position = 0; return stream;
    }
    [Fact]
    public void ImportIsAtomicDeduplicatedAndDiscoverableWithoutOverwritingManualFiles()
    {
        string directory = Destination(); Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "manual.dlvl"), "preserve");
        using var source = Package(); byte[] bytes = source.ToArray(); LevelImportResult result = LevelImportStore.Import(source, directory, MissionAssets.Content);
        Assert.True(result.Success, string.Join(';', result.Diagnostics)); Assert.Equal(bytes, File.ReadAllBytes(result.Path!));
        Assert.StartsWith(Path.GetFullPath(directory) + Path.DirectorySeparatorChar, result.Path!);
        source.Position = 0; Assert.Equal(result.Path, LevelImportStore.Import(source, directory, MissionAssets.Content).Path);
        Assert.Equal(2, Directory.GetFiles(directory).Length); Assert.Equal("preserve", File.ReadAllText(Path.Combine(directory, "manual.dlvl")));
        Assert.True(Assert.Single(LevelCatalog.Scan([new(result.Path!, LevelSource.User, bytes, true)], MissionAssets.Content)).IsPlayable);
        Assert.Empty(Directory.GetFiles(directory, "*.tmp-*"));
    }
    [Fact]
    public void ImportRejectsTraversalAndInvalidLevelBeforeCreatingDestination()
    {
        foreach (bool unsafePath in new[] { false, true })
        {
            string directory = Destination(); using var stream = new MemoryStream();
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
            { using var writer = new StreamWriter(zip.CreateEntry(unsafePath ? "../level.json" : "level.json").Open()); writer.Write("{broken"); }
            stream.Position = 0; LevelImportResult result = LevelImportStore.Import(stream, directory, MissionAssets.Content);
            Assert.False(result.Success); Assert.Contains(result.Diagnostics, item => item.Severity == DiagnosticSeverity.Error); Assert.False(Directory.Exists(directory));
        }
    }
    [Fact]
    public void ImportPreservesChangedContentAtAnExistingDestination()
    {
        string directory = Destination(); using var stream = Package(); LevelImportResult first = LevelImportStore.Import(stream, directory, MissionAssets.Content);
        Assert.True(first.Success); File.WriteAllText(first.Path!, "changed by user"); stream.Position = 0;
        Assert.False(LevelImportStore.Import(stream, directory, MissionAssets.Content).Success); Assert.Equal("changed by user", File.ReadAllText(first.Path!));
    }
    [Fact]
    public void OversizedSourceIsRejectedBeforeReadingAndIoFailureBecomesADiagnostic()
    {
        using var source = new OversizedStream(); string directory = Destination();
        Assert.False(LevelImportStore.Import(source, directory, MissionAssets.Content).Success); Assert.False(Directory.Exists(directory));
        using var package = Package(); string parent = Destination(); Directory.CreateDirectory(parent); string file = Path.Combine(parent, "file"); File.WriteAllText(file, "occupied");
        Assert.False(LevelImportStore.Import(package, file, MissionAssets.Content).Success); Assert.Equal("occupied", File.ReadAllText(file));
    }
    [Fact]
    public void HapticsFilterHeroEventsAndUseOneLongestPulsePerRenderFrame()
    {
        SimEvent[] hits = [new(1, SimEventKind.DamageApplied, 2, default, amount: 12), new(2, SimEventKind.DamageApplied, 1, default, amount: 5)];
        Assert.Equal(30, HapticFeedback.Duration(hits, 1, true)); Assert.Equal(0, HapticFeedback.Duration(hits, 1, false)); Assert.Equal(0, HapticFeedback.Duration(hits, 3, true));
        SimEvent[] death = [hits[1], new(2, SimEventKind.PlayerDied, 1, default), hits[1]];
        Assert.Equal(60, HapticFeedback.Duration(death, 1, true)); Assert.Equal(0, HapticFeedback.Duration([new(1, SimEventKind.DamageApplied, 1, default, amount: 0)], 1, true));
        Assert.Equal(0, HapticFeedback.Duration(death, 0, true));
    }
    [Fact]
    public void HapticServiceReceivesNamedPatternsAndSuppressesReplayAndDisabledFeedback()
    {
        var service = new FakeHaptics();
        SimEvent hit = new(1, SimEventKind.DamageApplied, 1, default, amount: 5);
        HapticFeedback.Play(service, [hit], 1, enabled: true, replay: false);
        Assert.Equal(1, service.Plays); Assert.Equal(HapticPattern.HeroHit, service.Last);
        SimEvent death = new(2, SimEventKind.PlayerDied, 1, default);
        HapticFeedback.Play(service, [hit, death, hit], 1, enabled: true, replay: false);
        Assert.Equal(2, service.Plays); Assert.Equal(HapticPattern.HeroDeath, service.Last);
        HapticFeedback.Play(service, [death], 1, enabled: false, replay: false);
        HapticFeedback.Play(service, [death], 1, enabled: true, replay: true);
        HapticFeedback.Play(service, [death], 2, enabled: true, replay: false);
        Assert.Equal(2, service.Plays);
    }
    private sealed class FakeHaptics : IHaptics
    {
        public int Plays { get; private set; }
        public HapticPattern Last { get; private set; }
        public void Play(HapticPattern pattern) { Last = pattern; Plays++; }
        public void Stop() { }
    }
    private sealed class OversizedStream : Stream
    {
        public override bool CanRead => true; public override bool CanSeek => true; public override bool CanWrite => false;
        public override long Length => LevelPackage.MaxBytes + 1L; public override long Position { get; set; }
        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Oversized source must never be read.");
        public override void Flush() => throw new NotSupportedException(); public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
