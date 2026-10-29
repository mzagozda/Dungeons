using System.IO.Compression;
using System.Text;
using Dungeons.Core.Content;
using Dungeons.Core.Level;
using Xunit;

namespace Dungeons.Core.Tests;

public sealed class PackageAndCatalogTests
{
    [Theory]
    [InlineData("../level.json")] [InlineData("/level.json")] [InlineData("assets/../../outside")]
    [InlineData("C:/level.json")] [InlineData("assets\\escape")] [InlineData("a//b")]
    [InlineData("NUL.txt")] [InlineData("assets/COM1.ogg")] [InlineData("assets/a. ")]
    public void UnsafePathsAreRejectedBeforeExtraction(string path)
    {
        Assert.False(LevelPackage.IsSafePath(path));
        PackageLoadResult created = LevelPackage.Create(new[] { (path, (ReadOnlyMemory<byte>)new byte[] { 1 }) });
        Assert.False(created.Success); Assert.Equal(DiagnosticCode.LVL0004, Assert.Single(created.Diagnostics).Code);
        using var zip = Zip((path, new byte[] { 1 })); Assert.False(LevelPackage.Read(zip).Success);
    }

    [Fact]
    public void PackageRoundtripIsDeterministicAndBounded()
    {
        byte[] json = Encoding.UTF8.GetBytes("{\"formatVersion\":1}"); byte[] model = [1, 2, 3];
        PackageLoadResult result = LevelPackage.Create(new[] { ("level.json", (ReadOnlyMemory<byte>)json), ("assets/model.bin", (ReadOnlyMemory<byte>)model) });
        Assert.True(result.Success); Assert.Contains(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Warning);
        model[0] = 9; Assert.Equal(1, result.Package!.Entries["assets/model.bin"].Span[0]);
        using var first = new MemoryStream(); result.Package.Write(first); Assert.True(first.CanWrite);
        first.Position = 0; PackageLoadResult read = LevelPackage.Read(first); Assert.True(read.Success); Assert.True(first.CanRead);
        Assert.Equal(json, read.Package!.Entries["level.json"].ToArray());
        using var second = new MemoryStream(); read.Package.Write(second); Assert.Equal(first.ToArray(), second.ToArray());
        Assert.False(LevelPackage.Create(new[] { ("assets/a", (ReadOnlyMemory<byte>)model) }).Success);
        Assert.False(LevelPackage.Create(new[] { ("level.json", (ReadOnlyMemory<byte>)json), ("LEVEL.JSON", (ReadOnlyMemory<byte>)json) }).Success);
        Assert.False(LevelPackage.Create(new[] { ("level.json", (ReadOnlyMemory<byte>)json), ("assets", (ReadOnlyMemory<byte>)json), ("assets/a", (ReadOnlyMemory<byte>)json) }).Success);
        var tooMany = Enumerable.Range(0, 513).Select(i => ("file" + i, ReadOnlyMemory<byte>.Empty));
        Assert.Contains(LevelPackage.Create(tooMany).Diagnostics, d => d.Code == DiagnosticCode.LVL0005);
        using var invalid = new MemoryStream(new byte[] { 1, 2, 3 }); Assert.False(LevelPackage.Read(invalid).Success);
        using var duplicated = Zip(("level.json", json), ("LEVEL.JSON", json)); Assert.False(LevelPackage.Read(duplicated).Success);
    }

    [Fact]
    public void ZipBombHeaderAndEntryCountAreRejectedBeforeInflation()
    {
        using var archive = new MemoryStream();
        using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, true))
        {
            using Stream entry = zip.CreateEntry("level.json").Open(); byte[] zeros = new byte[64 * 1024];
            for (int i = 0; i < 1024; i++) { entry.Write(zeros); } entry.WriteByte(0);
        }
        Assert.True(archive.Length < 1024 * 1024); archive.Position = 0;
        PackageLoadResult result = LevelPackage.Read(archive); Assert.False(result.Success); Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.LVL0005);
        using var many = new MemoryStream(); using (var zip = new ZipArchive(many, ZipArchiveMode.Create, true)) { for (int i = 0; i < 513; i++) { zip.CreateEntry("file" + i); } }
        many.Position = 0; Assert.Contains(LevelPackage.Read(many).Diagnostics, d => d.Code == DiagnosticCode.LVL0005);
    }

    [Fact]
    public void CatalogueKeepsBrokenEntriesAndUsesUserThenSemanticVersionPrecedence()
    {
        ContentRegistry content = ContentRegistry.Load(Array.Empty<(string path, string json)>());
        CatalogCandidate Candidate(string path, LevelSource source, string version) => new(path, source, Encoding.UTF8.GetBytes($$"""{"id":"test.level","name":"Level","version":"{{version}}"}"""));
        CatalogCandidate[] sources = [Candidate("built", LevelSource.BuiltIn, "9.0.0"), Candidate("user2", LevelSource.User, "1.2.0"), Candidate("user10", LevelSource.User, "1.10.0"), new("broken", LevelSource.User, Encoding.UTF8.GetBytes("bad"))];
        IReadOnlyList<CatalogEntry> entries = LevelCatalog.Scan(sources, content);
        Assert.Equal(2, entries.Count); CatalogEntry winner = entries.Single(e => e.Id == "test.level");
        Assert.Equal("user10", winner.Path); Assert.False(winner.IsPlayable);
        Assert.Contains(winner.Diagnostics, d => d.Severity == DiagnosticSeverity.Warning);
        Assert.Contains(entries, e => e.Id == "broken" && !e.IsPlayable);
        Assert.Equal(entries.Select(e => e.Path), LevelCatalog.Scan(sources.Reverse(), content).Select(e => e.Path));
        PackageLoadResult package = LevelPackage.Create(new[] { ("level.json", (ReadOnlyMemory<byte>)sources[0].Bytes.ToArray()), ("preview.png", (ReadOnlyMemory<byte>)new byte[] { 1, 2 }) });
        using var stream = new MemoryStream(); package.Package!.Write(stream);
        CatalogEntry packed = Assert.Single(LevelCatalog.Scan(new[] { new CatalogCandidate("a.dlvl", LevelSource.User, stream.ToArray(), true) }, content));
        Assert.Equal(2, packed.PreviewBytes.Length); Assert.Equal("test.level", packed.Id);
        Assert.False(Assert.Single(LevelCatalog.Scan(new[] { new CatalogCandidate("bad.dlvl", LevelSource.User, new byte[] { 1 }, true) }, content)).IsPlayable);
    }

    private static MemoryStream Zip(params (string path, byte[] bytes)[] files)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true)) { foreach (var file in files) { using Stream entry = zip.CreateEntry(file.path).Open(); entry.Write(file.bytes); } }
        stream.Position = 0; return stream;
    }
    [Fact]
    public void DirectoryPreviewsAndDescriptionsSurviveInvalidMetadataAndOwnTheirBytes()
    {
        ContentRegistry content = ContentRegistry.Load(Array.Empty<(string path, string json)>()); byte[] preview = [1, 2, 3];
        var candidate = new CatalogCandidate("directory/level.json", LevelSource.User, Encoding.UTF8.GetBytes("""{"id":"test.level","name":"Broken","description":{"en":"Authored description"}}"""), previewBytes: preview);
        CatalogEntry entry = Assert.Single(LevelCatalog.Scan([candidate], content)); preview[0] = 9;
        Assert.False(entry.IsPlayable); Assert.Equal("Authored description", entry.Description); Assert.Equal(new byte[] { 1, 2, 3 }, entry.PreviewBytes.ToArray());
    }
}
