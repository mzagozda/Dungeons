using Dungeons.Core.Content;
using Dungeons.Core.Level;
using Dungeons.Game.Rendering;
using Xunit;

namespace Dungeons.Core.Tests;

public sealed class ChunkMesherTests
{
    [Fact]
    public void MossyHollowHasSixStableChunksSeparateWaterAndVisibleCliffs()
    {
        string root = FindRoot(), path = Path.Combine(root, "game/content");
        string[] manifest = System.Text.Json.JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(path, "index.json")))!;
        ContentRegistry content = ContentRegistry.Load(manifest.Select(p => (p, File.ReadAllText(Path.Combine(path, p)))));
        LevelDocument document = LevelLoader.Load(File.ReadAllText(Path.Combine(root, "game/levels/mossy_hollow/level.json")), content).Document!;
        var source = new TerrainSnapshot(document, content, File.ReadAllText(Path.Combine(path, "atlas.json")));
        Assert.InRange(source[3, 24].R, 0.198f, 0.199f); Assert.InRange(source[3, 24].G, 0.434f, 0.435f); Assert.InRange(source[3, 24].B, 0.068f, 0.069f);
        var chunks = new List<ChunkGeometry>();
        for (int z = 0; z < 3; z++) { for (int x = 0; x < 2; x++) { chunks.Add(ChunkMesher.Build(source, x, z, source.WaterTexture)); } }
        Assert.Equal(6, chunks.Count); Assert.Contains(chunks, c => c.Water.Indices.Length > 0); Assert.Contains(chunks, c => c.Foliage.Indices.Length > 0);
        var treeModels = new HashSet<string>(); var wallModels = new HashSet<string>();
        for (int z = 0; z < source.Height; z++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                TerrainCell cell = source[x, z];
                if (cell.TileId == "core:tree_oak") { treeModels.Add(cell.Model!); Assert.InRange(cell.Rotation, 0, MathF.PI * 1.5f); }
                if (cell.TileId == "core:stone_wall") { wallModels.Add(cell.Model!); Assert.Equal(0, cell.Rotation); }
            }
        }
        Assert.Equal(3, treeModels.Count); Assert.Equal(2, wallModels.Count);
        Assert.Contains(chunks.SelectMany(c => c.Terrain.Vertices), v => v.Position.Y == -1 && v.Normal.Y == 0);
        Assert.Contains(chunks.SelectMany(c => c.Terrain.Vertices), v => v.Position.Y == 1 && v.Normal.Y == 1);
        Assert.Contains(chunks.SelectMany(c => c.Terrain.Vertices), v => v.Position.Y == 0.25f && v.Normal.Y == 1);
        foreach (ChunkGeometry chunk in chunks)
        {
            Assert.Equal(0, chunk.Terrain.Indices.Length % 3);
            Assert.All(chunk.Terrain.Indices, i => Assert.InRange(i, 0, chunk.Terrain.Vertices.Length - 1));
            Assert.All(chunk.Water.Vertices, v => Assert.InRange(v.Position.Y, -0.15001f, -0.14999f));
            Assert.Equal(chunk.Terrain.Vertices, ChunkMesher.Build(source, chunk.X, chunk.Z, source.WaterTexture).Terrain.Vertices);
        }
        Assert.Equal(0, source[-1, 0].Height); Assert.True(source[-1, 0].Void);
        Assert.Throws<ArgumentOutOfRangeException>(() => ChunkMesher.Build(source, 2, 0, source.WaterTexture));
    }
    private static string FindRoot()
    { DirectoryInfo? d = new(AppContext.BaseDirectory); while (d is not null && !File.Exists(Path.Combine(d.FullName, "specification.md"))) { d = d.Parent; } return d!.FullName; }
}
