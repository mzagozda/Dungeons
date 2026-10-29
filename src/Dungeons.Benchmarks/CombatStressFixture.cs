using Dungeons.Core.Content;
using Dungeons.Core.Math;
using Dungeons.Core.Sim;

namespace Dungeons.Benchmarks;

/// <summary>Engine-free workload shared by the benchmark and the allocation/budget gate.</summary>
public static class CombatStressFixture
{
    /// <summary>A 256-square field starting with 64 active brains, 32 moving arrows and 128 pickups.</summary>
    public static GameSession Create()
    {
        var builder = new ScenarioBuilder(256, 256, "combat_stress");
        builder.Entity(new EntityDef("bench:hero", EntityKind.Player, new(1000000, 4.5f)), new(128.5f, 128.5f));
        var melee = new EntityDef("bench:chaser", EntityKind.Enemy, new(10000, 2.6f),
            weapons: new(new WeaponDef("bench:claws", new MeleeSwing[] { new(27, 6, 42, 8, 1, 45, 1.4f) })), brain: new(BrainKind.MeleeChaser, 400, 400));
        var ranged = new EntityDef("bench:archer", EntityKind.Enemy, new(10000, 2.8f),
            weapons: new(ranged: new WeaponDef("bench:bow", "core:arrow_hero", 36, 84, 0)), brain: new(BrainKind.Archer, 400, 400, trackingTicks: 24));
        var arrow = new EntityDef("bench:arrow", EntityKind.Projectile, new(1, 0), .1f, projectile: new(6, 11), faction: Faction.Monster);
        for (int i = 0; i < 64; i++) { builder.Entity(i < 32 ? melee : ranged, new(110.5f + i % 8 * 4, 110.5f + i / 8 * 4)); }
        for (int i = 0; i < 32; i++) { builder.Entity(arrow, new(115.5f + i % 8 * 3, 115.5f + i / 8 * 3)); }
        var pickup = new EntityDef("bench:emerald", EntityKind.Pickup, new(1, 0), pickup: new(PickupKind.Emerald));
        for (int i = 0; i < 128; i++) { builder.Entity(pickup, new(80.5f + i % 16 * 2, 80.5f + i / 16 * 2)); }
        return builder.Build().Session;
    }
    /// <summary>Repeatable moving target forces chase repaths and archer decisions.</summary>
    public static PlayerInput Input(int tick) => new((tick / 120 % 4) switch { 0 => new Vec2(1, 0), 1 => new(0, 1), 2 => new(-1, 0), _ => new(0, -1) });
}
