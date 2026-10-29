using Dungeons.Core.Math;

namespace Dungeons.Core.Sim;

/// <summary>Deterministic target selection for commands without an explicit aim direction.</summary>
public static class AutoAim
{
    // Interpolated 0.1-degree cosine table: no trigonometric calls in simulation ticks.
    private static readonly float[] Cosines = BuildCosines();
    private static float[] BuildCosines()
    { var values = new float[1801]; for (int i = 0; i < values.Length; i++) { values[i] = MathF.Cos(i * (MathF.PI / 1800)); } return values; }
    /// <summary>Returns the lowest angle-plus-distance score, preferring the forward 100-degree arc.</summary>
    public static Vec2 Resolve(World world, Entity hero, bool ranged, bool strong = false)
    {
        Entity? front = null, behind = null; float frontScore = float.MaxValue, behindScore = float.MaxValue;
        float range = ranged ? 12 : 2.5f;
        for (int i = 0; i < world.Entities.Count; i++)
        {
            Entity candidate = world.Entities[i];
            if (!candidate.Alive || candidate.Kind is not (EntityKind.Enemy or EntityKind.Player) || candidate.Def.Faction == hero.Def.Faction || candidate.Id == hero.Id) { continue; }
            Vec2 offset = candidate.Pos - hero.Pos; float distance = offset.Length;
            if (distance > range || !TerrainSight.Visible(world.Grid, hero.Pos, hero.Y + .9f, candidate.Pos, candidate.Y + .9f)) { continue; }
            float angle = distance == 0 ? 0 : Angle(Vec2.Dot(hero.Facing, offset / distance));
            float score = (strong ? angle * GameConstants.StrongAimAngleWeight : angle) + distance * 8;
            if (angle <= 100 && score < frontScore) { front = candidate; frontScore = score; }
            else if (angle > 100 && score < behindScore) { behind = candidate; behindScore = score; }
        }
        Entity? selected = front ?? behind;
        if (selected is null) { return hero.Facing; }
        Vec2 point = selected.Pos;
        if (ranged && hero.Def.Weapons.Ranged?.ProjectileId is string id)
        { float speed = world.ProjectileDefinition(id).Projectile!.Speed; point += selected.Vel * ((point - hero.Pos).Length / speed); }
        Vec2 aim = (point - hero.Pos).Normalized; return aim == Vec2.Zero ? hero.Facing : aim;
    }
    /// <summary>Snaps a mouse ground point only within 1.2 m of a visible hostile; otherwise preserves raw aim.</summary>
    public static Vec2 ResolveCursor(World world, Entity hero, Vec2 cursor, bool strong = false)
    {
        if (!cursor.IsFinite) { return hero.Facing; }
        float range = strong ? GameConstants.StrongCursorSnapRange : GameConstants.CursorSnapRange;
        Entity? nearest = null; float best = range * range;
        for (int i = 0; i < world.Entities.Count; i++)
        {
            Entity candidate = world.Entities[i];
            if (!candidate.Alive || candidate.Kind is not (EntityKind.Enemy or EntityKind.Player) || candidate.Def.Faction == hero.Def.Faction || candidate.Id == hero.Id || Vec2.DistanceSquared(candidate.Pos, hero.Pos) > 144) { continue; }
            float squared = Vec2.DistanceSquared(candidate.Pos, cursor);
            if (squared > best || nearest is not null && squared == best || !TerrainSight.Visible(world.Grid, hero.Pos, hero.Y + .9f, candidate.Pos, candidate.Y + .9f)) { continue; }
            nearest = candidate; best = squared;
        }
        if (nearest is not null)
        {
            cursor = nearest.Pos;
            if (hero.Def.Weapons.Ranged?.ProjectileId is string id) { cursor += nearest.Vel * ((cursor - hero.Pos).Length / world.ProjectileDefinition(id).Projectile!.Speed); }
        }
        Vec2 direction = (cursor - hero.Pos).Normalized; return direction == Vec2.Zero ? hero.Facing : direction;
    }
    private static float Angle(float cosine)
    {
        cosine = MathF.Max(-1, MathF.Min(1, cosine));
        int low = 0, high = 1800;
        while (high - low > 1) { int middle = (low + high) / 2; if (Cosines[middle] >= cosine) { low = middle; } else { high = middle; } }
        float gap = Cosines[low] - Cosines[high];
        return (low + (gap > 0 ? (Cosines[low] - cosine) / gap : 0)) * .1f;
    }
}

/// <summary>Supercover terrain DDA shared by deterministic targeting and later enemy perception.</summary>
public static class TerrainSight
{
    /// <summary>Tests the eye-to-eye segment against vision flags and terrain height.</summary>
    public static bool Visible(NavGrid grid, Vec2 from, float fromY, Vec2 to, float toY)
    {
        if (!from.IsFinite || !to.IsFinite || !MathUtil.IsFinite(fromY) || !MathUtil.IsFinite(toY)) { return false; }
        IntVec2 start = IntVec2.FromPosition(from), end = IntVec2.FromPosition(to);
        if (!grid.Contains(start.X, start.Z) || !grid.Contains(end.X, end.Z)) { return false; }
        Vec2 delta = to - from; int x = start.X, z = start.Z;
        int stepX = delta.X > 0 ? 1 : delta.X < 0 ? -1 : 0, stepZ = delta.Z > 0 ? 1 : delta.Z < 0 ? -1 : 0;
        float intervalX = stepX == 0 ? float.PositiveInfinity : MathF.Abs(1 / delta.X), intervalZ = stepZ == 0 ? float.PositiveInfinity : MathF.Abs(1 / delta.Z);
        float nextX = stepX == 0 ? float.PositiveInfinity : ((stepX > 0 ? x + 1 : x) - from.X) / delta.X;
        float nextZ = stepZ == 0 ? float.PositiveInfinity : ((stepZ > 0 ? z + 1 : z) - from.Z) / delta.Z;
        for (int steps = 0; steps <= grid.Width + grid.Height; steps++)
        {
            if (x == end.X && z == end.Z) { return true; }
            float t = MathF.Min(nextX, nextZ); if (t > 1) { return true; }
            float height = fromY + (toY - fromY) * t;
            if (nextX == nextZ)
            {
                if (Blocked(grid, x + stepX, z, height) || Blocked(grid, x, z + stepZ, height)) { return false; }
                x += stepX; z += stepZ; nextX += intervalX; nextZ += intervalZ;
            }
            else if (nextX < nextZ) { x += stepX; nextX += intervalX; }
            else { z += stepZ; nextZ += intervalZ; }
            if (Blocked(grid, x, z, height)) { return false; }
        }
        return false;
    }
    private static bool Blocked(NavGrid grid, int x, int z, float eyeHeight)
    { Cell cell = grid[x, z]; return !grid.Contains(x, z) || cell.BlocksVision || cell.Height >= eyeHeight; }
}
