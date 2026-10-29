using Dungeons.Core.Math;

namespace Dungeons.Core.Sim;

// Shared load/runtime footprint geometry; positions identify the authored gate centre.
internal readonly struct GateArea
{
    internal GateArea(Vec2 position, int width, bool alongX)
    {
        X = (int)MathF.Floor(position.X - (alongX ? width / 2f : 0)); Z = (int)MathF.Floor(position.Z - (alongX ? 0 : width / 2f));
        Width = alongX ? width : 1; Depth = alongX ? 1 : width;
    }
    internal readonly int X, Z, Width, Depth;
    internal bool Contains(int x, int z) => x >= X && z >= Z && x < X + Width && z < Z + Depth;
    internal bool InBounds(NavGrid grid) => grid.Contains(X, Z) && grid.Contains(X + Width - 1, Z + Depth - 1);
    internal bool Overlaps(Vec2 position, float radius)
    {
        if (position.X >= X && position.X < X + Width && position.Z >= Z && position.Z < Z + Depth) { return true; }
        float dx = position.X - MathUtil.Clamp(position.X, X, X + Width), dz = position.Z - MathUtil.Clamp(position.Z, Z, Z + Depth);
        return dx * dx + dz * dz < radius * radius - GameConstants.CollisionTangencyEpsilon;
    }
}
