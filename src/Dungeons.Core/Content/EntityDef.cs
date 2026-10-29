using Dungeons.Core.Math;
using Dungeons.Core.Sim;
using Dungeons.Core.Level;
using Dungeons.Core.Util;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Dungeons.Core.Content;

/// <summary>Immutable spawn-time stats. Systems read the entity's copy, not global hero constants.</summary>
public readonly struct StatBlock
{
    /// <summary>Validates per-kind health, armour, speed and future gear multipliers.</summary>
    public StatBlock(int hp, float speed, int armor = 0, float damageMultiplier = 1, float cooldownMultiplier = 1)
    {
        if (hp <= 0 || armor < 0 || !MathUtil.IsFinite(speed) || speed < 0 || speed > GameConstants.MaxSpeed ||
            !MathUtil.IsFinite(damageMultiplier) || damageMultiplier <= 0 || !MathUtil.IsFinite(cooldownMultiplier) || cooldownMultiplier <= 0)
        { throw new ArgumentOutOfRangeException(nameof(hp), "Invalid stat block."); }
        Hp = hp; Speed = speed; Armor = armor; DamageMultiplier = damageMultiplier; CooldownMultiplier = cooldownMultiplier;
    }
    /// <summary>Spawn health.</summary>
    public int Hp { get; }
    /// <summary>Ground speed in metres/second.</summary>
    public float Speed { get; }
    /// <summary>Damage reduction stat.</summary>
    public int Armor { get; }
    /// <summary>Outgoing damage multiplier.</summary>
    public float DamageMultiplier { get; }
    /// <summary>Cooldown duration multiplier.</summary>
    public float CooldownMultiplier { get; }
}

