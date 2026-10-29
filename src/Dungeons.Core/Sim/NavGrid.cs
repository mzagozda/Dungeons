using Dungeons.Core.Math;
using Dungeons.Core.Util;

namespace Dungeons.Core.Sim;

/// <summary>Direction in which a stairs tile rises.</summary>
public enum StairsDirection : byte
{
    /// <summary>Flat terrain.</summary>
    None,
    /// <summary>Up toward negative Z.</summary>
    North,
    /// <summary>Up toward positive X.</summary>
    East,
    /// <summary>Up toward positive Z.</summary>
    South,
    /// <summary>Up toward negative X.</summary>
    West,
}

/// <summary>Baked terrain flags shared by movement, pathfinding and validation (§7.6).</summary>
public readonly struct Cell
{
    /// <summary>Creates a cell with validated height and traversal cost.</summary>
    public Cell(byte height = 0, bool walkable = true, bool solid = false, bool blocksProjectiles = false,
        bool blocksVision = false, StairsDirection stairsDir = StairsDirection.None, float moveCost = 1)
    {
        if (height > 15) { throw new ArgumentOutOfRangeException(nameof(height)); }
        if (!MathUtil.IsFinite(moveCost) || moveCost <= 0) { throw new ArgumentOutOfRangeException(nameof(moveCost)); }
        if (stairsDir > StairsDirection.West) { throw new ArgumentOutOfRangeException(nameof(stairsDir)); }
        Height = height; Walkable = walkable; Solid = solid; BlocksProjectiles = blocksProjectiles;
        BlocksVision = blocksVision; StairsDir = stairsDir; MoveCost = moveCost;
    }
    /// <summary>Flat walkable floor (default(Cell) intentionally represents void).</summary>
    public static Cell Floor => new(moveCost: 1);
    /// <summary>Fully blocking terrain.</summary>
    public static Cell Wall => new(walkable: false, solid: true, blocksProjectiles: true, blocksVision: true);
    /// <summary>Integer base height.</summary>
    public byte Height { get; }
    /// <summary>Supports ground movement.</summary>
    public bool Walkable { get; }
    /// <summary>Blocks physical movement.</summary>
    public bool Solid { get; }
    /// <summary>Blocks arrows.</summary>
    public bool BlocksProjectiles { get; }
    /// <summary>Blocks sight.</summary>
    public bool BlocksVision { get; }
    /// <summary>Uphill direction; none for flat terrain.</summary>
    public StairsDirection StairsDir { get; }
    /// <summary>Positive pathfinding cost multiplier.</summary>
    public float MoveCost { get; }
}

