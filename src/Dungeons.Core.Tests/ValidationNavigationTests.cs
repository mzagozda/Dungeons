using Dungeons.Core.Level;
using Dungeons.Core.Math;
using Dungeons.Core.Sim;
using Xunit;

namespace Dungeons.Core.Tests;

[Collection("Performance")]
public sealed class ValidationNavigationTests
{
    [Fact]
    public void OptimizedStepChecksMatchThePreviousRecursiveRuleAcrossEveryRepresentativeSquare()
    {
        var choices = new List<Cell>();
        foreach (byte height in new byte[] { 0, 1 }) { foreach (StairsDirection direction in Enum.GetValues<StairsDirection>()) { choices.Add(new(height, stairsDir: direction)); } }
        choices.Add(Cell.Wall); choices.Add(new(walkable: false)); choices.Add(new(solid: true)); choices.Add(new(3));
        IntVec2[] points = [new(0, 0), new(1, 0), new(0, 1), new(1, 1)]; var cells = new Cell[4];
        foreach (Cell a in choices) { foreach (Cell b in choices) { foreach (Cell c in choices) { foreach (Cell d in choices)
        {
            cells[0] = a; cells[1] = b; cells[2] = c; cells[3] = d; var grid = new NavGrid(2, 2, cells);
            foreach (IntVec2 from in points) { foreach (IntVec2 to in points) { Assert.Equal(Reference(from, to), grid.CanStep(from, to)); } }
            bool Reference(IntVec2 from, IntVec2 to)
            {
                bool Walk(IntVec2 p) => p.X >= 0 && p.X < 2 && p.Z >= 0 && p.Z < 2 && cells[p.Z * 2 + p.X].Walkable && !cells[p.Z * 2 + p.X].Solid;
                int dx = to.X - from.X, dz = to.Z - from.Z;
                if (System.Math.Abs(dx) > 1 || System.Math.Abs(dz) > 1 || !Walk(from) || !Walk(to)) { return false; }
                if (dx == 0 && dz == 0) { return true; }
                if (dx != 0 && dz != 0) { IntVec2 h = new(to.X, from.Z), v = new(from.X, to.Z); return Reference(from, h) && Reference(from, v) && Reference(h, to) && Reference(v, to); }
                Cell start = cells[from.Z * 2 + from.X], end = cells[to.Z * 2 + to.X];
                bool Axis(StairsDirection direction) => direction == StairsDirection.None || (direction is StairsDirection.North or StairsDirection.South ? dx == 0 : dz == 0);
                bool Up(StairsDirection direction, int x, int z) => direction switch { StairsDirection.North => z == -1, StairsDirection.East => x == 1, StairsDirection.South => z == 1, StairsDirection.West => x == -1, _ => false };
                if (!Axis(start.StairsDir) || !Axis(end.StairsDir)) { return false; }
                int change = end.Height - start.Height; return change == 0 || change == 1 && Up(start.StairsDir, dx, dz) || change == -1 && Up(end.StairsDir, -dx, -dz);
            }
        } } } }
        var edge = new LevelDocument(2, 2).BakeGrid(); Assert.False(edge.CanStep(new(-1, 0), new(0, 0))); Assert.False(edge.IsWalkable(2, 1));
    }

    [Fact]
    public void DiscoveredAndExhaustiveSearchEvidenceMatchesAnIndependentConnectivityOracle()
    {
        var random = new Random(661);
        for (int map = 0; map < 32; map++)
        {
            var builder = new ScenarioBuilder(8, 8);
            for (int z = 0; z < 8; z++) { for (int x = 0; x < 8; x++) { if (x == 0 && z == 0) { continue; } builder.Cell(x, z, random.Next(5) == 0 ? Cell.Wall : new((byte)random.Next(2), stairsDir: (StairsDirection)random.Next(5))); } }
            NavGrid grid = builder.Document.BakeGrid(); bool[] expected = Flood(grid), discoveries = new bool[64]; var pathfinder = new AStar(grid, discoveries); var path = new IntVec2[64];
            Assert.Throws<ArgumentException>(() => new AStar(grid, new bool[1]));
            for (int i = 0; i < 64; i++)
            {
                var point = new IntVec2(i % 8, i / 8);
                bool reached = discoveries[i] || (pathfinder.TryGetLastReachability(point, out bool known) ? known : pathfinder.FindPath(new(0, 0), point, path).ReachedGoal);
                Assert.Equal(expected[i], reached);
            }
            Assert.False(pathfinder.FindPath(new(0, 0), new(-1, -1), path).ReachedGoal);
            for (int i = 0; i < 64; i++) { Assert.True(pathfinder.TryGetLastReachability(new(i % 8, i / 8), out bool reached)); Assert.Equal(expected[i], reached); Assert.Equal(expected[i], discoveries[i]); }
            Assert.True(pathfinder.FindPath(new(0, 0), new(0, 0), path).ReachedGoal); Assert.False(pathfinder.TryGetLastReachability(new(0, 0), out _));
            pathfinder.FindPath(new(-1, -1), new(0, 0), path); Assert.False(pathfinder.TryGetLastReachability(new(0, 0), out _));
        }
        static bool[] Flood(NavGrid grid)
        {
            var visited = new bool[grid.Count]; var queue = new Queue<IntVec2>(); queue.Enqueue(new(0, 0)); visited[0] = true;
            while (queue.TryDequeue(out IntVec2 current))
            {
                for (int z = -1; z <= 1; z++) { for (int x = -1; x <= 1; x++)
                { IntVec2 next = new(current.X + x, current.Z + z); if (!grid.CanStep(current, next)) { continue; } int index = next.Z * grid.Width + next.X; if (!visited[index]) { visited[index] = true; queue.Enqueue(next); } } }
            }
            return visited;
        }
    }
}
