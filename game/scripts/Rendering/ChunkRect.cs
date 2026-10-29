using Dungeons.Core.Level;

namespace Dungeons.Game.Rendering;

/// <summary>Half-open rectangle of 16×16 render chunks.</summary>
public readonly struct ChunkRect
{
    /// <summary>Creates a rectangle; negative origins are clipped by the view.</summary>
    public ChunkRect(int x, int z, int width, int height)
    {
        if (width < 0) { throw new ArgumentOutOfRangeException(nameof(width)); }
        if (height < 0) { throw new ArgumentOutOfRangeException(nameof(height)); }
        X = x; Z = z; Width = width; Height = height;
    }
    public int X { get; }
    public int Z { get; }
    public int Width { get; }
    public int Height { get; }
    public bool IsEmpty => Width == 0 || Height == 0;
    /// <summary>Computes affected chunks from edited cells, including the one-cell cliff/AO dependency halo.</summary>
    public static ChunkRect FromCells(CellRect cells, int mapWidth, int mapHeight)
    {
        if (mapWidth <= 0 || mapHeight <= 0) { throw new ArgumentOutOfRangeException(nameof(mapWidth)); }
        if (cells.IsEmpty || cells.X >= mapWidth || cells.Z >= mapHeight || (long)cells.X + cells.Width <= 0 || (long)cells.Z + cells.Height <= 0) { return default; }
        int x = (int)System.Math.Clamp((long)cells.X - 1, 0, mapWidth), z = (int)System.Math.Clamp((long)cells.Z - 1, 0, mapHeight);
        int right = (int)System.Math.Clamp((long)cells.X + cells.Width + 1, 0, mapWidth), bottom = (int)System.Math.Clamp((long)cells.Z + cells.Height + 1, 0, mapHeight);
        int cx = x / ChunkMesher.ChunkSize, cz = z / ChunkMesher.ChunkSize;
        return new(cx, cz, (right - 1) / ChunkMesher.ChunkSize - cx + 1, (bottom - 1) / ChunkMesher.ChunkSize - cz + 1);
    }
    /// <summary>Clips against a map's chunk grid without integer overflow.</summary>
    public ChunkRect Clip(int columns, int rows)
    {
        if (columns < 0 || rows < 0) { throw new ArgumentOutOfRangeException(nameof(columns)); }
        if (IsEmpty) { return default; }
        int x = (int)System.Math.Clamp((long)X, 0, columns), z = (int)System.Math.Clamp((long)Z, 0, rows);
        int right = (int)System.Math.Clamp((long)X + Width, 0, columns), bottom = (int)System.Math.Clamp((long)Z + Height, 0, rows);
        return new(x, z, right - x, bottom - z);
    }
}
