using System.Text;
using Dungeons.Core.Level;
using Dungeons.Core.Level.Commands;
using Dungeons.Core.Sim;
using Xunit;

namespace Dungeons.Core.Tests;

[Collection("Performance")]
public sealed class AssetSourceTests
{
    [Fact]
    public void MemorySourcesCopyInputsSortPathsAndKeepMissingPathsDistinctFromEmptyFiles()
    {
        byte[] input = [1, 2, 3]; var source = new MemoryAssetSource([("assets/z.bin", input), ("assets/a/empty.bin", ReadOnlyMemory<byte>.Empty)]); input[0] = 99;
        Assert.Equal(["assets/a/empty.bin", "assets/z.bin"], source.Paths);
        Assert.True(source.TryRead("assets/z.bin", out var stored)); Assert.Equal(new byte[] { 1, 2, 3 }, stored.ToArray());
        Assert.True(source.TryRead("assets/a/empty.bin", out var empty)); Assert.True(empty.IsEmpty);
        foreach (string path in new[] { "assets/missing", "assets/Z.bin", "assets/../z.bin", "/assets/z.bin", "level.json", "assets\\z.bin", "" })
        { Assert.False(source.TryRead(path, out var missing)); Assert.True(missing.IsEmpty); }
        Assert.False(source.TryRead(null!, out _)); Assert.Empty(MemoryAssetSource.Empty.Paths); Assert.False(MemoryAssetSource.Empty.TryRead("assets/a.bin", out _));
        Assert.Throws<NotSupportedException>(() => ((IList<string>)source.Paths).Add("bad")); Assert.Throws<ArgumentNullException>(() => new MemoryAssetSource(null!));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("assets/../escape")]
    [InlineData("assets\\escape")]
    [InlineData("assets/CON.png")]
    [InlineData("assets/file. ")]
    [InlineData("level.json")]
    [InlineData("Assets/wrong-case.bin")]
    [InlineData("assets/")]
    public void SourcesRejectUnsafeOrNonAssetPaths(string path)
        => Assert.Throws<InvalidDataException>(() => new MemoryAssetSource([(path, ReadOnlyMemory<byte>.Empty)]));