/// <summary>Immutable simulation prototype; data loading/inheritance arrives at M2.</summary>
public sealed class EntityDef
{
    internal EntityDef(string id, EntityKind kind, StatBlock stats, float radius, bool solid, DefinitionData data, WeaponLoadout? weapons = null)
        : this(id, kind, stats, radius, solid, weapons, HeroRules.Parse(JsonData.Parse(data.Json.GetRawText())["control"] as JsonObject))
    {
        Data = data; JsonObject source = JsonData.Parse(data.Json.GetRawText()).AsObject();
        Brain = BrainDef.Parse(source);
        if (Brain is not null && weapons is null) { Weapons = BrainDef.ParseWeapons(source); }
        ValidateBrain();
        if (source["faction"] is JsonNode faction)
        { Faction = JsonData.Text(faction) switch { "Hero" => Faction.Hero, "Monster" => Faction.Monster, _ => throw new ArgumentException("Invalid authored faction.") }; }
        Experience = JsonData.Integer(source["stats"]?["xp"]); KnockbackResistance = (float)JsonData.Number(source["stats"]?["knockbackResistance"]);
        Breakable = JsonData.Boolean(source["breakable"]);
        LootTableId = source["loot"] is null ? null : JsonData.Text(source["loot"]);
        if (Experience < 0 || !MathUtil.IsFinite(KnockbackResistance) || KnockbackResistance < 0 || KnockbackResistance > 1) { throw new ArgumentException("Invalid XP or knockback resistance."); }
        if (source["projectile"] is JsonObject projectile)
        {
            if (JsonData.Integer(projectile["pierce"]) != 0) { throw new ArgumentException("Piercing projectiles are not supported in v1."); }
            Projectile = new(JsonData.Integer(projectile["damage"]), (float)JsonData.Number(projectile["speed"]), projectile["lifetimeTicks"] is not null ? JsonData.Integer(projectile["lifetimeTicks"]) : MathUtil.SecondsToTicks((float)JsonData.Number(projectile["lifetime"], 1.5)), (float)JsonData.Number(projectile["knockback"]), (float)JsonData.Number(projectile["height"], 1));
        }
        if (source["pickup"] is JsonObject pickup)
        {
            PickupKind type = JsonData.Text(pickup["kind"]) switch { "emerald" => PickupKind.Emerald, "food" => PickupKind.Food, "arrows" => PickupKind.Arrows, _ => throw new ArgumentException("Unknown pickup kind.") };
            Pickup = new(type, JsonData.Integer(pickup["amount"], type == PickupKind.Arrows ? 10 : 1), (float)JsonData.Number(pickup["healFraction"], JsonData.Number(pickup["healPercent"], 25) / 100));
        }
        var diagnostics = new List<Diagnostic>(); JsonObject defaults = Data.Properties.Normalize(new JsonObject(), diagnostics, "/props");
        using JsonDocument document = JsonDocument.Parse(JsonData.Stringify(defaults)); DefaultProperties = document.RootElement.Clone();
        DefaultPropertiesHash = RuntimeProperties.Hash(Kind, DefaultProperties);
        DefaultPickupAmount = Pickup?.Amount ?? 0;
        DefaultObjectRules = new(Kind, DefaultProperties);
        if (Pickup?.Kind == PickupKind.Emerald && DefaultProperties.TryGetProperty("amount", out JsonElement amount)) { DefaultPickupAmount = amount.GetInt32(); }
        SimulationHash = ComputeSimulationHash();
    }
    /// <summary>Creates a prototype without host resources or platform dependencies.</summary>
    public EntityDef(string id, EntityKind kind, StatBlock stats, float radius = 0.35f, bool solid = false, WeaponLoadout? weapons = null, HeroRules? controls = null, ProjectileSpec? projectile = null, PickupSpec? pickup = null, Faction? faction = null, BrainDef? brain = null, string? lootTableId = null)
    {
        if (string.IsNullOrWhiteSpace(id)) { throw new ArgumentException("Definition id is required.", nameof(id)); }
        if (!Enum.IsDefined(typeof(EntityKind), kind) || stats.Hp <= 0 || faction is not null && !Enum.IsDefined(typeof(Faction), faction.Value)) { throw new ArgumentException("Invalid entity kind/stat block/faction.", nameof(stats)); }
        if (!MathUtil.IsFinite(radius) || radius < 0 || radius > 1) { throw new ArgumentOutOfRangeException(nameof(radius)); }
        Id = id; Kind = kind; Stats = stats; Radius = radius; Solid = solid;
        Weapons = weapons ?? (kind == EntityKind.Player ? WeaponLoadout.HeroDefaults : WeaponLoadout.Empty); Controls = controls ?? HeroRules.Default;
        Projectile = projectile; Pickup = pickup; Faction = faction ?? (kind == EntityKind.Player ? Faction.Hero : kind == EntityKind.Enemy ? Faction.Monster : Faction.Neutral);
        Brain = brain;
        if (lootTableId is not null && !ContentRegistry.IsId(lootTableId)) { throw new ArgumentException("Invalid loot table id.", nameof(lootTableId)); }
        LootTableId = lootTableId; DefaultPickupAmount = pickup?.Amount ?? 0;
        DefaultObjectRules = new(kind, DefaultProperties);
        ValidateBrain();
        var hash = new Fnv1a(); hash.Add("{}"); DefaultPropertiesHash = hash.Value;
        SimulationHash = ComputeSimulationHash();
    }
    /// <summary>Namespaced content id.</summary>
    public string Id { get; }
    internal ObjectRules DefaultObjectRules { get; }
    /// <summary>Runtime component family.</summary>
    public EntityKind Kind { get; }
    /// <summary>Base statistics copied at spawn.</summary>
    public StatBlock Stats { get; }
    /// <summary>Ground collision radius.</summary>
    public float Radius { get; }
    /// <summary>Whether this is a static blocking entity.</summary>
    public bool Solid { get; }
    /// <summary>Immutable authored metadata and property schema.</summary>
    public DefinitionData Data { get; } = DefinitionData.Empty;
    /// <summary>Immutable weapon references; systems read these rather than global sword numbers.</summary>
    public WeaponLoadout Weapons { get; }
    /// <summary>Action and collection tuning for this prototype.</summary>
    public HeroRules Controls { get; }
    /// <summary>Projectile parameters, when applicable.</summary>
    public ProjectileSpec? Projectile { get; }
    /// <summary>Pickup effect, when applicable.</summary>
    public PickupSpec? Pickup { get; }
    /// <summary>Spawn allegiance.</summary>
    public Faction Faction { get; }
    /// <summary>XP granted on an enemy death.</summary>
    public int Experience { get; }
    /// <summary>Fraction of incoming impulse resisted.</summary>
    public float KnockbackResistance { get; }
    /// <summary>Whether a neutral prop can be damaged.</summary>
    public bool Breakable { get; }
    /// <summary>Optional enemy strategy; code-only stationary fixtures omit it.</summary>
    public BrainDef? Brain { get; }
    /// <summary>Optional immutable enemy/destructible loot table reference.</summary>
    public string? LootTableId { get; }
    internal int DefaultPickupAmount { get; }
    internal JsonElement DefaultProperties { get; } = DefinitionData.Empty.Json;
    internal ulong DefaultPropertiesHash { get; }
    internal ulong SimulationHash { get; }
    private void ValidateBrain()
    {
        if (Brain is not null && (Kind != EntityKind.Enemy || Brain.Kind == BrainKind.MeleeChaser && Weapons.Melee is null || Brain.Kind == BrainKind.Archer && Weapons.Ranged is null))
        { throw new ArgumentException("An enemy brain requires a matching weapon loadout."); }
    }
    private ulong ComputeSimulationHash()
    {
        var hash = new Fnv1a(); hash.Add((int)Faction); hash.Add(Experience); hash.Add(BitConverter.SingleToInt32Bits(KnockbackResistance)); hash.Add(Breakable);
        hash.Add((int)Kind); hash.Add(Stats.Hp); hash.Add(Stats.Armor); hash.Add(BitConverter.SingleToInt32Bits(Stats.Speed)); hash.Add(BitConverter.SingleToInt32Bits(Stats.DamageMultiplier)); hash.Add(BitConverter.SingleToInt32Bits(Stats.CooldownMultiplier));
        hash.Add(BitConverter.SingleToInt32Bits(Radius)); hash.Add(Solid); hash.Add(RequiresGround); hash.Add(DefaultPropertiesHash); hash.Add(DefaultPickupAmount);
        hash.Add(Brain is not null); Brain?.Hash(ref hash);
        hash.Add(LootTableId?.Length ?? -1); if (LootTableId is not null) { hash.Add(LootTableId); }
        HeroRules c = Controls;
        hash.Add(c.RollTicks); hash.Add(BitConverter.SingleToInt32Bits(c.RollDistance)); hash.Add(c.RollInvulnerableFirst); hash.Add(c.RollInvulnerableLast); hash.Add(c.RollCooldown); hash.Add(c.PotionCooldown);
        hash.Add(c.ArrowsStart); hash.Add(c.ArrowsMax); hash.Add(BitConverter.SingleToInt32Bits(c.PickupRadius)); hash.Add(BitConverter.SingleToInt32Bits(c.MagnetRadius)); hash.Add(BitConverter.SingleToInt32Bits(c.MagnetSpeed)); hash.Add(BitConverter.SingleToInt32Bits(c.RecoveryWalk));
        HashWeapon(ref hash, Weapons.Melee); HashWeapon(ref hash, Weapons.Ranged);
        hash.Add(Projectile is not null);
        if (Projectile is ProjectileSpec p) { hash.Add(p.Damage); hash.Add(BitConverter.SingleToInt32Bits(p.Speed)); hash.Add(p.LifetimeTicks); hash.Add(BitConverter.SingleToInt32Bits(p.Knockback)); hash.Add(BitConverter.SingleToInt32Bits(p.Height)); }
        hash.Add(Pickup is not null);
        if (Pickup is PickupSpec pickup) { hash.Add((int)pickup.Kind); hash.Add(pickup.Amount); hash.Add(BitConverter.SingleToInt32Bits(pickup.HealFraction)); }
        return hash.Value;
    }
    private static void HashWeapon(ref Fnv1a hash, WeaponDef? weapon)
    {
        hash.Add(weapon is not null); if (weapon is null) { return; }
        hash.Add(weapon.Id.Length); hash.Add(weapon.Id); hash.Add(weapon.ProjectileId?.Length ?? -1); if (weapon.ProjectileId is not null) { hash.Add(weapon.ProjectileId); }
        hash.Add(weapon.Windup); hash.Add(weapon.Recovery); hash.Add(weapon.Cooldown); hash.Add(weapon.Swings.Length);
        foreach (MeleeSwing swing in weapon.Swings)
        { hash.Add(swing.Windup); hash.Add(swing.Active); hash.Add(swing.Recovery); hash.Add(swing.Damage); hash.Add(BitConverter.SingleToInt32Bits(swing.Knockback)); hash.Add(BitConverter.SingleToInt32Bits(swing.HalfAngle)); hash.Add(BitConverter.SingleToInt32Bits(swing.Reach)); }
    }
    /// <summary>Whether placement requires a traversable ground cell.</summary>
    public bool RequiresGround => Data.Json.TryGetProperty("requiresGround", out System.Text.Json.JsonElement value) ? value.GetBoolean()
        : Solid || Kind is EntityKind.Player or EntityKind.Enemy or EntityKind.Chest or EntityKind.Checkpoint or EntityKind.Exit;
}
