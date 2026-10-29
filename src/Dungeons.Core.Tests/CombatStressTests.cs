using System.Diagnostics;
using Dungeons.Benchmarks;
using Dungeons.Core.Sim;
using Xunit;
using Xunit.Abstractions;

namespace Dungeons.Core.Tests;

[Collection("Performance")]
public sealed class CombatStressTests(ITestOutputHelper output)
{
    [Fact, Trait("Category", "Performance")]
    public void Active64BrainCombatFieldMeetsTickBudgetAndAllocatesNothing()
    {
        GameSession session = CombatStressFixture.Create(); Assert.Equal(32, session.World.Entities.Count(e => e.Kind == EntityKind.Projectile));
        Assert.Equal(128, session.World.Entities.Count(e => e.Kind == EntityKind.Pickup));
        for (int tick = 0; tick < 400; tick++) { session.Step(CombatStressFixture.Input(tick)); session.DrainEvents(); }
        long before = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp(); int shots = 0;
        for (int tick = 400; tick < 1000; tick++)
        {
            session.Step(CombatStressFixture.Input(tick));
            foreach (SimEvent ev in session.DrainEvents()) { if (ev.Kind == SimEventKind.ProjectileSpawned) { shots++; } }
            session.Hash();
        }
        double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds / 600; long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"64-brain combat with hashing: {ms:F4} ms/tick; {allocated} allocated bytes; {shots} arrows fired.");
        Assert.Equal(0, allocated); Assert.Equal(64, session.World.LiveMonsterCount); Assert.True(shots > 0);
        Assert.True(ms <= 1.5, $"Active combat averaged {ms:F4} ms/tick (local CPU budget 1.5 ms).");
    }

    [Fact, Trait("Category", "Performance")]
    public void Queued64ChaserPathsStayBelowOneMillisecondPerServiceTick()
    {
        GameSession session = CombatStressFixture.Create();
        for (int tick = 0; tick < 30; tick++) { session.Step(CombatStressFixture.Input(tick)); session.DrainEvents(); }
        Entity[] enemies = session.World.Entities.Where(e => e.Kind == EntityKind.Enemy).ToArray();
        Dungeons.Core.Math.IntVec2 target = Dungeons.Core.Math.IntVec2.FromPosition(session.World.Players[0].Pos);
        void Round()
        {
            foreach (Entity enemy in enemies) { Assert.True(session.World.EnqueuePath(enemy.Id, target)); }
            for (int tick = 0; tick < 4; tick++) { session.World.ServePaths(); }
        }
        for (int i = 0; i < 20; i++) { Round(); }
        long start = Stopwatch.GetTimestamp(); for (int i = 0; i < 100; i++) { Round(); }
        double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds / 400;
        output.WriteLine($"64 queued chaser routes on 256x256 field: {ms:F4} ms/service tick (20-request cap).");
        Assert.True(ms < 1, $"Path service averaged {ms:F4} ms/tick (local CPU budget 1 ms).");
    }
}
