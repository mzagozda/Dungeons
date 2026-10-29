using Dungeons.Core.Math;
using Dungeons.Core.Util;

namespace Dungeons.Core.Sim;

/// <summary>Canonical future-affecting state fingerprint (§7.9), with quantised transforms.</summary>
public static class WorldHasher
{
    /// <summary>Hashes ordered state without allocation or dependence on process hash randomisation.</summary>
    public static ulong Compute(World world, SessionState state = SessionState.Running)
    {
        var hash = world.Grid.HashPrefix;
        hash.Add(world.LevelId.Length); hash.Add(world.LevelId); hash.Add(world.Seed); hash.Add(world.Tick);
        // Exact source bytes remain a replay-envelope compatibility check. Runtime identity
        // uses baked terrain, entities and the compiled mission, including shortcut actions.
        hash.Add(world.ContentHash.Length); hash.Add(world.ContentHash);
        hash.Add((int)state); hash.Add(world.NextEntityId); hash.Add(world.Lives); hash.Add(world.Kills); hash.Add(world.Deaths);
        world.Difficulty.Hash(ref hash);
        HashRng(ref hash, world.Randoms.Combat.State); HashRng(ref hash, world.Randoms.Loot.State);
        HashRng(ref hash, world.Randoms.Ai.State); HashRng(ref hash, world.Randoms.Spawn.State);
        hash.Add(world.Players.Count);
        for (int i = 0; i < world.Players.Count; i++) { hash.Add(world.Players[i].Id); }
        hash.Add(world.Entities.Count);
        for (int i = 0; i < world.Entities.Count; i++)
        {
            Entity e = world.Entities[i];
            hash.Add(e.Id); hash.Add((int)e.Kind); hash.Add(e.Def.Id.Length); hash.Add(e.Def.Id);
            hash.Add(e.Def.SimulationHash);
            hash.Add(e.LevelId?.Length ?? -1); if (e.LevelId is not null) { hash.Add(e.LevelId); }
            hash.Add(e.PropertiesHash); hash.Add(e.Tags.Count);
            for (int tag = 0; tag < e.Tags.Count; tag++) { hash.Add(e.Tags[tag].Length); hash.Add(e.Tags[tag]); }
            HashVector(ref hash, e.Pos); hash.Add(MathUtil.Quantize(e.Y));
            HashVector(ref hash, e.Vel); HashVector(ref hash, e.Facing); HashVector(ref hash, e.Knockback);
            hash.Add(e.Alive); hash.Add(e.Solid); hash.Add(BitConverter.SingleToInt32Bits(e.Radius));
            hash.Add(e.MissionRegistered);
            hash.Add(e.SpawnOwnerId);
            hash.Add(e.Stats.Hp); hash.Add(e.Stats.Armor); hash.Add(BitConverter.SingleToInt32Bits(e.Stats.Speed));
            hash.Add(BitConverter.SingleToInt32Bits(e.Stats.DamageMultiplier)); hash.Add(BitConverter.SingleToInt32Bits(e.Stats.CooldownMultiplier));
            hash.Add(e.Health.Max); hash.Add(e.Health.Current); hash.Add(e.Health.Armor); hash.Add(e.Health.InvulnTicks);
            hash.Add(e.Inventory.Emeralds); hash.Add(e.Inventory.Arrows); hash.Add(e.Inventory.Souls);
            hash.Add(e.Experience); hash.Add(e.PickupAmount); hash.Add(e.PickupArmTicks); hash.Add(e.FootstepTicks);
            ActionState a = e.Action;
            hash.Add((int)a.Phase); hash.Add(a.Swing); hash.Add(a.Ranged); hash.Add(a.Remaining); hash.Add(a.StartedTick); hash.Add(a.BufferTicks);
            HashVector(ref hash, a.Direction); HashVector(ref hash, a.RollDirection); HashVector(ref hash, a.Move);
            hash.Add(a.RollRemaining); hash.Add(a.RollCooldown); hash.Add(a.PotionCooldown); hash.Add(a.BowCooldown); hash.Add(a.StaggerTicks); hash.Add(a.DeathTicks);
            hash.Add(e.Projectile.OwnerId); hash.Add((int)e.Projectile.Faction); HashVector(ref hash, e.Projectile.Velocity); hash.Add(BitConverter.SingleToInt32Bits(e.Projectile.DamageMultiplier));
            if (e.Kind == EntityKind.Enemy) { e.AiData.Hash(ref hash); }
            if (e.Kind is EntityKind.Gate or EntityKind.Chest or EntityKind.Checkpoint or EntityKind.Exit) { e.ObjectData.Hash(ref hash); e.ObjectRules.Hash(ref hash); }
            hash.Add(e.LifetimeTicks); hash.Add(e.SpawnTick); hash.Add(e.PathReachedGoal); hash.Add(e.PathTruncated); hash.Add(e.Path.Length);
            for (int p = 0; p < e.Path.Length; p++) { hash.Add(e.Path[p].X); hash.Add(e.Path[p].Z); }
        }
        world.HashRequests(ref hash);
        world.HashSpawns(ref hash);
        world.HashStatistics(ref hash);
        world.HashObjects(ref hash);
        world.Mission.Hash(ref hash);
        return hash.Value;
    }
    private static void HashVector(ref Fnv1a hash, Vec2 value) { hash.Add(MathUtil.Quantize(value.X)); hash.Add(MathUtil.Quantize(value.Z)); }
    private static void HashRng(ref Fnv1a hash, RngState rng) { hash.Add(rng.S0); hash.Add(rng.S1); hash.Add(rng.S2); hash.Add(rng.S3); }
}
