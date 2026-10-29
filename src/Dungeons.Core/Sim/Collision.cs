using Dungeons.Core.Math;

namespace Dungeons.Core.Sim;

/// <summary>Allocation-free axis-separated circle/terrain collision using NavGrid passability.</summary>
public static class Collision
{
    /// <summary>Integrates velocity for one tick and zeros only blocked velocity components.</summary>
    public static Vec2 Move(NavGrid grid, Vec2 position, ref Vec2 velocity, float radius)
    {
        velocity = velocity.IsFinite ? velocity.ClampLength(GameConstants.MaxSpeed) : Vec2.Zero;
        Vec2 target = position + velocity * GameConstants.Dt;
        Vec2 resolved = Resolve(grid, position, target, radius);
        velocity = new(resolved.X != target.X ? 0 : velocity.X, resolved.Z != target.Z ? 0 : velocity.Z);
        return resolved;
    }
    /// <summary>Resolves a displacement, including separation pushes, against terrain.</summary>
    public static Vec2 Resolve(NavGrid grid, Vec2 from, Vec2 target, float radius)
    {
        if (!from.IsFinite || !target.IsFinite || !MathUtil.IsFinite(radius) || radius < 0) { throw new ArgumentOutOfRangeException(nameof(target)); }
        float x = ResolveAxis(grid, from, target.X, from.Z, radius, true);
        return new(x, ResolveAxis(grid, new(x, from.Z), target.Z, x, radius, false));
    }
    private static float ResolveAxis(NavGrid grid, Vec2 from, float candidate, float tangent, float radius, bool alongX)
    {
        float origin = alongX ? from.X : from.Z;
        if (candidate == origin) { return origin; }
        IntVec2 source = IntVec2.FromPosition(from);
        int minAxis = (int)MathF.Floor(MathF.Min(origin, candidate) - radius);
        int maxAxis = (int)MathF.Floor(MathF.Max(origin, candidate) + radius);
        int minTangent = (int)MathF.Floor(tangent - radius), maxTangent = (int)MathF.Floor(tangent + radius);
        // Swept axis range also makes large tooling/separation corrections safe.
        for (int axis = minAxis; axis <= maxAxis; axis++)
        {
            for (int side = minTangent; side <= maxTangent; side++)
            {
                int cx = alongX ? axis : side, cz = alongX ? side : axis;
                var cell = new IntVec2(cx, cz);
                if (cell == source || grid.CanStep(source, cell)) { continue; }
                float nearest = MathUtil.Clamp(tangent, side, side + 1);
                float distance = tangent - nearest;
                // A circle grazing a continuous wall must cross tile seams freely. Coordinates and
                // radius have different float rounding, so exact squared equality would snag there.
                if (distance * distance >= radius * radius - GameConstants.CollisionTangencyEpsilon && radius > 0) { continue; }
                float reach = MathF.Sqrt(MathF.Max(0, radius * radius - distance * distance));
                if (candidate > origin && origin <= axis && candidate > axis - reach) { candidate = MathF.Min(candidate, axis - reach); }
                else if (candidate < origin && origin >= axis + 1 && candidate < axis + 1 + reach) { candidate = MathF.Max(candidate, axis + 1 + reach); }
            }
        }
        return candidate;
    }
}
