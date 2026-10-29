using Dungeons.Core.Content;
using Dungeons.Core.Math;

namespace Dungeons.Core.Sim;

public sealed partial class GameSession
{
    private readonly PendingDamage[] _damage = new PendingDamage[GameConstants.MaxPendingDamage];
    private int _damageCount;

    private void AdvanceActionTimers(Entity e)
    {
        ref ActionState a = ref e.ActionData;
        if (a.RollCooldown > 0) { a.RollCooldown--; }
        if (a.PotionCooldown > 0) { a.PotionCooldown--; }
        if (a.BowCooldown > 0) { a.BowCooldown--; }
        if (a.BufferTicks > 0) { a.BufferTicks--; }
        if (a.StaggerTicks > 0) { a.StaggerTicks--; }
        if (a.DeathTicks > 0) { a.DeathTicks--; }
        if (e.PickupArmTicks > 0) { e.PickupArmTicks--; }
        if (a.RollRemaining > 0 && --a.RollRemaining == 0)
        {
            a.RollCooldown = Cooldown(e, e.Def.Controls.RollCooldown);
            World.Emit(new(World.Tick, SimEventKind.RollEnded, e.Id, e.Pos, direction: a.RollDirection));
        }
    }

    private static int Cooldown(Entity e, int ticks) => (int)System.Math.Min(int.MaxValue, System.Math.Ceiling((double)ticks * e.Stats.CooldownMultiplier));

    private void ControlActions(Entity e, in PlayerInput input)
    {
        ref ActionState a = ref e.ActionData;
        a.Move = input.Move;
        if (a.Phase == AttackPhase.Idle && a.RollRemaining == 0 && input.Move.LengthSquared > .01f) { e.Facing = input.Move.Normalized; }
        if ((input.Pressed & InputButtons.Potion) != 0 && a.PotionCooldown == 0)
        {
            int restored = e.HealthData.Max - e.HealthData.Current; e.HealthData.Current = e.HealthData.Max;
            a.PotionCooldown = Cooldown(e, e.Def.Controls.PotionCooldown);
            World.Emit(new(World.Tick, SimEventKind.PotionUsed, e.Id, e.Pos, amount: restored, y: e.Y, text: e.Def.Id));
            if (restored > 0) { World.Emit(new(World.Tick, SimEventKind.Healed, e.Id, e.Pos, amount: restored)); }
        }
        if ((input.Pressed & InputButtons.Roll) != 0 && a.RollRemaining == 0 && a.RollCooldown == 0 && a.Phase != AttackPhase.Active)
        {
            a.Phase = AttackPhase.Idle; a.Remaining = a.BufferTicks = a.Swing = 0;
            a.RollRemaining = e.Def.Controls.RollTicks; a.RollDirection = input.Move.LengthSquared > .01f ? input.Move.Normalized : e.Facing;
            World.Emit(new(World.Tick, SimEventKind.RollStarted, e.Id, e.Pos, direction: a.RollDirection, y: e.Y, text: e.Def.Id));
        }
        if (a.RollRemaining > 0) { return; }
        if ((input.Pressed & InputButtons.Attack) != 0 && e.Def.Weapons.Melee is WeaponDef melee)
        {
            if (a.Phase == AttackPhase.Idle) { BeginAttack(e, false, 1, input.AimValid ? input.Aim : AutoAim.Resolve(World, e, false)); }
            else if (!a.Ranged && a.Swing < melee.Swings.Length && a.Phase is AttackPhase.Active or AttackPhase.Recovery) { a.BufferTicks = GameConstants.ComboBufferTicks; }
        }
        if ((input.Pressed & InputButtons.Ranged) != 0 && a.Phase == AttackPhase.Idle && a.BowCooldown == 0 && e.InventoryData.Arrows > 0 && e.Def.Weapons.Ranged is not null)
        { BeginAttack(e, true, 0, input.AimValid ? input.Aim : AutoAim.Resolve(World, e, true)); }
    }

