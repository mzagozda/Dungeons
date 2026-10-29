using Dungeons.Core.Content;
using Dungeons.Core.Math;

namespace Dungeons.Core.Sim;

public sealed partial class GameSession
{
    private void ProjectileSystem()
    {
        for (int i = 0; i < World.Active.Count; i++)
        {
            Entity shot = World.Active[i];
            if (shot.Kind != EntityKind.Projectile || !shot.Alive || shot.LifetimeTicks == 0 || shot.Def.Projectile is not ProjectileSpec specification) { continue; }
            Vec2 displacement = shot.ProjectileData.Velocity * (GameConstants.Dt / 4);
            for (int part = 0; part < 4 && shot.Alive; part++)
            {
                Vec2 from = shot.Pos, to = from + displacement; float earliest = float.PositiveInfinity; Entity? victim = null;
                IntVec2 hitCell = default;
                int minX = BoundCell(MathF.Min(from.X, to.X) - shot.Radius, World.Grid.Width), maxX = BoundCell(MathF.Max(from.X, to.X) + shot.Radius, World.Grid.Width);
                int minZ = BoundCell(MathF.Min(from.Z, to.Z) - shot.Radius, World.Grid.Height), maxZ = BoundCell(MathF.Max(from.Z, to.Z) + shot.Radius, World.Grid.Height);
                for (int z = minZ; z <= maxZ; z++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        Cell cell = World.Grid[x, z];
                        if (World.Grid.Contains(x, z) && !cell.BlocksProjectiles && cell.Height < shot.Y) { continue; }
                        float impact = SweepCell(from, displacement, x, z, shot.Radius);
                        if (impact < earliest) { earliest = impact; hitCell = new(x, z); }
                    }
                }
                for (int targetIndex = 0; targetIndex < World.Active.Count; targetIndex++)
                {
                    Entity target = World.Active[targetIndex];
                    if (!CanDamage(shot.ProjectileData.Faction, shot.ProjectileData.OwnerId, target) || shot.Y < target.Y || shot.Y > target.Y + 1.8f) { continue; }
                    float impact = SweepCircle(from, displacement, target.Pos, target.Radius + shot.Radius);
                    // Terrain wins ties; equal entity impacts retain ascending runtime id.
                    if (impact < earliest) { earliest = impact; victim = target; }
                }
                if (earliest <= 1)
                {
                    shot.Pos = from + displacement * earliest; shot.Alive = false; shot.LifetimeTicks = 0;
                    if (victim is null)
                    { World.Emit(new(World.Tick, SimEventKind.ProjectileHitTerrain, shot.Id, shot.Pos, direction: shot.Facing, cell: hitCell, y: shot.Y, text: shot.Def.Id)); }
                    else
                    {
                        QueueDamage(shot.ProjectileData.OwnerId, victim.Id, specification.Damage, shot.ProjectileData.DamageMultiplier, specification.Knockback, shot.Facing);
                        World.Emit(new(World.Tick, SimEventKind.ProjectileHitEntity, victim.Id, shot.Pos, shot.Id, direction: shot.Facing, y: shot.Y, text: shot.Def.Id));
                    }
                }
                else { shot.Pos = to; }
            }
        }
    }

    private static int BoundCell(float coordinate, int size) => (int)MathF.Max(-1, MathF.Min(size, MathF.Floor(coordinate)));

    private static float SweepCircle(Vec2 from, Vec2 delta, Vec2 centre, float radius)
    {
        Vec2 offset = from - centre;
        double c = (double)offset.X * offset.X + (double)offset.Z * offset.Z - (double)radius * radius;
        if (c <= 0) { return 0; }
        double a = (double)delta.X * delta.X + (double)delta.Z * delta.Z;
        double b = (double)offset.X * delta.X + (double)offset.Z * delta.Z;
        double discriminant = b * b - a * c;
        if (a == 0 || b >= 0 || discriminant < 0) { return float.PositiveInfinity; }
        double t = (-b - System.Math.Sqrt(discriminant)) / a;
        return t is >= 0 and <= 1 ? (float)t : float.PositiveInfinity;
    }

    private static float SweepCell(Vec2 from, Vec2 delta, int x, int z, float radius)
    {
        // Exact rounded rectangle: two side strips and four corner circles.
        float result = MathF.Min(SweepBox(from, delta, x - radius, z, x + 1 + radius, z + 1), SweepBox(from, delta, x, z - radius, x + 1, z + 1 + radius));
        result = MathF.Min(result, SweepCircle(from, delta, new(x, z), radius));
        result = MathF.Min(result, SweepCircle(from, delta, new(x + 1, z), radius));
        result = MathF.Min(result, SweepCircle(from, delta, new(x, z + 1), radius));
        return MathF.Min(result, SweepCircle(from, delta, new(x + 1, z + 1), radius));
    }

    private static float SweepBox(Vec2 from, Vec2 delta, float minX, float minZ, float maxX, float maxZ)
    {
        float enter = 0, leave = 1;
        if (!Slab(from.X, delta.X, minX, maxX, ref enter, ref leave) || !Slab(from.Z, delta.Z, minZ, maxZ, ref enter, ref leave)) { return float.PositiveInfinity; }
        return enter;
    }
    private static bool Slab(float origin, float delta, float minimum, float maximum, ref float enter, ref float leave)
    {
        if (delta == 0) { return origin >= minimum && origin <= maximum; }
        float a = (minimum - origin) / delta, b = (maximum - origin) / delta;
        enter = MathF.Max(enter, MathF.Min(a, b)); leave = MathF.Min(leave, MathF.Max(a, b));
        return enter <= leave;
    }
}
