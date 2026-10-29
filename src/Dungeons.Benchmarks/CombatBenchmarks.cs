using BenchmarkDotNet.Attributes;
using Dungeons.Core.Sim;

namespace Dungeons.Benchmarks;

/// <summary>Finite combat windows keep the measured actors and shots active.</summary>
[MemoryDiagnoser]
public class CombatBenchmarks
{
    private GameSession _session = null!;
    [IterationSetup]
    public void Setup() { _session = CombatStressFixture.Create(); }
    [Benchmark(OperationsPerInvoke = 6000)]
    public ulong TickStress64Monsters32Arrows()
    {
        for (int tick = 0; tick < 6000; tick++)
        { _session.Step(CombatStressFixture.Input(tick)); _session.DrainEvents(); }
        return _session.Hash();
    }
}
