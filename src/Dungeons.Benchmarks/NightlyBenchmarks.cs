using BenchmarkDotNet.Attributes;
using Dungeons.Core.Content;
using Dungeons.Core.Level;
using Dungeons.Core.Math;
using Dungeons.Core.Sim;
using Dungeons.Game.Rendering;

namespace Dungeons.Benchmarks;

/// <summary>The six specification 20.7 workloads. Source construction and resetting are outside timed invocations.</summary>
[MemoryDiagnoser]
public class NightlyBenchmarks
{
    private readonly MossyReplayBenchmarks _mossy = new();
    private ContentRegistry _content = null!;
    private string _largeJson = null!;
    private AStar _open = null!, _maze = null!;
    private string _mazeSha256 = null!;
    private readonly IntVec2[] _path = new IntVec2[GameConstants.MaxPathWaypoints];
    private TerrainSnapshot _terrain = null!;
    private GameSession _stress = null!;
    [GlobalSetup(Target = nameof(Tick_MossyHollow_Replay))]
    public void LoadReplay() => _mossy.Load();
    [GlobalSetup(Target = nameof(LevelLoad_256x256))]
    public void LoadLevel()
    {
        string root = NightlyBenchmarkData.Root(); _content = NightlyBenchmarkData.Content(root);
        _largeJson = NightlyBenchmarkData.LargeLevelJson(root);
        LevelLoadResult large = LevelLoader.Load(_largeJson, _content);
        if (!large.Success) { throw new InvalidDataException("The full-size benchmark level failed validation."); }
    }
    [GlobalSetup(Target = nameof(ChunkMesher_256x256))]
    public void LoadMesher()
    {
        string root = NightlyBenchmarkData.Root(); _content = NightlyBenchmarkData.Content(root);
        LevelLoadResult rich = LevelLoader.Load(NightlyBenchmarkData.LargeLevelJson(root, true), _content);
        if (!rich.Success) { throw new InvalidDataException("The rich benchmark terrain failed validation."); }
        _terrain = new(rich.Document!, _content, File.ReadAllText(Path.Combine(root, "game/content/atlas.json")));
    }
    [GlobalSetup(Target = nameof(AStar_256_Open))]
    public void LoadOpen() => _open = new(new LevelDocument(256, 256).BakeGrid());
    [GlobalSetup(Target = nameof(AStar_256_Maze))]
    public void LoadMaze()
    {
        LevelDocument maze = NightlyBenchmarkData.Maze(); byte[] mazeCells = new byte[256 * 256];
        for (int z = 0; z < 256; z++) { for (int x = 0; x < 256; x++) { mazeCells[z * 256 + x] = maze.GetCell(x, z).Walkable ? (byte)1 : (byte)0; } }
        _mazeSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(mazeCells)).ToLowerInvariant();
        _maze = new(maze.BakeGrid());
        if (!_maze.FindPath(new(1, 1), new(253, 253), _path).ReachedGoal) { throw new InvalidDataException("Maze goal is unreachable."); }
    }
    [IterationSetup(Target = nameof(Tick_MossyHollow_Replay))]
    public void ResetReplay() => _mossy.Reset();
    [IterationSetup(Target = nameof(Tick_Stress_64Monsters_32Arrows))]
    public void ResetStress() => _stress = CombatStressFixture.Create();
    [Benchmark(OperationsPerInvoke = 11591)]
    public ulong Tick_MossyHollow_Replay() => _mossy.MossyHollowFullClearReplay();
    [Benchmark(OperationsPerInvoke = 6000)]
    public ulong Tick_Stress_64Monsters_32Arrows()
    {
        for (int tick = 0; tick < 6000; tick++) { _stress.Step(CombatStressFixture.Input(tick)); _stress.DrainEvents(); }
        return _stress.Hash();
    }
    [Benchmark]
    public PathResult AStar_256_Open() => _open.FindPath(new(0, 0), new(255, 255), _path);
    [Benchmark]
    public PathResult AStar_256_Maze() => _maze.FindPath(new(1, 1), new(253, 253), _path);
    [Benchmark]
    public int ChunkMesher_256x256()
    {
        int indices = 0;
        for (int z = 0; z < 16; z++)
        {
            for (int x = 0; x < 16; x++)
            {
                ChunkGeometry chunk = ChunkMesher.Build(_terrain, x, z, _terrain.WaterTexture);
                indices += chunk.Terrain.Indices.Length + chunk.Water.Indices.Length + chunk.Foliage.Indices.Length;
            }
        }
        return indices;
    }
    [Benchmark]
    public LevelLoadResult LevelLoad_256x256() => LevelLoader.Load(_largeJson, _content);

    /// <summary>Executes the real workloads once before measurement, rejecting empty or incorrectly sized fixtures.</summary>
    public object Contracts()
    {
        LoadReplay(); LoadLevel(); LoadMesher(); LoadOpen(); LoadMaze(); ResetReplay(); ulong fullClear = Tick_MossyHollow_Replay(); ResetStress();
        int monsters = _stress.World.LiveMonsterCount;
        int arrows = _stress.World.Entities.Count(entity => entity.Kind == EntityKind.Projectile);
        int pickups = _stress.World.Entities.Count(entity => entity.Kind == EntityKind.Pickup);
        ulong stress = Tick_Stress_64Monsters_32Arrows(); PathResult open = AStar_256_Open(), maze = AStar_256_Maze();
        int indices = ChunkMesher_256x256(); LevelLoadResult loaded = LevelLoad_256x256();
        if (monsters != 64 || arrows != 32 || pickups != 128 || _stress.World.LiveMonsterCount != 64 || !open.ReachedGoal || !maze.ReachedGoal || maze.Cost <= open.Cost || indices <= 65536 * 6 || !loaded.Success || loaded.Document!.Width != 256 || loaded.Document.Height != 256 || loaded.Document.Entities.Count(entity => entity.Definition.Kind == EntityKind.Enemy) != 64)
        { throw new InvalidDataException("Nightly benchmark workload contract failed."); }
        string root = NightlyBenchmarkData.Root();
        string inputs = string.Join('\n', NightlyBenchmarkData.Version, _largeJson, NightlyBenchmarkData.LargeLevelJson(root, true), _mazeSha256, _content.Hash,
            File.ReadAllText(Path.Combine(root, "game/content/atlas.json")), File.ReadAllText(Path.Combine(root, "game/levels/mossy_hollow/level.json")),
            File.ReadAllText(Path.Combine(root, "artifacts/m6/candidates/mossy_hollow_full_clear.dreplay")));
        string identity = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(inputs))).ToLowerInvariant();
        return new { formatVersion = 1, fixtureVersion = NightlyBenchmarkData.Version, fixtureSha256 = identity, requiredMethods = NightlyBenchmarkData.Names, correctnessPassed = true,
            stressInitialMonsters = monsters, stressInitialArrows = arrows, stressInitialPickups = pickups, stressFinalMonsters = _stress.World.LiveMonsterCount,
            fullClearHash = fullClear.ToString("x16"), stressFinalHash = stress.ToString("x16"), openCost = open.Cost, mazeCost = maze.Cost, meshedChunks = 256, meshedIndices = indices,
            runnerIdentity = new { cpuIdentity = CpuIdentity(), logicalProcessors = Environment.ProcessorCount }, physicalAcceptance = false };
    }
    private static string CpuIdentity()
    {
        // The pinned BenchmarkDotNet host reports "Unknown processor" on this Windows
        // installation. Record a second OS-provided identity rather than pooling CPUs.
        string? identity = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
        if (string.IsNullOrWhiteSpace(identity) && File.Exists("/proc/cpuinfo"))
        { identity = string.Join("; ", File.ReadLines("/proc/cpuinfo").Where(line => line.StartsWith("model name", StringComparison.Ordinal)).Select(line => line[(line.IndexOf(':') + 1)..].Trim()).Distinct().Order(StringComparer.Ordinal)); }
        if (string.IsNullOrWhiteSpace(identity)) { throw new PlatformNotSupportedException("An actual CPU identity is required for the per-runner baseline."); }
        return identity;
    }
}