    private void BeginAttack(Entity e, bool ranged, int swing, Vec2 direction)
    {
        ref ActionState a = ref e.ActionData;
        a.Phase = AttackPhase.Windup; a.Ranged = ranged; a.Swing = swing; a.StartedTick = World.Tick; a.Direction = direction; a.BufferTicks = 0;
        e.Facing = direction;
        a.Remaining = ranged ? e.Def.Weapons.Ranged!.Windup : e.Def.Weapons.Melee!.Swings[swing - 1].Windup;
        World.Emit(new(World.Tick, SimEventKind.AttackStarted, e.Id, e.Pos, amount: e.Kind == EntityKind.Player ? swing : 0, direction: direction, y: e.Y, text: e.Def.Id, value: ranged ? 1 : 0));
    }

    private void CombatSystem()
    {
        for (int i = 0; i < World.Active.Count; i++)
        {
            Entity e = World.Active[i]; if (!e.Alive) { continue; }
            ref ActionState a = ref e.ActionData;
            if (a.Phase != AttackPhase.Idle && a.StartedTick != World.Tick) { a.Remaining--; }
            if (a.Phase == AttackPhase.Windup && a.Remaining <= 0)
            {
                if (a.Ranged)
                {
                    if (e.Def.Brain is not null && !EnemyCanFire(e))
                    { a.Phase = AttackPhase.Idle; a.Remaining = 0; SetEnemyMode(e, EnemyMode.Position); continue; }
                    WeaponDef bow = e.Def.Weapons.Ranged!;
                    Entity? shot = World.SpawnProjectile(e, World.ProjectileDefinition(bow.ProjectileId!), a.Direction);
                    if (shot is not null && e.Kind == EntityKind.Player) { e.InventoryData.Arrows--; a.BowCooldown = Cooldown(e, bow.Cooldown); }
                    a.Phase = AttackPhase.Recovery; a.Remaining = Cooldown(e, bow.Recovery);
                }
                else
                {
                    a.Phase = AttackPhase.Active; a.Remaining = e.Def.Weapons.Melee!.Swings[a.Swing - 1].Active;
                    World.Emit(new(World.Tick, SimEventKind.AttackActive, e.Id, e.Pos, amount: e.Kind == EntityKind.Player ? a.Swing : 0, direction: a.Direction, y: e.Y, text: e.Def.Id));
                    MeleeHits(e);
                }
            }
            else if (a.Phase == AttackPhase.Active && a.Remaining <= 0)
            { a.Phase = AttackPhase.Recovery; a.Remaining = Cooldown(e, e.Def.Weapons.Melee!.Swings[a.Swing - 1].Recovery); }
            if (a.Phase == AttackPhase.Recovery)
            {
                if (!a.Ranged && a.BufferTicks > 0 && a.Swing < e.Def.Weapons.Melee!.Swings.Length) { BeginAttack(e, false, a.Swing + 1, a.Direction); }
                else if (a.Remaining <= 0) { a.Phase = AttackPhase.Idle; a.Swing = a.BufferTicks = 0; }
            }
            if (e.Kind != EntityKind.Player) { continue; }
            if (a.RollRemaining > 0)
            {
                HeroRules rules = e.Def.Controls;
                e.Vel = a.RollDirection * (rules.RollDistance / (rules.RollTicks * GameConstants.Dt));
                int age = rules.RollTicks - a.RollRemaining + 1;
                if (age >= rules.RollInvulnerableFirst && age <= rules.RollInvulnerableLast) { e.HealthData.InvulnTicks = System.Math.Max(1, e.HealthData.InvulnTicks); }
            }
            else
            {
                float speed = a.Phase is AttackPhase.Windup or AttackPhase.Active ? 0 : a.Phase == AttackPhase.Recovery && !a.Ranged ? e.Def.Controls.RecoveryWalk : 1;
                e.Vel = a.Move * (e.Stats.Speed * speed);
            }
        }
    }

