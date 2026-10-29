using BenchmarkDotNet.Attributes;
using Dungeons.Core.Content;
using Dungeons.Core.Level;
using Dungeons.Core.Sim;

namespace Dungeons.Benchmarks;

/// <summary>Whole authored gameplay candidate; reviewed golden promotion is tracked separately.</summary>
[MemoryDiagnoser]
public class MossyReplayBenchmarks
{
    private LevelDocument _level = null!;
    private ReplaySnapshot _replay = null!;
    private GameSession _session = null!;
    [GlobalSetup]
    public void Load()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "specification.md"))) { root = root.Parent; }
        if (root is null) { throw new DirectoryNotFoundException("Repository root not found."); }
        string contentDirectory = Path.Combine(root.FullName, "game/content"); string[] files = System.Text.Json.JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(contentDirectory, "index.json")))!;
        ContentRegistry content = ContentRegistry.Load(files.Select(path => (path, File.ReadAllText(Path.Combine(contentDirectory, path)))));
        _level = LevelLoader.Load(File.ReadAllText(Path.Combine(root.FullName, "game/levels/mossy_hollow/level.json")), content).Document ?? throw new InvalidDataException("Mossy Hollow failed validation.");
        ReplayDocument replay = ReplayJson.Read(File.ReadAllText(Path.Combine(root.FullName, "artifacts/m6/candidates/mossy_hollow_full_clear.dreplay"))).Document ?? throw new InvalidDataException("Run the scripted playthrough before this benchmark.");
        if (replay.LevelHash != _level.SourceHash || replay.ContentHash != content.Hash || replay.Snapshot.TickCount != 11591) { throw new InvalidDataException("Replay identity/tick count changed; regenerate and review the benchmark fixture."); }
        _replay = replay.Snapshot;
    }
    [IterationSetup]
    public void Reset() { _session = new(_level, _replay.Seed, difficulty: _replay.Difficulty, startOverride: _replay.StartOverride); _session.Hash(); }
    [Benchmark(OperationsPerInvoke = 11591)]
    public ulong MossyHollowFullClearReplay()
    {
        ReplayVerification result = ReplayVerifier.Verify(_session, _replay);
        if (!result.Success || _session.State != SessionState.Won) { throw new InvalidOperationException(result.Message); }
        return _session.Hash();
    }
}
