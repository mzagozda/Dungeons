using Dungeons.Core.Math;
using Dungeons.Core.Util;

namespace Dungeons.Core.Sim;

internal sealed class SeparationSystem
{
    private readonly int _columns, _rows;
    private readonly int[] _heads, _next = new int[GameConstants.MaxEntities];
    private readonly long[] _pairs = new long[GameConstants.MaxSeparationPairs];

    internal SeparationSystem(int width, int height)
    {
        _columns = (int)MathF.Ceiling(width / GameConstants.SpatialHashCell);
        _rows = (int)MathF.Ceiling(height / GameConstants.SpatialHashCell);
        _heads = new int[_columns * _rows];
    }
    internal void Step(World world)
    {
        for (int iteration = 0; iteration < GameConstants.SeparationIterations; iteration++)
        {
            for (int i = 0; i < _heads.Length; i++) { _heads[i] = -1; }
            for (int i = 0; i < world.Active.Count; i++)
            {
                Entity entity = world.Active[i];
                if (!Participates(entity)) { continue; }
                int bucket = BucketZ(entity.Pos.Z) * _columns + BucketX(entity.Pos.X);
                _next[i] = _heads[bucket]; _heads[bucket] = i;
            }
            int pairCount = 0;
            for (int i = 0; i < world.Active.Count; i++)
            {
                Entity a = world.Active[i];
                if (!Participates(a)) { continue; }
                int bx = BucketX(a.Pos.X), bz = BucketZ(a.Pos.Z);
                for (int z = System.Math.Max(0, bz - 1); z <= System.Math.Min(_rows - 1, bz + 1); z++)
                {
                    for (int x = System.Math.Max(0, bx - 1); x <= System.Math.Min(_columns - 1, bx + 1); x++)
                    {
                        for (int j = _heads[z * _columns + x]; j >= 0; j = _next[j])
                        {
                            if (j <= i || (!IsActor(a) && !IsActor(world.Active[j]))) { continue; }
                            if (pairCount == _pairs.Length) { world.Log.Log(LogLevel.Warning, "Sim", "Separation pair capacity exceeded."); break; }
                            _pairs[pairCount++] = ((long)i << 32) | (uint)j;
                        }
                    }
                }
            }
            Array.Sort(_pairs, 0, pairCount);
            for (int p = 0; p < pairCount; p++)
            {
                Entity a = world.Active[(int)(_pairs[p] >> 32)], b = world.Active[(int)_pairs[p]];
                Vec2 delta = b.Pos - a.Pos;
                float radius = a.Radius + b.Radius, squared = delta.LengthSquared;
                if (squared >= radius * radius) { continue; }
                float distance = MathF.Sqrt(squared);
                Vec2 direction = distance > 0 ? delta / distance : new Vec2(1, 0);
                float overlap = radius - distance;
                if (a.Kind == EntityKind.Enemy && b.Kind == EntityKind.Enemy) { overlap *= 0.5f; }
                float shareA = !IsActor(a) ? 0 : !IsActor(b) ? 1 : 0.5f;
                float shareB = !IsActor(b) ? 0 : !IsActor(a) ? 1 : 0.5f;
                if (shareA > 0) { a.Pos = Collision.Resolve(world.Grid, a.Pos, a.Pos - direction * (overlap * shareA), a.Radius); }
                if (shareB > 0) { b.Pos = Collision.Resolve(world.Grid, b.Pos, b.Pos + direction * (overlap * shareB), b.Radius); }
            }
        }
    }
    private int BucketX(float x) => System.Math.Max(0, System.Math.Min(_columns - 1, (int)MathF.Floor(x / GameConstants.SpatialHashCell)));
    private int BucketZ(float z) => System.Math.Max(0, System.Math.Min(_rows - 1, (int)MathF.Floor(z / GameConstants.SpatialHashCell)));
    private static bool IsActor(Entity e) => e.Kind is EntityKind.Player or EntityKind.Enemy;
    private static bool Participates(Entity e) => e.Alive && e.Ai.Mode != EnemyMode.Emerging && e.Radius > 0 && (IsActor(e) || e.Solid);
}
