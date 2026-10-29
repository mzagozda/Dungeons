using Dungeons.Benchmarks;
using Dungeons.Core.Level;
using Dungeons.Core.Math;
using Dungeons.Core.Sim;
using Dungeons.Game.Rendering;
using Xunit;

namespace Dungeons.Core.Tests;

public sealed class NightlyBenchmarkFixtureTests
{
    [Fact]
    public void FullMazeIsRepeatableConnectedAndRequiresALongNonDiagonalRoute()
    {
        LevelDocument first = NightlyBenchmarkData.Maze(), second = NightlyBenchmarkData.Maze(); int walkable = 0;
        for (int z = 0; z < 256; z++)
        {
            for (int x = 0; x < 256; x++)
            {
                Assert.Equal(first.GetCell(x, z).Walkable, second.GetCell(x, z).Walkable);
                if (first.GetCell(x, z).Walkable) { walkable++; }
                if (x % 2 == 1 && z % 2 == 1 && x < 255 && z < 255) { Assert.True(first.GetCell(x, z).Walkable); }
                if (x is 0 or 255 || z is 0 or 255) { Assert.False(first.GetCell(x, z).Walkable); }
            }
        }
        Assert.Equal(32257, walkable);
        var finder = new AStar(first.BakeGrid()); PathResult path = finder.FindPath(new(1, 1), new(253, 253), new IntVec2[GameConstants.MaxPathWaypoints]);
        Assert.True(path.ReachedGoal); Assert.True(path.Truncated); Assert.True(path.Cost > 6000);
    }
    [Fact]
    public void FullSizeLoaderAndAll256MesherChunksUseRealTerrainContent()
    {
        string root = NightlyBenchmarkData.Root(); var content = NightlyBenchmarkData.Content(root);
        LevelLoadResult flat = LevelLoader.Load(NightlyBenchmarkData.LargeLevelJson(root), content);
        LevelLoadResult rich = LevelLoader.Load(NightlyBenchmarkData.LargeLevelJson(root, true), content);
        Assert.True(flat.Success, string.Join('\n', flat.Diagnostics)); Assert.True(rich.Success, string.Join('\n', rich.Diagnostics));
        Assert.Equal(256, flat.Document!.Width); Assert.Equal(256, flat.Document.Height);
        Assert.Equal(64, flat.Document.Entities.Count(entity => entity.Definition.Kind == EntityKind.Enemy));
        var snapshot = new TerrainSnapshot(rich.Document!, content, File.ReadAllText(Path.Combine(root, "game/content/atlas.json")));
        int terrain = 0, water = 0, foliage = 0;
        for (int z = 0; z < 16; z++)
        {
            for (int x = 0; x < 16; x++)
            {
                ChunkGeometry chunk = ChunkMesher.Build(snapshot, x, z, snapshot.WaterTexture);
                Assert.Equal(x, chunk.X); Assert.Equal(z, chunk.Z); terrain += chunk.Terrain.Indices.Length;
                water += chunk.Water.Indices.Length; foliage += chunk.Foliage.Indices.Length;
            }
        }
        Assert.True(terrain > 65536 * 6); Assert.True(water > 0); Assert.True(foliage > 0);
    }
}