    private void MeleeHits(Entity attacker)
    {
        MeleeSwing swing = attacker.Def.Weapons.Melee!.Swings[attacker.ActionData.Swing - 1];
        for (int i = 0; i < World.Active.Count; i++)
        {
            Entity target = World.Active[i]; if (!CanDamage(attacker.Def.Faction, attacker.Id, target)) { continue; }
            Vec2 offset = target.Pos - attacker.Pos; float distance = offset.Length;
            IntVec2 targetCell = IntVec2.FromPosition(target.Pos), attackerCell = IntVec2.FromPosition(attacker.Pos);
            bool adjacentStairs = World.Grid[targetCell.X, targetCell.Z].StairsDir != StairsDirection.None && System.Math.Abs(targetCell.X - attackerCell.X) <= 1 && System.Math.Abs(targetCell.Z - attackerCell.Z) <= 1;
            // One micrometre accommodates subtraction rounding at the authored reach boundary.
            if (System.Math.Abs(target.Y - attacker.Y) > .5f && !adjacentStairs || distance > swing.Reach + target.Radius + .000001f || distance > .3f && Vec2.Dot(attacker.Facing, offset / distance) < swing.CosHalfAngle) { continue; }
            QueueDamage(attacker.Id, target.Id, swing.Damage, attacker.Stats.DamageMultiplier, swing.Knockback, distance > 0 ? offset / distance : attacker.Facing, attacker.Kind == EntityKind.Player ? attacker.ActionData.Swing : 0);
        }
    }

    private static bool CanDamage(Faction faction, int owner, Entity target) => target.Id != owner && target.Alive && target.LifetimeTicks != 0 && target.AiData.Mode != EnemyMode.Emerging &&
        (target.Kind is EntityKind.Player or EntityKind.Enemy && target.Def.Faction != faction || target.Kind == EntityKind.Prop && target.Def.Breakable);

    internal void QueueDamage(int sourceId, int targetId, int damage, float multiplier, float knockback, Vec2 direction, int meleeSwing = 0)
    {
        if (_damageCount == _damage.Length) { throw new InvalidOperationException("Deferred damage capacity exceeded."); }
        _damage[_damageCount++] = new(sourceId, targetId, damage, multiplier, knockback, direction, meleeSwing);
    }

    private void DamageSystem()
    {
        for (int i = 0; i < _damageCount; i++)
        {
            PendingDamage hit = _damage[i]; Entity? target = World.FindEntity(hit.TargetId);
            if (target is null || !target.Alive || target.HealthData.InvulnTicks > 0 || target.AiData.Mode == EnemyMode.Emerging) { continue; }
            double raw = hit.Damage * (double)World.Randoms.Combat.Range(.9f, 1.1f) * hit.Multiplier * (100d / (100d + target.HealthData.Armor));
            int amount = (int)System.Math.Max(1, System.Math.Min(int.MaxValue, System.Math.Round(raw, MidpointRounding.AwayFromZero)));
            target.HealthData.Current = System.Math.Max(0, target.HealthData.Current - amount);
            float impulse = hit.Knockback * (1 - target.Def.KnockbackResistance);
            target.Knockback += hit.Direction * impulse;
            if (target.Kind == EntityKind.Player) { target.HealthData.InvulnTicks = GameConstants.InvulnAfterHitTicks; }
            else if (target.Kind == EntityKind.Enemy && impulse >= 2)
            { target.ActionData.Phase = AttackPhase.Idle; target.ActionData.Remaining = target.ActionData.BufferTicks = target.ActionData.Swing = 0; target.ActionData.StaggerTicks = GameConstants.StaggerTicks; target.Vel = Vec2.Zero; }
            bool lethal = target.HealthData.Current == 0;
            World.Emit(new(World.Tick, SimEventKind.DamageApplied, target.Id, target.Pos, hit.SourceId, amount, hit.Direction * impulse, lethal, y: target.Y, meleeSwing: hit.MeleeSwing, text: target.Def.Id));
            if (!lethal) { continue; }
            target.Alive = target.Solid = false; target.Vel = target.Knockback = Vec2.Zero; target.ActionData = default; target.FootstepTicks = 0;
            World.Mission.Removed(target);
            if (target.Def.Brain is not null) { SetEnemyMode(target, EnemyMode.Dead); }
            if (target.Kind == EntityKind.Player)
            { target.ActionData.DeathTicks = GameConstants.DeathTicks; World.Deaths++; World.Lives = System.Math.Max(0, World.Lives - 1); World.Emit(new(World.Tick, SimEventKind.PlayerDied, target.Id, target.Pos, amount: World.Lives)); }
            else
            {
                target.LifetimeTicks = GameConstants.CorpseTicks;
                if (target.Kind == EntityKind.Enemy) { World.RecordKill(target.Def.Id); World.ReleaseEnemyPath(target); }
                if (target.Def.LootTableId is string loot) { World.DropLoot(loot, target.Pos); }
                Entity? source = World.FindEntity(hit.SourceId);
                if (source?.Kind == EntityKind.Player) { source.Experience = (int)System.Math.Min(int.MaxValue, (long)source.Experience + target.Def.Experience); }
            }
        }
        _damageCount = 0;
    }

