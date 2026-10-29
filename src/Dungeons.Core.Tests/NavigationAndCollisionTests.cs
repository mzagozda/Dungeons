using System.Diagnostics;
using Dungeons.Core.Math;
using Dungeons.Core.Sim;
using Xunit;

namespace Dungeons.Core.Tests;

[Collection("Performance")]
public sealed class NavigationAndCollisionTests
{
    [Theory]
    [InlineData(1, 0)] [InlineData(-1, 0)] [InlineData(0, 1)] [InlineData(0, -1)]
    [InlineData(1, 1)] [InlineData(-1, 1)] [InlineData(1, -1)] [InlineData(-1, -1)]
    public void CircleAgainstOneCellFromEightDirections(float x, float z)
    {
        NavGrid grid = new ScenarioBuilder(7, 7).Cell(3, 3, Cell.Wall).Document.BakeGrid();
        Vec2 direction = new Vec2(x, z).Normalized;
        Vec2 start = new Vec2(3.5f, 3.5f) + direction * (0.5f / MathF.Max(MathF.Abs(direction.X), MathF.Abs(direction.Z)) + 0.36f);
        Vec2 velocity = -direction * GameConstants.MaxSpeed;
        Vec2 result = Collision.Move(grid, start, ref velocity, 0.35f);
        var nearest = new Vec2(MathUtil.Clamp(result.X, 3, 4), MathUtil.Clamp(result.Z, 3, 4));
        Assert.True((result - nearest).Length >= 0.3499f);
        Assert.True(velocity.X == 0 || velocity.Z == 0);
    }

    [Fact]
    public void NoTunnellingAtSpeedCapOrAcrossGridEdges()
    {
        var builder = new ScenarioBuilder(8, 8);
        for (int z = 0; z < 8; z++) { builder.Cell(4, z, Cell.Wall); }
        NavGrid grid = builder.Document.BakeGrid(); Vec2 position = new(2.5f, 3.5f);
        for (int i = 0; i < 300; i++) { Vec2 velocity = new(1000, 0); position = Collision.Move(grid, position, ref velocity, 0.35f); }
        Assert.InRange(position.X, 3.6499f, 3.6501f);
        for (int i = 0; i < 300; i++) { Vec2 velocity = new(-12, -12); position = Collision.Move(grid, position, ref velocity, 0.35f); }
        Assert.InRange(position.X, 0.3499f, 0.3501f); Assert.InRange(position.Z, 0.3499f, 0.3501f);
        Vec2 invalid = new(float.NaN, 1); Assert.Equal(position, Collision.Move(grid, position, ref invalid, 0.35f));
        Assert.Throws<ArgumentOutOfRangeException>(() => Collision.Resolve(grid, default, new Vec2(float.NaN, 0), 0.35f));
    }

    [Fact]
    public void StairsOnlyConnectLowAndHighEndsAndRejectSidewaysPushes()
    {
        foreach (StairsDirection direction in new[] { StairsDirection.North, StairsDirection.East, StairsDirection.South, StairsDirection.West })
        {
            var builder = new ScenarioBuilder(5, 5).Cell(2, 2, new Cell(stairsDir: direction));
            IntVec2 up = direction switch { StairsDirection.North => new(0, -1), StairsDirection.East => new(1, 0), StairsDirection.South => new(0, 1), _ => new(-1, 0) };
            var higher = new IntVec2(2 + up.X, 2 + up.Z);
            builder.Cell(higher.X, higher.Z, new Cell(height: 1));
            NavGrid grid = builder.Document.BakeGrid();
            Assert.True(grid.CanStep(new IntVec2(2, 2), higher)); Assert.True(grid.CanStep(higher, new IntVec2(2, 2)));
            var side = new IntVec2(2 + up.Z, 2 - up.X);
            Assert.False(grid.CanStep(side, new IntVec2(2, 2)));
            Vec2 result = Collision.Resolve(grid, side.Center, new IntVec2(2, 2).Center, 0.35f);
            Assert.NotEqual(new IntVec2(2, 2), IntVec2.FromPosition(result));
            Assert.Equal(0.5f, grid.HeightAt(new Vec2(2.5f, 2.5f)));
        }
        var step = new ScenarioBuilder(3, 3).Cell(1, 1, new Cell(height: 1)).Document.BakeGrid();
        Assert.False(step.CanStep(new IntVec2(0, 1), new IntVec2(1, 1)));
    }

    [Fact]
    public void NavigationRejectsCornerCuttingWaterAndOutOfBounds()
    {
        NavGrid grid = ScenarioBuilder.FromAscii(".#.\n#..\n.~.").Document.BakeGrid();
        Assert.False(grid.CanStep(new IntVec2(0, 0), new IntVec2(1, 1)));
        Assert.False(grid.CanStep(new IntVec2(1, 1), new IntVec2(1, 2)));
        Assert.False(grid.IsWalkable(-1, 0)); Assert.False(grid.Contains(0, 3));
        Assert.False(grid.CanStep(new IntVec2(1, 1), new IntVec2(1, 3)));
        Assert.True(grid.CanStep(new IntVec2(1, 1), new IntVec2(1, 1)));
        var pathfinder = new AStar(grid); var path = new IntVec2[9];
        PathResult trapped = pathfinder.FindPath(new IntVec2(0, 0), new IntVec2(2, 2), path);
        Assert.False(trapped.ReachedGoal); Assert.Equal(0, trapped.Count); Assert.Equal(new IntVec2(0, 0), trapped.End);
        Assert.Equal(0, pathfinder.FindPath(new IntVec2(1, 0), new IntVec2(2, 2), path).Count);
    }

