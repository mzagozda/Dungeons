using Dungeons.Core.Content;
using Dungeons.Core.Math;
using Dungeons.Core.Util;

namespace Dungeons.Core.Sim;

/// <summary>Spawn-time tier tuning; v1 selects Default, keeping later tiers independent of monster code.</summary>
public sealed class DifficultyProfile
{
    /// <summary>Creates finite positive multipliers and a live cap bounded by engine capacity.</summary>
    public DifficultyProfile(float hpMultiplier = 1, float damageMultiplier = 1, float extraSpawnsMultiplier = 1, int maxMonsters = GameConstants.MaxMonsters)
    {
        if (!MathUtil.IsFinite(hpMultiplier) || hpMultiplier <= 0 || !MathUtil.IsFinite(damageMultiplier) || damageMultiplier <= 0 || !MathUtil.IsFinite(extraSpawnsMultiplier) || extraSpawnsMultiplier <= 0 || maxMonsters < 1 || maxMonsters > GameConstants.MaxMonsters)
        { throw new ArgumentException("Invalid difficulty profile."); }
        HpMultiplier = hpMultiplier; DamageMultiplier = damageMultiplier; ExtraSpawnsMultiplier = extraSpawnsMultiplier; MaxMonsters = maxMonsters;
    }
    /// <summary>Unmodified v1 tuning.</summary>
    public static DifficultyProfile Default { get; } = new();
    /// <summary>Enemy health multiplier.</summary>
    public float HpMultiplier { get; }
    /// <summary>Enemy outgoing damage multiplier.</summary>
    public float DamageMultiplier { get; }
    /// <summary>Multiplier applied once to authored spawn-group counts.</summary>
    public float ExtraSpawnsMultiplier { get; }
    /// <summary>Live monster cap for this session.</summary>
    public int MaxMonsters { get; }
    internal bool IsDefault => HpMultiplier == 1 && DamageMultiplier == 1 && ExtraSpawnsMultiplier == 1 && MaxMonsters == GameConstants.MaxMonsters;
    /// <summary>Rounds authored wave counts up and rejects requests beyond bounded queue capacity.</summary>
    public int ScaleSpawnCount(int count)
    {
        double scaled = System.Math.Ceiling(count * (double)ExtraSpawnsMultiplier);
        if (count < 0 || scaled > GameConstants.MaxEntities) { throw new ArgumentOutOfRangeException(nameof(count)); }
        return (int)scaled;
    }
    internal void Apply(Entity entity)
    {
        if (entity.Kind != EntityKind.Enemy) { return; }
        StatBlock basis = entity.Def.Stats;
        int hp = (int)System.Math.Max(1, System.Math.Min(int.MaxValue, System.Math.Ceiling(basis.Hp * (double)HpMultiplier)));
        float damage = (float)System.Math.Max(float.Epsilon, System.Math.Min(float.MaxValue, basis.DamageMultiplier * (double)DamageMultiplier));
        entity.Stats = new(hp, basis.Speed, basis.Armor, damage, basis.CooldownMultiplier);
        entity.HealthData.Max = entity.HealthData.Current = hp;
    }
    internal void Hash(ref Fnv1a hash)
    { hash.Add(BitConverter.SingleToInt32Bits(HpMultiplier)); hash.Add(BitConverter.SingleToInt32Bits(DamageMultiplier)); hash.Add(BitConverter.SingleToInt32Bits(ExtraSpawnsMultiplier)); hash.Add(MaxMonsters); }
}
