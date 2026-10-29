using System.Text.Json;
using Dungeons.Benchmarks;
using Dungeons.Core.Content;
using Dungeons.Core.Math;
using Dungeons.Core.Sim;
using Dungeons.Tools;
using Xunit;

namespace Dungeons.Core.Tests;

public sealed class SimulationFuzzTests
{
    [Theory]
    [InlineData(0, 8, 1)]
    [InlineData(1, 16, 2)]
    [InlineData(2, 32, 3)]
    [InlineData(3, 64, 4)]
    public void GeneratedMapsRunTenThousandRealTicksTwiceAndRetainExactPlans(int variant, int size, int players)
    {
        ContentRegistry content = NightlyBenchmarkData.Content(NightlyBenchmarkData.Root());
        // Variant 1 is the real failing seed: tick 3638 originally scattered a drop
        // .199m beyond the map edge. Keep it as an independent regression fixture.
        ulong seed = SimulationFuzz.CaseSeed(SimulationFuzz.DefaultSeed, variant * 2 + 1);
        var level = SimulationFuzz.Generate(content, seed, variant);
        Assert.Equal(size, level.Width); Assert.Equal(size, level.Height);
        string folder = Path.Combine(Path.GetTempPath(), "dungeons-sim-fuzz-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            SimulationFuzz.CaseResult report = SimulationFuzz.RunCase(level, content, seed, 10000, variant, "generated", folder);
            Assert.Equal(players, report.Players); Assert.Equal(10000, report.ActualTicksPerRun); Assert.True(report.EntityChecks >= 20000 * players);
            using JsonDocument plan = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "plan.json")));
            foreach (JsonElement stream in plan.RootElement.GetProperty("inputs").EnumerateArray())
            { Assert.Equal(10000, stream.EnumerateArray().Sum(run => run.GetProperty("Count").GetInt32())); }
            Assert.False(File.Exists(Path.Combine(folder, "failure.json")));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Theory]
    [InlineData(9)]
    [InlineData(19)]
    [InlineData(29)]
    public void GeneratedGateMapsExerciseClosingTransitionsAndArchiveExactAuthoredBytes(int index)
    {
        ContentRegistry content = NightlyBenchmarkData.Content(NightlyBenchmarkData.Root());
        var template = Dungeons.Core.Level.LevelLoader.Load(File.ReadAllText(Path.Combine(NightlyBenchmarkData.Root(), "game/levels/mossy_hollow/level.json")), content).Document!;
        ulong seed = SimulationFuzz.CaseSeed(SimulationFuzz.DefaultSeed, index);
        var level = SimulationFuzz.GenerateGates(template, content, seed, index / 2);
        string folder = Path.Combine(Path.GetTempPath(), "dungeons-gate-fuzz-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            SimulationFuzz.CaseResult result = SimulationFuzz.RunCase(level, content, seed, 10000, index, "generated-gates", folder);
            Assert.True(result.GateClosures > 0); Assert.Equal(10000, result.ActualTicksPerRun);
            string source = File.ReadAllText(Path.Combine(folder, "level.json"));
            var loaded = Dungeons.Core.Level.LevelLoader.Load(source, content);
            Assert.True(loaded.Success); Assert.Equal(level.SourceHash, loaded.Document!.SourceHash);
            Assert.Null(SimulationFuzz.Inspect(GameSession.Create(loaded.Document, content, seed).World));
        }
        finally
        {
            Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase);
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void TerminalMissionsRestartIntoRecordedEpochsInsteadOfCountingNoOpCalls()
    {
        ContentRegistry content = NightlyBenchmarkData.Content(NightlyBenchmarkData.Root());
        var hero = new EntityDef("test:fragile", EntityKind.Player, new(1, 0));
        var enemy = new EntityDef("test:lethal", EntityKind.Enemy, new(int.MaxValue, 0),
            weapons: new(new WeaponDef("test:strike", new MeleeSwing[] { new(1, 1, 1, 10000, 0, 180, 8) })),
            brain: new(BrainKind.MeleeChaser, 20, 20, triggerDistance: 10));
        var level = new ScenarioBuilder(8, 8).Entity(hero, new(2.5f, 2.5f)).Entity(enemy, new(4.5f, 2.5f)).Document;
        string folder = Path.Combine(Path.GetTempPath(), "dungeons-fuzz-epochs-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            SimulationFuzz.CaseResult report = SimulationFuzz.RunCase(level, content, 51, 2000, 0, "terminal-contract", folder);
            Assert.Equal(2000, report.ActualTicksPerRun); Assert.True(report.Epochs >= 2);
            using JsonDocument epochs = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "epochs.json")));
            Assert.Equal(report.Epochs, epochs.RootElement.GetArrayLength());
            Assert.InRange(epochs.RootElement[1].GetProperty("Step").GetInt32(), 1, 1999);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void OracleDetectsCorruptTransformsBoundsHealthAndActualCirclePenetration()
    {
        var definition = new EntityDef("test:hero", EntityKind.Player, new(100, 4));
        GameSession session = new ScenarioBuilder(8, 8).Cell(3, 2, Cell.Wall).Entity(definition, new(2.5f, 2.5f)).Build().Session;
        Entity hero = session.World.Players[0]; Assert.Null(SimulationFuzz.Inspect(session.World));
        hero.Pos = new(float.NaN, 2); Assert.Equal("finite", SimulationFuzz.Inspect(session.World)!.Rule);
        hero.Pos = new(-.1f, 2); Assert.Equal("bounds", SimulationFuzz.Inspect(session.World)!.Rule);
        hero.Pos = new(2.5f, 2.5f); hero.HealthData.Current = -1; Assert.Equal("health", SimulationFuzz.Inspect(session.World)!.Rule);
        hero.HealthData.Current = 100; hero.Pos = new(3 - hero.Radius + .002f, 2.5f); Assert.Equal("solid", SimulationFuzz.Inspect(session.World)!.Rule);
        hero.Pos = new(3 - hero.Radius + .0005f, 2.5f); Assert.Null(SimulationFuzz.Inspect(session.World));
        hero.Pos = new(2.5f, 2.5f); hero.PrevY = float.PositiveInfinity; Assert.Equal("finite", SimulationFuzz.Inspect(session.World)!.Rule);
    }

    [Fact]
    public void RandomInputsAreSanitizedRepeatableAndOnlyPressNewlyHeldActions()
    {
        PlayerInput[][] first = SimulationFuzz.Inputs(71, 10000, 4), second = SimulationFuzz.Inputs(71, 10000, 4);
        for (int player = 0; player < 4; player++)
        {
            InputButtons held = InputButtons.None; int actions = 0, directions = 0;
            for (int tick = 0; tick < 10000; tick++)
            {
                PlayerInput input = first[player][tick]; Assert.Equal(input, second[player][tick]);
                Assert.True(input.Move.IsFinite && input.Aim.IsFinite); Assert.InRange(input.Move.LengthSquared, 0, 1.000001f);
                Assert.Equal(input.Held & ~held, input.Pressed); held = input.Held; actions |= (int)input.Pressed;
                if (input.Move.X > 0) { directions |= 1; } if (input.Move.X < 0) { directions |= 2; }
                if (input.Move.Z > 0) { directions |= 4; } if (input.Move.Z < 0) { directions |= 8; }
            }
            Assert.Equal(31, actions); Assert.Equal(15, directions);
        }
    }

    [Fact]
    public void InvalidArgumentsAndExistingEvidenceNeverOverwriteUserFiles()
    {
        ContentRegistry content = NightlyBenchmarkData.Content(NightlyBenchmarkData.Root()); var level = SimulationFuzz.Generate(content, 17, 0);
        string folder = Path.Combine(Path.GetTempPath(), "dungeons-fuzz-boundary-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        string sentinel = Path.Combine(folder, "keep.txt"); File.WriteAllText(sentinel, "unchanged");
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => SimulationFuzz.Run(level, content, folder, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => SimulationFuzz.Run(level, content, folder, ticks: 0));
            Assert.Throws<IOException>(() => SimulationFuzz.Run(level, content, folder));
            Assert.Equal("unchanged", File.ReadAllText(sentinel)); Assert.Single(Directory.GetFiles(folder));
        }
        finally { Directory.Delete(folder, true); }
    }
}
