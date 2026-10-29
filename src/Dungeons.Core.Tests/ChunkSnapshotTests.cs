using System.Diagnostics;
using System.Text.Json.Nodes;
using Dungeons.Core.Level;
using Dungeons.Core.Level.Commands;
using Dungeons.Core.Sim;
using Dungeons.Game.Rendering;
using Xunit;
using Xunit.Abstractions;

namespace Dungeons.Core.Tests;

[Collection("Performance")]
public sealed class ChunkSnapshotTests(ITestOutputHelper output)
{
    private static readonly string Atlas = File.ReadAllText(Path.Combine(MissionAssets.Root, "game/content/atlas.json"));
    [Fact]
    public void DirectAuthoredReferencesRespectCompositeAndLayerOverlaysAndCommandUndo()
    {
        LevelDocument document = LevelCommandTests.Create(); Assert.Same(MissionAssets.Content, document.Registry);
        new SetTile(2, 2, new JsonObject { ["tile"] = "core:grass", ["overlay"] = "core:tall_grass" }).Apply(document);
        Assert.Equal(("core:grass", "core:tall_grass"), document.GetTileReference(2, 2));
        var overlay = new SetOverlay(2, 2, "core:flowers"); overlay.Apply(document); Assert.Equal(("core:grass", "core:flowers"), document.GetTileReference(2, 2));
        overlay.Undo(document); Assert.Equal(("core:grass", "core:tall_grass"), document.GetTileReference(2, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.GetTileReference(-1, 0)); Assert.Throws<ArgumentOutOfRangeException>(() => document.GetTileReference(12, 0));
        var code = new LevelDocument(8, 8); Assert.Null(code.Registry); Assert.Throws<InvalidOperationException>(() => code.GetTileReference(1, 1));
    }

    [Fact]
    public void LocalCopiesMatchCompleteRebuildsAndOlderWorkersKeepTheirOriginalCells()
    {
        LevelDocument document = MissionAssets.Load("game/levels/mossy_hollow/level.json"); var old = new TerrainSnapshot(document, MissionAssets.Content, Atlas);
        TerrainCell original = old[16, 34]; ChunkGeometry originalMesh = ChunkMesher.Build(old, 1, 2, old.WaterTexture);
        var session = new GameSession(document); ulong initial = session.Hash();
        var edit = new SetTile(16, 34, new JsonObject { ["tile"] = "core:grass", ["overlay"] = "core:tall_grass" }); edit.Apply(document);
        TerrainSnapshot updated = old.WithChanges(document, new(16, 34, 1, 1)), complete = new(document, MissionAssets.Content, Atlas);
        Assert.Equal(original, old[16, 34]); Assert.Equal("core:grass", updated[16, 34].TileId); Assert.NotNull(updated[16, 34].Overlay);
        for (int z = 0; z < document.Height; z++) { for (int x = 0; x < document.Width; x++) { Assert.Equal(complete[x, z], updated[x, z]); } }
        Assert.Equal(originalMesh.Terrain.Vertices, ChunkMesher.Build(old, 1, 2, old.WaterTexture).Terrain.Vertices);
        Assert.Equal(initial, session.Hash());
        edit.Undo(document); TerrainSnapshot undone = updated.WithChanges(document, new(16, 34, 1, 1)); Assert.Equal(original, undone[16, 34]);
        Assert.Same(undone, undone.WithChanges(document, default)); Assert.Same(undone, undone.WithChanges(document, new(int.MaxValue, int.MaxValue, 1, 1)));
    }

    [Fact]
    public async Task ConcurrentMeshReadersObserveCompleteIndependentSnapshotsAcrossRapidEdits()
    {
        LevelDocument document = MissionAssets.Load("game/levels/mossy_hollow/level.json"); var snapshots = new List<TerrainSnapshot> { new(document, MissionAssets.Content, Atlas) };
        var tasks = new List<Task<ChunkGeometry>>(); var expected = new List<TerrainVertex[]>();
        for (int i = 0; i < 12; i++)
        {
            var edit = new SetHeight(10, 35, i % 2); edit.Apply(document); TerrainSnapshot snapshot = snapshots[^1].WithChanges(document, new(10, 35, 1, 1)); snapshots.Add(snapshot);
            expected.Add(ChunkMesher.Build(snapshot, 0, 2, snapshot.WaterTexture).Terrain.Vertices); tasks.Add(Task.Run(() => ChunkMesher.Build(snapshot, 0, 2, snapshot.WaterTexture)));
        }
        ChunkGeometry[] results = await Task.WhenAll(tasks);
        for (int i = 0; i < results.Length; i++) { Assert.Equal(expected[i], results[i].Terrain.Vertices); Assert.Equal((byte)(i % 2), snapshots[i + 1][10, 35].Height); }
    }

    [Fact]
    public void ChunkRectanglesIncludeBoundaryDependenciesAndClipExtremeCoordinates()
    {
        AssertRect(ChunkRect.FromCells(new(2, 2, 1, 1), 256, 256), 0, 0, 1, 1);
        AssertRect(ChunkRect.FromCells(new(15, 15, 1, 1), 256, 256), 0, 0, 2, 2);
        AssertRect(ChunkRect.FromCells(new(16, 16, 1, 1), 256, 256), 0, 0, 2, 2);
        AssertRect(ChunkRect.FromCells(new(255, 255, 1, 1), 256, 256), 15, 15, 1, 1);
        AssertRect(ChunkRect.FromCells(new(-1, -1, 2, 2), 17, 17), 0, 0, 1, 1);
        Assert.True(ChunkRect.FromCells(new(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue), 256, 256).IsEmpty);
        Assert.True(ChunkRect.FromCells(new(-10, -10, 2, 2), 256, 256).IsEmpty); Assert.True(ChunkRect.FromCells(default, 256, 256).IsEmpty);
        AssertRect(new ChunkRect(-1, -1, 3, 3).Clip(2, 2), 0, 0, 2, 2); Assert.True(new ChunkRect(int.MaxValue, 0, int.MaxValue, 1).Clip(2, 2).IsEmpty);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChunkRect(0, 0, -1, 1)); Assert.Throws<ArgumentOutOfRangeException>(() => new ChunkRect(0, 0, 1, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ChunkRect.FromCells(default, 0, 1)); Assert.Throws<ArgumentOutOfRangeException>(() => default(ChunkRect).Clip(-1, 1));
    }

    [Fact]
    public void IdentityResizeRegistryAndInvalidSourceChangesRequireACompleteSnapshotRebuild()
    {
        LevelDocument document = LevelCommandTests.Create(); var snapshot = new TerrainSnapshot(document, MissionAssets.Content, Atlas);
        Assert.Throws<ArgumentNullException>(() => snapshot.WithChanges(null!, default));
        new Resize(13, 13).Apply(document); Assert.Throws<ArgumentException>(() => snapshot.WithChanges(document, new(0, 0, 1, 1)));
        Assert.Throws<ArgumentNullException>(() => new TerrainSnapshot(null!, MissionAssets.Content, Atlas));
        document = LevelCommandTests.Create(); new SetTile(2, 2, "core:missing").Apply(document);
        Assert.Throws<ArgumentException>(() => snapshot.WithChanges(document, new(2, 2, 1, 1))); Assert.Throws<ArgumentException>(() => new TerrainSnapshot(document, MissionAssets.Content, Atlas));
        Assert.Throws<ArgumentOutOfRangeException>(() => ChunkMesher.Build(snapshot, int.MaxValue, 0, snapshot.WaterTexture)); Assert.Throws<ArgumentNullException>(() => ChunkMesher.Build(null!, 0, 0, default));
    }

    [Fact, Trait("Category", "Performance")]
    public void UpdatedChunksStayBelowSixteenMillisecondsOnTheLargestAuthoredGrid()
    {
        LevelDocument document = MissionAssets.Load("game/levels/mossy_hollow/level.json"); new Resize(256, 256).Apply(document);
        Assert.False(document.HasErrors); var original = new TerrainSnapshot(document, MissionAssets.Content, Atlas); new SetHeight(120, 120, 1).Apply(document);
        for (int i = 0; i < 4; i++) { var warm = original.WithChanges(document, new(120, 120, 1, 1)); _ = ChunkMesher.Build(warm, 7, 7, warm.WaterTexture); }
        for (int i = 0; i < 8; i++)
        {
            var timer = Stopwatch.StartNew(); TerrainSnapshot next = original.WithChanges(document, new(120, 120, 1, 1)); ChunkGeometry geometry = ChunkMesher.Build(next, 7, 7, next.WaterTexture); timer.Stop();
            output.WriteLine($"Snapshot update + chunk mesh: {timer.Elapsed.TotalMilliseconds:F3} ms"); Assert.NotEmpty(geometry.Terrain.Vertices); Assert.True(timer.Elapsed.TotalMilliseconds < 16);
        }
    }
    private static void AssertRect(ChunkRect value, int x, int z, int width, int height)
    { Assert.Equal(x, value.X); Assert.Equal(z, value.Z); Assert.Equal(width, value.Width); Assert.Equal(height, value.Height); }
}