    [Fact]
    public void DuplicateCaseParentConflictsAndCountOrByteOverflowAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => new MemoryAssetSource([("assets/a", ReadOnlyMemory<byte>.Empty), ("assets/A", ReadOnlyMemory<byte>.Empty)]));
        Assert.Throws<InvalidDataException>(() => new MemoryAssetSource([("assets/a/b", ReadOnlyMemory<byte>.Empty), ("assets/A", ReadOnlyMemory<byte>.Empty)]));
        Assert.Throws<InvalidDataException>(() => new MemoryAssetSource(Enumerable.Range(0, LevelPackage.MaxEntries + 1).Select(i => ("assets/" + i, ReadOnlyMemory<byte>.Empty))));
        Assert.Throws<InvalidDataException>(() => new MemoryAssetSource([("assets/big", new byte[LevelPackage.MaxBytes + 1])]));
    }

    [Fact]
    public void PackageRoundTripAndAllLoaderBoundariesRetainTheSameReservedBytesAndOneWarning()
    {
        byte[] input = [4, 5, 6]; byte[] json = Encoding.UTF8.GetBytes(LevelCommandTests.Create().ToJson());
        PackageLoadResult created = LevelPackage.Create([("level.json", json), ("assets/texture.bin", input)]); Assert.True(created.Success); input[0] = 88;
        Assert.True(created.Package!.Assets.TryRead("assets/texture.bin", out var bytes)); Assert.Equal(new byte[] { 4, 5, 6 }, bytes.ToArray());
        using var zip = new MemoryStream(); created.Package.Write(zip); zip.Position = 0; PackageLoadResult read = LevelPackage.Read(zip); Assert.True(read.Success);
        LevelLoadResult[] results = [LevelLoader.LoadPackage(read.Package!, MissionAssets.Content), LevelLoader.Load(json, MissionAssets.Content, assets: read.Package!.Assets),
            LevelLoader.Load(Encoding.UTF8.GetString(json), MissionAssets.Content, assets: read.Package.Assets), LevelLoader.Load(new MemoryStream(json), MissionAssets.Content, assets: read.Package.Assets)];
        foreach (LevelLoadResult loaded in results)
        {
            Assert.True(loaded.Success); Assert.Same(read.Package.Assets, loaded.Document!.Assets); AssertWarning(loaded.Diagnostics);
            Assert.True(loaded.Document.Assets.TryRead("assets/texture.bin", out var actual)); Assert.Equal(bytes.ToArray(), actual.ToArray());
            AssertWarning(LevelValidator.Validate(loaded.Document));
        }
        Assert.Throws<ArgumentNullException>(() => LevelLoader.LoadPackage(null!, MissionAssets.Content));
        LevelLoadResult empty = LevelLoader.Load(Encoding.UTF8.GetString(json), MissionAssets.Content, assets: MemoryAssetSource.Empty); Assert.DoesNotContain(empty.Diagnostics, d => d.JsonPath == "/assets");
    }

    [Fact]
    public void EditsUndoReloadAndValidationKeepAssetsWithoutChangingSimulationOrSerialization()
    {
        string json = LevelCommandTests.Create().ToJson(); LevelDocument plain = LevelLoader.Load(json, MissionAssets.Content).Document!; var assets = new MemoryAssetSource([("assets/model.bin", new byte[] { 7 })]);
        LevelDocument document = LevelLoader.Load(json, MissionAssets.Content, assets: assets).Document!;
        Assert.Equal(plain.SourceHash, document.SourceHash); Assert.Equal(LevelSerializer.Write(plain), LevelSerializer.Write(document));
        var normal = GameSession.Create(plain, MissionAssets.Content, 9); var reserved = GameSession.Create(document, MissionAssets.Content, 9);
        Assert.Equal(normal.Hash(), reserved.Hash()); normal.Step(default(PlayerInput)); reserved.Step(default(PlayerInput)); Assert.Equal(normal.Hash(), reserved.Hash());
        var edit = new SetMetadata("name", "Asset edit"); edit.Apply(document); Assert.Same(assets, document.Assets); AssertWarning(document.Diagnostics);
        string history = LevelCommandSerializer.Write(edit); var reloaded = LevelLoader.LoadForEditing(LevelSerializer.Write(document), MissionAssets.Content, assets: assets).Document!;
        LevelCommandSerializer.Read(history).Undo(reloaded); Assert.Same(assets, reloaded.Assets); AssertWarning(reloaded.Diagnostics);
        edit.Undo(document); Assert.Same(assets, document.Assets); AssertWarning(document.Diagnostics); Assert.Equal(plain.SourceHash, document.SourceHash);
        document.PaintCell(1, 8, "core:grass", 0); Assert.Same(assets, document.Assets); AssertWarning(document.Diagnostics);
        JsonObjectWithBadEntity(document, assets);
    }
    private static void JsonObjectWithBadEntity(LevelDocument document, IAssetSource assets)
    {
        var source = document.ToJsonObject(); source["entities"]![3]!["type"] = "core:missing";
        var loaded = LevelLoader.LoadForEditing(source.ToJsonString(), MissionAssets.Content, assets: assets);
        Assert.NotNull(loaded.Document); Assert.False(loaded.Success); Assert.Same(assets, loaded.Document!.Assets); AssertWarning(loaded.Diagnostics);
        Assert.Throws<ArgumentException>(() => GameSession.Create(loaded.Document, MissionAssets.Content, 1));
        Assert.Null(LevelLoader.Load(source.ToJsonString(), MissionAssets.Content, assets: assets).Document);
        Assert.False(LevelLoader.Load("{}", MissionAssets.Content, assets: assets).Success);
    }

    [Fact]
    public void PackedAndDirectoryCatalogueCandidatesRetainAssetsAndDeduplicateTheUnsupportedWarning()
    {
        byte[] json = Encoding.UTF8.GetBytes(LevelCommandTests.Create().ToJson()); var assets = new MemoryAssetSource([("assets/x", new byte[] { 1, 4 })]);
        var packed = LevelPackage.Create([("level.json", json), ("assets/x", new byte[] { 1, 4 })]); using var zip = new MemoryStream(); packed.Package!.Write(zip);
        foreach (CatalogCandidate candidate in new CatalogCandidate[] { new("directory/level.json", LevelSource.User, json, assets: assets), new("file.dlvl", LevelSource.User, zip.ToArray(), true) })
        {
            CatalogEntry entry = LevelCatalog.Scan([candidate], MissionAssets.Content).Single(); Assert.True(entry.IsPlayable); AssertWarning(entry.Diagnostics);
            Assert.True(entry.Document!.Assets.TryRead("assets/x", out var bytes)); Assert.Equal(new byte[] { 1, 4 }, bytes.ToArray());
        }
    }
    private static void AssertWarning(IReadOnlyList<Diagnostic> diagnostics)
    { Diagnostic diagnostic = Assert.Single(diagnostics, d => d.JsonPath == "/assets"); Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity); Assert.Equal(DiagnosticCode.LVL0100, diagnostic.Code); Assert.Contains("ignored in v1", diagnostic.Message); }
}
