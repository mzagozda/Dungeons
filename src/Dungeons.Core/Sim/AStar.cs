using Dungeons.Core.Math;

namespace Dungeons.Core.Sim;

/// <summary>Search outcome; a truncated path can still have reached its requested goal.</summary>
public readonly struct PathResult(int count, bool reachedGoal, bool truncated, float cost, IntVec2 end)
{
    /// <summary>Waypoints copied, excluding the start cell.</summary>
    public int Count { get; } = count;
    /// <summary>Search reached the requested goal rather than its closest reachable cell.</summary>
    public bool ReachedGoal { get; } = reachedGoal;
    /// <summary>Destination span was shorter than the complete route.</summary>
    public bool Truncated { get; } = truncated;
    /// <summary>Cost to the search's terminal cell, before truncation.</summary>
    public float Cost { get; } = cost;
    /// <summary>Closest/goal cell reached by the search.</summary>
    public IntVec2 End { get; } = end;
}

/// <summary>Reusable 8-connected weighted A* with a preallocated binary heap (§9.4).</summary>
public sealed class AStar
{
    private readonly NavGrid _grid;
    private readonly float[] _g, _h;
    private readonly int[] _parents, _heap, _heapPositions, _reverse;
    private readonly uint[] _visited, _closed;
    private uint _generation;
    private int _heapCount;
    private bool _lastExhaustive;
    private readonly bool[]? _discoveries;
    private static readonly IntVec2[] _neighbors = [new(0, -1), new(1, 0), new(0, 1), new(-1, 0), new(1, -1), new(1, 1), new(-1, 1), new(-1, -1)];

    /// <summary>Allocates bounded work arrays once for a grid.</summary>
    public AStar(NavGrid grid)
        : this(grid, null) { }
    // Optional caller-owned union of discovered cells. Validation uses one fixed
    // start and an immutable grid; a different start/grid requires a fresh buffer.
    internal AStar(NavGrid grid, bool[]? discoveries)
    {
        _grid = grid ?? throw new ArgumentNullException(nameof(grid));
        if (discoveries is not null && discoveries.Length != grid.Count) { throw new ArgumentException("Discovery buffer must match the grid.", nameof(discoveries)); }
        _discoveries = discoveries;
        _g = new float[grid.Count]; _h = new float[grid.Count]; _parents = new int[grid.Count];
        _heap = new int[grid.Count]; _heapPositions = new int[grid.Count]; _reverse = new int[grid.Count];
        _visited = new uint[grid.Count]; _closed = new uint[grid.Count];
    }
    /// <summary>Finds a path or the closest reachable endpoint. No allocation after construction.</summary>
    public PathResult FindPath(IntVec2 start, IntVec2 goal, Span<IntVec2> destination)
    {
        _lastExhaustive = false;
        if (!_grid.IsWalkable(start.X, start.Z)) { return new(0, false, false, 0, start); }
        unchecked { _generation++; }
        if (_generation == 0) { Array.Clear(_visited, 0, _visited.Length); Array.Clear(_closed, 0, _closed.Length); _generation = 1; }
        _heapCount = 0;
        int startIndex = start.Z * _grid.Width + start.X;
        Visit(startIndex, -1, 0, Heuristic(start, goal));
        int best = startIndex;
        bool reached = false;
        while (_heapCount > 0)
        {
            int current = Pop();
            _closed[current] = _generation;
            var position = Position(current);
            if (_h[current] < _h[best] || (_h[current] == _h[best] && (_g[current] < _g[best] || (_g[current] == _g[best] && current < best)))) { best = current; }
            if (position == goal) { best = current; reached = true; break; }
            for (int i = 0; i < _neighbors.Length; i++)
            {
                IntVec2 offset = _neighbors[i];
                var neighbor = new IntVec2(position.X + offset.X, position.Z + offset.Z);
                if (!_grid.CanStep(position, neighbor)) { continue; }
                int next = neighbor.Z * _grid.Width + neighbor.X;
                if (_closed[next] == _generation) { continue; }
                float cost = _g[current] + (i < 4 ? 1 : GameConstants.DiagonalCost) * _grid[next].MoveCost;
                if (_visited[next] != _generation) { Visit(next, current, cost, Heuristic(neighbor, goal)); }
                else if (cost < _g[next]) { _g[next] = cost; _parents[next] = current; SiftUp(_heapPositions[next]); }
            }
        }
        int length = 0;
        _lastExhaustive = !reached && _heapCount == 0;
        for (int node = best; node != startIndex; node = _parents[node]) { _reverse[length++] = node; }
        int count = System.Math.Min(length, destination.Length);
        for (int i = 0; i < count; i++) { destination[i] = Position(_reverse[length - 1 - i]); }
        return new(count, reached, count < length, _g[best], Position(best));
    }
    // A failed unbounded search has visited the complete component reachable from
    // its start. Validation can query other targets without running the same A* again.
    internal bool TryGetLastReachability(IntVec2 target, out bool reachable)
    {
        reachable = _lastExhaustive && _grid.Contains(target.X, target.Z) && _visited[target.Z * _grid.Width + target.X] == _generation;
        return _lastExhaustive;
    }
    private IntVec2 Position(int index) => new(index % _grid.Width, index / _grid.Width);
    private float Heuristic(IntVec2 a, IntVec2 b)
    {
        int dx = System.Math.Abs(a.X - b.X), dz = System.Math.Abs(a.Z - b.Z);
        return (System.Math.Max(dx, dz) + (GameConstants.DiagonalCost - 1) * System.Math.Min(dx, dz)) * _grid.MinimumMoveCost;
    }
    private void Visit(int node, int parent, float cost, float heuristic)
    {
        if (_discoveries is not null) { _discoveries[node] = true; }
        _visited[node] = _generation; _parents[node] = parent; _g[node] = cost; _h[node] = heuristic;
        _heapPositions[node] = _heapCount; _heap[_heapCount++] = node; SiftUp(_heapCount - 1);
    }
    private bool Less(int a, int b)
    {
        float fa = _g[a] + _h[a], fb = _g[b] + _h[b];
        return fa < fb || (fa == fb && (_h[a] < _h[b] || (_h[a] == _h[b] && a < b)));
    }
    private void SiftUp(int index)
    {
        while (index > 0)
        {
            int parent = (index - 1) / 2;
            if (!Less(_heap[index], _heap[parent])) { break; }
            Swap(index, parent); index = parent;
        }
    }
    private int Pop()
    {
        int result = _heap[0];
        _heapCount--;
        if (_heapCount == 0) { return result; }
        _heap[0] = _heap[_heapCount]; _heapPositions[_heap[0]] = 0;
        int index = 0;
        while (index * 2 + 1 < _heapCount)
        {
            int child = index * 2 + 1;
            if (child + 1 < _heapCount && Less(_heap[child + 1], _heap[child])) { child++; }
            if (!Less(_heap[child], _heap[index])) { break; }
            Swap(index, child); index = child;
        }
        return result;
    }
    private void Swap(int a, int b)
    {
        int node = _heap[a]; _heap[a] = _heap[b]; _heap[b] = node;
        _heapPositions[_heap[a]] = a; _heapPositions[_heap[b]] = b;
    }
}
