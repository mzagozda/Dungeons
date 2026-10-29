using BenchmarkDotNet.Attributes;
using Dungeons.Core.Content;
using Dungeons.Core.Level;
using Dungeons.Core.Math;
using Dungeons.Core.Sim;

namespace Dungeons.Benchmarks;

/// <summary>M1 terrain/navigation fixtures; later milestones add the authored combat mission.</summary>
[MemoryDiagnoser]
public class CoreBenchmarks
{
    private GameSession _empty = null!, _actors = null!;
    private AStar _pathfinder = null!;
    private readonly IntVec2[] _path = new IntVec2[GameConstants.MaxPathWaypoints];
    private readonly PlayerInput[] _none = [];
    private readonly PlayerInput _idle = new(Vec2.Zero);

    [GlobalSetup]
    public void Setup()
    {
        _empty = new(new LevelDocument(30, 30));
        var builder = new ScenarioBuilder(30, 30);
        builder.Entity(new EntityDef("bench:hero", EntityKind.Player, new StatBlock(100, 4.5f)), new(1.5f, 1.5f));
        var enemy = new EntityDef("bench:enemy", EntityKind.Enemy, new StatBlock(40, 2.6f));
        for (int i = 0; i < 64; i++) { builder.Entity(enemy, new(5.5f + i % 8 * 2, 5.5f + i / 8 * 2)); }
        _actors = builder.Build().Session;
        _pathfinder = new(new LevelDocument(256, 256).BakeGrid());
    }

    [Benchmark]
    public void EmptyTick() => _empty.Step(_none.AsSpan());

    [Benchmark]
    public void HeroAnd64StationaryEnemiesTick() => _actors.Step(_idle);

    [Benchmark]
    public PathResult AStar256Diagonal() => _pathfinder.FindPath(new(0, 0), new(255, 255), _path);
}
