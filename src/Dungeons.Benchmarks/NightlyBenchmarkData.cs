using System.Text.Json;
using System.Text.Json.Nodes;
using Dungeons.Core.Content;
using Dungeons.Core.Level;
using Dungeons.Core.Math;
using Dungeons.Core.Sim;

namespace Dungeons.Benchmarks;

/// <summary>Versioned, deterministic inputs shared by the six required nightly workloads and their contract checks.</summary>
public static class NightlyBenchmarkData
{
    public const int Version = 3;
    public static readonly string[] Names = ["Tick_MossyHollow_Replay", "Tick_Stress_64Monsters_32Arrows", "AStar_256_Open", "AStar_256_Maze", "ChunkMesher_256x256", "LevelLoad_256x256"];
    public static string Root()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "specification.md"))) { directory = directory.Parent; }
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
    public static ContentRegistry Content(string root)
    {
        string folder = Path.Combine(root, "game/content");
        string[] files = JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(folder, "index.json")))!;
        ContentRegistry content = ContentRegistry.Load(files.Select(path => (path, File.ReadAllText(Path.Combine(folder, path)))));
        if (!content.IsValid) { throw new InvalidDataException("Benchmark content registry is invalid."); }
        return content;
    }
    public static string LargeLevelJson(string root, bool richTerrain = false)
    {
        JsonObject source = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "game/levels/mossy_hollow/level.json")))!.AsObject();
        source["id"] = "benchmark.large"; source["size"] = new JsonObject { ["width"] = 256, ["height"] = 256 };
        source["palette"] = new JsonObject { ["."] = "core:grass", ["~"] = "core:water", [","] = new JsonObject { ["tile"] = "core:grass", ["overlay"] = "core:tall_grass" } };
        var floor = new JsonArray(); var heights = new JsonArray();
        for (int z = 0; z < 256; z++)
        {
            char[] row = new char[256], elevation = new char[256];
            for (int x = 0; x < 256; x++)
            {
                row[x] = richTerrain && x >= 80 && z % 32 is >= 20 and <= 23 ? '~' : richTerrain && (x + z) % 5 == 0 ? ',' : '.';
                elevation[x] = richTerrain && x >= 80 && z >= 80 && x % 32 is >= 8 and <= 23 && z % 32 is >= 8 and <= 15 ? '1' : '0';
            }
            floor.Add(new string(row)); heights.Add(new string(elevation));
        }
        source["layers"] = new JsonObject { ["floor"] = floor, ["heights"] = heights };
        var entities = new JsonArray(
            new JsonObject { ["id"] = "spawn", ["type"] = "core:player_spawn", ["x"] = 4.5, ["z"] = 4.5 },
            new JsonObject { ["id"] = "exit", ["type"] = "core:exit", ["x"] = 250.5, ["z"] = 250.5 },
            new JsonObject { ["type"] = "core:checkpoint", ["x"] = 6.5, ["z"] = 6.5 });
        for (int i = 0; i < 64; i++) { entities.Add(new JsonObject { ["type"] = "core:zombie", ["x"] = 8.5 + i % 8 * 8, ["z"] = 8.5 + i / 8 * 8 }); }
        source["entities"] = entities; source["triggers"] = new JsonArray(); source["objectives"] = new JsonArray();
        source["completion"] = new JsonObject { ["type"] = "exit", ["exit"] = "exit" };
        return source.ToJsonString();
    }
    public static LevelDocument Maze()
    {
        var level = new LevelDocument(256, 256, "benchmark.maze");
        for (int z = 0; z < 256; z++) { for (int x = 0; x < 256; x++) { level.SetCell(x, z, Cell.Wall); } }
        // Deterministic depth-first perfect maze on odd cells. It has genuine dead ends,
        // one-cell corridors and no diagonal corner cutting; construction is not timed.
        var random = new Rng(0x5eed); var stack = new Stack<IntVec2>();
        IntVec2[] directions = [new(0, -2), new(2, 0), new(0, 2), new(-2, 0)];
        IntVec2[] choices = new IntVec2[4]; level.SetCell(1, 1, Cell.Floor); stack.Push(new(1, 1));
        while (stack.Count > 0)
        {
            IntVec2 at = stack.Peek(); int count = 0;
            foreach (IntVec2 direction in directions)
            {
                var next = new IntVec2(at.X + direction.X, at.Z + direction.Z);
                if (next.X > 0 && next.Z > 0 && next.X < 255 && next.Z < 255 && !level.GetCell(next.X, next.Z).Walkable) { choices[count++] = next; }
            }
            if (count == 0) { stack.Pop(); continue; }
            IntVec2 chosen = choices[random.NextInt(0, count)];
            level.SetCell((at.X + chosen.X) / 2, (at.Z + chosen.Z) / 2, Cell.Floor); level.SetCell(chosen.X, chosen.Z, Cell.Floor); stack.Push(chosen);
        }
        return level;
    }
}