    private void PickupSystem()
    {
        for (int i = 0; i < World.Active.Count; i++)
        {
            Entity pickup = World.Active[i]; PickupSpec? effect = pickup.Def.Pickup;
            if (pickup.Kind != EntityKind.Pickup || !pickup.Alive || pickup.LifetimeTicks == 0 || pickup.PickupArmTicks > 0 || effect is null) { continue; }
            Entity? nearest = null; float nearestDistance = float.MaxValue;
            for (int p = 0; p < World.Players.Count; p++)
            {
                Entity player = World.Players[p];
                if (!player.Alive || effect.Kind == PickupKind.Food && player.HealthData.Current == player.HealthData.Max || effect.Kind == PickupKind.Arrows && player.InventoryData.Arrows >= player.Def.Controls.ArrowsMax) { continue; }
                float distance = Vec2.DistanceSquared(player.Pos, pickup.Pos);
                if (distance <= player.Def.Controls.MagnetRadius * player.Def.Controls.MagnetRadius && distance < nearestDistance) { nearest = player; nearestDistance = distance; }
            }
            if (nearest is null) { continue; }
            HeroRules rules = nearest.Def.Controls; Vec2 offset = nearest.Pos - pickup.Pos;
            pickup.Pos = Collision.Resolve(World.Grid, pickup.Pos, pickup.Pos + offset.ClampLength(rules.MagnetSpeed * GameConstants.Dt), pickup.Radius);
            if (Vec2.DistanceSquared(nearest.Pos, pickup.Pos) > rules.PickupRadius * rules.PickupRadius) { continue; }
            int amount = pickup.PickupAmount;
            switch (effect.Kind)
            {
                case PickupKind.Emerald:
                    int emeralds = (int)System.Math.Min(int.MaxValue, (long)nearest.InventoryData.Emeralds + amount);
                    amount = emeralds - nearest.InventoryData.Emeralds; nearest.InventoryData.Emeralds = emeralds; break;
                case PickupKind.Arrows:
                    int arrows = (int)System.Math.Min(rules.ArrowsMax, (long)nearest.InventoryData.Arrows + amount);
                    amount = arrows - nearest.InventoryData.Arrows; nearest.InventoryData.Arrows = arrows; break;
                case PickupKind.Food:
                    amount = (int)System.Math.Min(nearest.HealthData.Max - nearest.HealthData.Current, System.Math.Ceiling(nearest.HealthData.Max * (double)effect.HealFraction));
                    nearest.HealthData.Current += amount; World.Emit(new(World.Tick, SimEventKind.Healed, nearest.Id, nearest.Pos, amount: amount)); break;
            }
            pickup.Alive = false; pickup.LifetimeTicks = 0;
            World.Mission.Removed(pickup);
            World.Emit(new(World.Tick, SimEventKind.PickupCollected, pickup.Id, pickup.Pos, nearest.Id, amount, y: pickup.Y, text: pickup.Def.Id));
        }
    }
}
