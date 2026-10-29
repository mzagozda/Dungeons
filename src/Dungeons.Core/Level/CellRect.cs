namespace Dungeons.Core.Level;

/// <summary>Half-open rectangle in level cells; negative origins support brushes crossing map edges.</summary>
public readonly struct CellRect
{
    /// <summary>Creates a rectangle with nonnegative dimensions.</summary>
    public CellRect(int x, int z, int width, int height)
    {
        if (width < 0) { throw new ArgumentOutOfRangeException(nameof(width)); }
        if (height < 0) { throw new ArgumentOutOfRangeException(nameof(height)); }
        X = x; Z = z; Width = width; Height = height;
    }
    /// <summary>Western cell.</summary>
    public int X { get; }
    /// <summary>Northern cell.</summary>
    public int Z { get; }
    /// <summary>Column count.</summary>
    public int Width { get; }
    /// <summary>Row count.</summary>
    public int Height { get; }
    /// <summary>Whether the rectangle contains no cells.</summary>
    public bool IsEmpty => Width == 0 || Height == 0;
    /// <summary>Tests containment without overflowing at extreme brush coordinates.</summary>
    public bool Contains(int x, int z) => x >= X && z >= Z && (long)x < (long)X + Width && (long)z < (long)Z + Height;
    internal CellRect Clip(int width, int height, int border = 0)
    {
        if (IsEmpty) { return default; }
        int x = (int)System.Math.Clamp((long)X - border, 0, width), z = (int)System.Math.Clamp((long)Z - border, 0, height);
        int right = (int)System.Math.Clamp((long)X + Width + border, 0, width), bottom = (int)System.Math.Clamp((long)Z + Height + border, 0, height);
        return new(x, z, right - x, bottom - z);
    }
}