    [Fact]
    public void AStarKnownRoutesTruncationAndClosestEndpoint()
    {
        NavGrid open = new ScenarioBuilder(10, 10).Document.BakeGrid(); var pathfinder = new AStar(open);
        var path = new IntVec2[100];
        PathResult straight = pathfinder.FindPath(new IntVec2(1, 1), new IntVec2(5, 1), path);
        Assert.True(straight.ReachedGoal); Assert.Equal(4, straight.Count); Assert.Equal(4, straight.Cost);
        Assert.Equal(new IntVec2(2, 1), path[0]); Assert.Equal(new IntVec2(5, 1), path[3]);
        PathResult shortBuffer = pathfinder.FindPath(new IntVec2(1, 1), new IntVec2(5, 1), path.AsSpan(0, 2));
        Assert.True(shortBuffer.Truncated); Assert.True(shortBuffer.ReachedGoal); Assert.Equal(2, shortBuffer.Count);
        Assert.Equal(0, pathfinder.FindPath(new IntVec2(1, 1), new IntVec2(1, 1), path).Count);
        NavGrid blocked = ScenarioBuilder.FromAscii(".....\n..#..\n..#..\n..#..\n.....").Document.BakeGrid();
        PathResult detour = new AStar(blocked).FindPath(new IntVec2(0, 2), new IntVec2(4, 2), path);
        Assert.True(detour.ReachedGoal); Assert.InRange(detour.Cost, 6.8283f, 6.8285f);
        NavGrid partition = ScenarioBuilder.FromAscii("..#..\n..#..\n..#..").Document.BakeGrid();
        PathResult closest = new AStar(partition).FindPath(new IntVec2(0, 1), new IntVec2(4, 1), path);
        Assert.False(closest.ReachedGoal); Assert.Equal(new IntVec2(1, 1), closest.End);
    }

    [Fact]
    public void WeightedRoadRoutesMatchIndependentDijkstraOracle()
    {
        var random = new Random(2301);
        for (int trial = 0; trial < 80; trial++)
        {
            var builder = new ScenarioBuilder(12, 12);
            for (int z = 0; z < 12; z++)
            {
                for (int x = 0; x < 12; x++) { builder.Cell(x, z, random.Next(5) == 0 ? Cell.Wall : new Cell(moveCost: random.Next(2) == 0 ? 0.9f : 2)); }
            }
            builder.Cell(0, 0, Cell.Floor).Cell(11, 11, Cell.Floor);
            NavGrid grid = builder.Document.BakeGrid();
            float expected = Dijkstra(grid, new(0, 0), new(11, 11));
            PathResult actual = new AStar(grid).FindPath(new IntVec2(0, 0), new IntVec2(11, 11), new IntVec2[144]);
            Assert.Equal(!float.IsPositiveInfinity(expected), actual.ReachedGoal);
            if (actual.ReachedGoal) { Assert.InRange(MathF.Abs(actual.Cost - expected), 0, 0.0001f); }
        }
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void ReusedLargeGridSearchMeetsTwoMillisecondSmokeBudgetAndAllocatesNothing()
    {
        var pathfinder = new AStar(new ScenarioBuilder(256, 256).Document.BakeGrid());
        var path = new IntVec2[256]; var start = new IntVec2(0, 0); var end = new IntVec2(255, 255);
        for (int i = 0; i < 100; i++) { pathfinder.FindPath(start, end, path); }
        long before = GC.GetAllocatedBytesForCurrentThread(); long timer = Stopwatch.GetTimestamp();
        for (int i = 0; i < 100; i++) { pathfinder.FindPath(start, end, path); }
        double milliseconds = (Stopwatch.GetTimestamp() - timer) * 1000d / Stopwatch.Frequency / 100;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated); Assert.True(milliseconds <= 2, $"A* averaged {milliseconds:F3} ms per 256x256 query.");
    }

    private static float Dijkstra(NavGrid grid, IntVec2 start, IntVec2 goal)
    {
        var costs = new float[grid.Count]; Array.Fill(costs, float.PositiveInfinity);
        var queue = new PriorityQueue<IntVec2, float>(); costs[start.Z * grid.Width + start.X] = 0; queue.Enqueue(start, 0);
        while (queue.TryDequeue(out IntVec2 current, out float priority))
        {
            if (priority != costs[current.Z * grid.Width + current.X]) { continue; }
            if (current == goal) { return priority; }
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0) { continue; }
                    var next = new IntVec2(current.X + dx, current.Z + dz);
                    if (!grid.CanStep(current, next)) { continue; }
                    float candidate = priority + (dx != 0 && dz != 0 ? 1.41421f : 1) * grid[next.X, next.Z].MoveCost;
                    int index = next.Z * grid.Width + next.X;
                    if (candidate >= costs[index]) { continue; }
                    costs[index] = candidate; queue.Enqueue(next, candidate);
                }
            }
        }
        return float.PositiveInfinity;
    }
}