/// <summary>Immutable public view of baked terrain; runtime mutations are Core-internal.</summary>
public sealed class NavGrid
{
    private readonly Cell[] _cells;
    private Fnv1a _hashPrefix;
    private bool _hashDirty = true;
    // Terrain is the canonical first section of the world hash. Copying this value
    // resumes the exact FNV byte stream without rescanning unchanged cells each tick.
    internal Fnv1a HashPrefix
    {
        get
        {
            if (!_hashDirty) { return _hashPrefix; }
            var hash = new Fnv1a(); hash.Add(Width); hash.Add(Height);
            for (int i = 0; i < _cells.Length; i++)
            {
                Cell cell = _cells[i]; hash.Add(cell.Height); hash.Add(cell.Walkable); hash.Add(cell.Solid); hash.Add(cell.BlocksProjectiles);
                hash.Add(cell.BlocksVision); hash.Add((byte)cell.StairsDir); hash.Add(BitConverter.SingleToInt32Bits(cell.MoveCost));
            }
            _hashPrefix = hash; _hashDirty = false; return hash;
        }
    }
    /// <summary>Copies a row-major grid, preventing callers from mutating runtime terrain.</summary>
    public NavGrid(int width, int height, ReadOnlySpan<Cell> cells)
    {
        if (width <= 0 || width > GameConstants.MaxLevelSize) { throw new ArgumentOutOfRangeException(nameof(width)); }
        if (height <= 0 || height > GameConstants.MaxLevelSize) { throw new ArgumentOutOfRangeException(nameof(height)); }
        if (cells.Length != width * height) { throw new ArgumentException("Cell count must match dimensions.", nameof(cells)); }
        Width = width; Height = height; _cells = cells.ToArray();
        float minimum = float.MaxValue;
        for (int i = 0; i < _cells.Length; i++)
        {
            if (_cells[i].Walkable && !_cells[i].Solid) { minimum = MathF.Min(minimum, _cells[i].MoveCost); }
        }
        MinimumMoveCost = minimum == float.MaxValue ? 1 : minimum;
    }
    /// <summary>Column count.</summary>
    public int Width { get; }
    /// <summary>Row count.</summary>
    public int Height { get; }
    /// <summary>Cell count.</summary>
    public int Count => _cells.Length;
    /// <summary>Admissible heuristic cost lower bound.</summary>
    public float MinimumMoveCost { get; private set; }
    // Only Core systems may change terrain (for example a gate footprint). Recompute
    // the admissible heuristic bound so equal grid states retain equal A* behaviour.
    internal void SetRuntimeCell(int x, int z, Cell cell)
    {
        if (!Contains(x, z)) { throw new ArgumentOutOfRangeException(nameof(x)); }
        _cells[z * Width + x] = cell; _hashDirty = true; float minimum = float.MaxValue;
        for (int i = 0; i < _cells.Length; i++) { if (_cells[i].Walkable && !_cells[i].Solid) { minimum = MathF.Min(minimum, _cells[i].MoveCost); } }
        MinimumMoveCost = minimum == float.MaxValue ? 1 : minimum;
    }
    /// <summary>Reads a cell. Outside the grid is impassable void.</summary>
    public Cell this[int x, int z] => Contains(x, z) ? _cells[z * Width + x] : default;
    /// <summary>Reads a row-major cell.</summary>
    public Cell this[int index] => _cells[index];
    /// <summary>Whether a coordinate lies inside the grid.</summary>
    public bool Contains(int x, int z) => (uint)x < (uint)Width && (uint)z < (uint)Height;
    /// <summary>Checks a traversable centre cell.</summary>
    public bool IsWalkable(int x, int z)
    { if (!Contains(x, z)) { return false; } Cell cell = _cells[z * Width + x]; return cell.Walkable && !cell.Solid; }
    /// <summary>Single shared orthogonal/diagonal height and stairs rule (§7.6).</summary>
    public bool CanStep(IntVec2 from, IntVec2 to)
    {
        int dx = to.X - from.X, dz = to.Z - from.Z;
        if (System.Math.Abs(dx) > 1 || System.Math.Abs(dz) > 1 || !IsWalkable(from.X, from.Z) || !IsWalkable(to.X, to.Z)) { return false; }
        if (dx == 0 && dz == 0) { return true; }
        Cell a = _cells[from.Z * Width + from.X], b = _cells[to.Z * Width + to.X];
        if (dx != 0 && dz != 0)
        {
            // Both complete orthogonal routes must work, not merely the destination.
            // From/to are already inside, so the two rectangle corners are inside too.
            Cell horizontal = _cells[from.Z * Width + to.X], vertical = _cells[to.Z * Width + from.X];
            if (!horizontal.Walkable || horizontal.Solid || !vertical.Walkable || vertical.Solid) { return false; }
            if ((a.StairsDir | b.StairsDir | horizontal.StairsDir | vertical.StairsDir) == StairsDirection.None &&
                a.Height == b.Height && a.Height == horizontal.Height && a.Height == vertical.Height) { return true; }
            return CanStepOrthogonal(a, horizontal, dx, 0) && CanStepOrthogonal(a, vertical, 0, dz) &&
                CanStepOrthogonal(horizontal, b, 0, dz) && CanStepOrthogonal(vertical, b, dx, 0);
        }
        return CanStepOrthogonal(a, b, dx, dz);
    }
    private static bool CanStepOrthogonal(Cell a, Cell b, int dx, int dz)
    {
        if ((a.StairsDir | b.StairsDir) == StairsDirection.None) { return a.Height == b.Height; }
        if (!OnStairsAxis(a.StairsDir, dx, dz) || !OnStairsAxis(b.StairsDir, dx, dz)) { return false; }
        int difference = b.Height - a.Height;
        if (difference == 0) { return true; }
        if (difference == 1) { return Points(a.StairsDir, dx, dz); }
        if (difference == -1) { return Points(b.StairsDir, -dx, -dz); }
        return false;
    }
    /// <summary>Interpolated feet height along stairs, or flat cell height.</summary>
    public float HeightAt(Vec2 position)
    {
        IntVec2 cell = IntVec2.FromPosition(position);
        Cell terrain = this[cell.X, cell.Z];
        float x = position.X - cell.X, z = position.Z - cell.Z;
        float ramp = terrain.StairsDir switch
        {
            StairsDirection.North => 1 - z,
            StairsDirection.East => x,
            StairsDirection.South => z,
            StairsDirection.West => 1 - x,
            _ => 0,
        };
        return terrain.Height + ramp;
    }
    private static bool OnStairsAxis(StairsDirection direction, int dx, int dz) => direction switch
    {
        StairsDirection.East or StairsDirection.West => dz == 0,
        StairsDirection.North or StairsDirection.South => dx == 0,
        _ => true,
    };
    private static bool Points(StairsDirection direction, int dx, int dz) => direction switch
    {
        StairsDirection.North => dx == 0 && dz == -1,
        StairsDirection.East => dx == 1 && dz == 0,
        StairsDirection.South => dx == 0 && dz == 1,
        StairsDirection.West => dx == -1 && dz == 0,
        _ => false,
    };
}
