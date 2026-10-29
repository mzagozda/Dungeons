using System.Text.Json.Nodes;
using Dungeons.Core.Math;
using Dungeons.Core.Sim;
using Dungeons.Core.Util;

namespace Dungeons.Core.Content;

/// <summary>Allegiance used by damage and targeting.</summary>
public enum Faction
{
    /// <summary>World objects without an allegiance.</summary>
    Neutral,
    /// <summary>Cooperating heroes.</summary>
    Hero,
    /// <summary>Monsters; their projectiles pass through one another.</summary>
    Monster,
}

/// <summary>One immutable melee swing, in integral ticks and metres.</summary>
public readonly struct MeleeSwing
{
    /// <summary>Creates validated, gear-independent weapon timing and geometry.</summary>
    public MeleeSwing(int windup, int active, int recovery, int damage, float knockback, float halfAngle, float reach)
    {
        if (windup < 0 || active < 1 || recovery < 0 || damage < 1 || !MathUtil.IsFinite(knockback) || knockback < 0 || !MathUtil.IsFinite(halfAngle) || halfAngle < 0 || halfAngle > 180 || !MathUtil.IsFinite(reach) || reach <= 0)
        { throw new ArgumentOutOfRangeException(nameof(windup), "Invalid melee swing."); }
        Windup = windup; Active = active; Recovery = recovery; Damage = damage; Knockback = knockback; HalfAngle = halfAngle; Reach = reach;
        CosHalfAngle = MathF.Cos(halfAngle * (MathF.PI / 180));
    }
    /// <summary>Windup duration.</summary>
    public int Windup { get; }
    /// <summary>Active duration.</summary>
    public int Active { get; }
    /// <summary>Recovery duration before stat modifiers.</summary>
    public int Recovery { get; }
    /// <summary>Unmodified base damage.</summary>
    public int Damage { get; }
    /// <summary>Impulse before target resistance.</summary>
    public float Knockback { get; }
    /// <summary>Arc half-angle in degrees.</summary>
    public float HalfAngle { get; }
    /// <summary>Cached arc threshold; trigonometry never runs in the hit loop.</summary>
    public float CosHalfAngle { get; }
    /// <summary>Reach from the attacker centre, excluding target radius.</summary>
    public float Reach { get; }
}

/// <summary>Immutable melee combo or ranged weapon, referenced by a loadout.</summary>
public sealed class WeaponDef
{
    private readonly MeleeSwing[] _swings;
    /// <summary>Creates a melee weapon with a defensively copied combo.</summary>
    public WeaponDef(string id, IReadOnlyList<MeleeSwing> swings)
    {
        if (string.IsNullOrWhiteSpace(id) || swings is null || swings.Count < 1 || swings.Count > 16) { throw new ArgumentException("Weapon requires an id and 1–16 swings."); }
        Id = id; _swings = new MeleeSwing[swings.Count];
        for (int i = 0; i < swings.Count; i++) { if (swings[i].Damage < 1) { throw new ArgumentException("Invalid default swing."); } _swings[i] = swings[i]; }
    }
    /// <summary>Creates a ranged weapon whose projectile is resolved through content.</summary>
    public WeaponDef(string id, string projectileId, int windup, int recovery, int cooldown)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(projectileId) || windup < 0 || recovery < 0 || cooldown < 0) { throw new ArgumentException("Invalid ranged weapon."); }
        Id = id; ProjectileId = projectileId; Windup = windup; Recovery = recovery; Cooldown = cooldown; _swings = Array.Empty<MeleeSwing>();
    }
    /// <summary>Stable content id.</summary>
    public string Id { get; }
    /// <summary>Whether this weapon fires a projectile.</summary>
    public bool IsRanged => ProjectileId is not null;
    /// <summary>Immutable melee combo.</summary>
    public ReadOnlySpan<MeleeSwing> Swings => _swings;
    /// <summary>Projectile prototype id for a ranged weapon.</summary>
    public string? ProjectileId { get; }
    /// <summary>Ranged windup duration.</summary>
    public int Windup { get; }
    /// <summary>Ranged recovery duration.</summary>
    public int Recovery { get; }
    /// <summary>Ranged cooldown measured from fire.</summary>
    public int Cooldown { get; }
    internal static WeaponDef Parse(JsonObject source)
    {
        string id = JsonData.Text(source["id"]);
        if (JsonData.Text(source["kind"]) == "ranged")
        { return new(id, JsonData.Text(source["projectile"]), RequiredInt(source, "windupTicks"), RequiredInt(source, "recoveryTicks"), RequiredInt(source, "cooldownTicks")); }
        if (JsonData.Text(source["kind"]) != "melee" || source["swings"] is not JsonArray array) { throw new ArgumentException("Unknown weapon kind or missing swings."); }
        var swings = new List<MeleeSwing>();
        foreach (JsonNode? node in array)
        {
            JsonObject swing = node!.AsObject();
            swings.Add(new(RequiredInt(swing, "windupTicks"), RequiredInt(swing, "activeTicks"), RequiredInt(swing, "recoveryTicks"), RequiredInt(swing, "damage"), RequiredFloat(swing, "knockback"), RequiredFloat(swing, "halfAngleDeg"), RequiredFloat(swing, "reach")));
        }
        return new(id, swings);
    }
    private static int RequiredInt(JsonObject source, string key)
    { if (source[key] is not JsonValue value || !value.TryGetValue<int>(out int result)) { throw new ArgumentException("Weapon requires an integer " + key); } return result; }
    private static float RequiredFloat(JsonObject source, string key)
    { if (source[key] is null || source[key]!.GetValueKind() != System.Text.Json.JsonValueKind.Number) { throw new ArgumentException("Weapon requires a number " + key); } float result = (float)JsonData.Number(source[key], double.NaN); if (!MathUtil.IsFinite(result)) { throw new ArgumentException("Weapon number must be finite: " + key); } return result; }
}

/// <summary>Weapon references are immutable; future equipment replaces this loadout at spawn.</summary>
public sealed class WeaponLoadout
{
    /// <summary>Creates an optional melee/ranged pair.</summary>
    public WeaponLoadout(WeaponDef? melee = null, WeaponDef? ranged = null)
    { if (melee?.IsRanged == true || ranged is not null && !ranged.IsRanged) { throw new ArgumentException("Weapon in the wrong slot."); } Melee = melee; Ranged = ranged; }
    /// <summary>Melee slot.</summary>
    public WeaponDef? Melee { get; }
    /// <summary>Ranged slot.</summary>
    public WeaponDef? Ranged { get; }
    /// <summary>No attacks, useful for world objects and code scenarios.</summary>
    public static WeaponLoadout Empty { get; } = new();
    /// <summary>Code-scenario defaults; authored heroes reference the equivalent JSON definitions.</summary>
    public static WeaponLoadout HeroDefaults { get; } = new(new("core:sword", new MeleeSwing[] { new(6, 6, 12, 12, 1, 60, 1.7f), new(6, 6, 12, 12, 1, 60, 1.7f), new(9, 7, 21, 18, 2.5f, 70, 1.9f) }), new("core:bow", "core:arrow_hero", 6, 12, 30));
}

/// <summary>Per-prototype hero action tuning. Movement speed and armour stay in StatBlock.</summary>
public sealed class HeroRules
{
    /// <summary>Creates validated action and collection settings.</summary>
    public HeroRules(int rollTicks = 24, float rollDistance = 3, int rollInvulnerableFirst = 3, int rollInvulnerableLast = 20, int rollCooldown = 36, int potionCooldown = 1800,
        int arrowsStart = 30, int arrowsMax = 99, float pickupRadius = 1, float magnetRadius = 2.5f, float magnetSpeed = 6, float recoveryWalk = 0.4f)
    {
        if (rollTicks < 1 || !MathUtil.IsFinite(rollDistance) || rollDistance <= 0 || rollDistance * GameConstants.TickRate / rollTicks > GameConstants.MaxSpeed || rollInvulnerableFirst < 1 || rollInvulnerableLast < rollInvulnerableFirst || rollInvulnerableLast > rollTicks || rollCooldown < 0 || potionCooldown < 0 || arrowsStart < 0 || arrowsMax < arrowsStart || !MathUtil.IsFinite(pickupRadius) || pickupRadius <= 0 || !MathUtil.IsFinite(magnetRadius) || magnetRadius < pickupRadius || !MathUtil.IsFinite(magnetSpeed) || magnetSpeed <= 0 || !MathUtil.IsFinite(recoveryWalk) || recoveryWalk < 0 || recoveryWalk > 1)
        { throw new ArgumentException("Invalid hero control rules."); }
        RollTicks = rollTicks; RollDistance = rollDistance; RollInvulnerableFirst = rollInvulnerableFirst; RollInvulnerableLast = rollInvulnerableLast; RollCooldown = rollCooldown; PotionCooldown = potionCooldown;
        ArrowsStart = arrowsStart; ArrowsMax = arrowsMax; PickupRadius = pickupRadius; MagnetRadius = magnetRadius; MagnetSpeed = magnetSpeed; RecoveryWalk = recoveryWalk;
    }
    /// <summary>Default v1 settings for code-created heroes.</summary>
    public static HeroRules Default { get; } = new();
    /// <summary>Roll duration.</summary>
    public int RollTicks { get; }
    /// <summary>Unobstructed roll distance.</summary>
    public float RollDistance { get; }
    /// <summary>First invulnerable one-based roll tick.</summary>
    public int RollInvulnerableFirst { get; }
    /// <summary>Last invulnerable one-based roll tick.</summary>
    public int RollInvulnerableLast { get; }
    /// <summary>Cooldown from roll end.</summary>
    public int RollCooldown { get; }
    /// <summary>Potion cooldown.</summary>
    public int PotionCooldown { get; }
    /// <summary>Initial ammunition.</summary>
    public int ArrowsStart { get; }
    /// <summary>Ammunition capacity.</summary>
    public int ArrowsMax { get; }
    /// <summary>Collection distance.</summary>
    public float PickupRadius { get; }
    /// <summary>Magnet distance.</summary>
    public float MagnetRadius { get; }
    /// <summary>Magnet speed.</summary>
    public float MagnetSpeed { get; }
    /// <summary>Melee recovery movement fraction.</summary>
    public float RecoveryWalk { get; }
    internal static HeroRules Parse(JsonObject? source) => source is null ? Default : new(
        JsonData.Integer(source["rollTicks"], 24), (float)JsonData.Number(source["rollDistance"], 3), JsonData.Integer(source["rollInvulnerableFirst"], 3), JsonData.Integer(source["rollInvulnerableLast"], 20), JsonData.Integer(source["rollCooldownTicks"], 36), JsonData.Integer(source["potionCooldownTicks"], 1800),
        JsonData.Integer(source["arrowsStart"], 30), JsonData.Integer(source["arrowsMax"], 99), (float)JsonData.Number(source["pickupRadius"], 1), (float)JsonData.Number(source["magnetRadius"], 2.5), (float)JsonData.Number(source["magnetSpeed"], 6), (float)JsonData.Number(source["recoveryWalk"], .4));
}

/// <summary>Projectile motion and damage parameters copied at launch.</summary>
public sealed class ProjectileSpec
{
    /// <summary>Creates a non-piercing projectile specification.</summary>
    public ProjectileSpec(int damage, float speed, int lifetimeTicks = 90, float knockback = 0, float height = 1)
    { if (damage < 1 || !MathUtil.IsFinite(speed) || speed <= 0 || lifetimeTicks < 1 || !MathUtil.IsFinite(knockback) || knockback < 0 || !MathUtil.IsFinite(height) || height < 0) { throw new ArgumentException("Invalid projectile."); } Damage = damage; Speed = speed; LifetimeTicks = lifetimeTicks; Knockback = knockback; Height = height; }
    /// <summary>Base damage.</summary>
    public int Damage { get; }
    /// <summary>Speed in metres per second; not the ground-movement speed cap.</summary>
    public float Speed { get; }
    /// <summary>Lifetime in ticks.</summary>
    public int LifetimeTicks { get; }
    /// <summary>Impact impulse.</summary>
    public float Knockback { get; }
    /// <summary>Launch height over shooter's feet.</summary>
    public float Height { get; }
}

/// <summary>Supported collection effects.</summary>
public enum PickupKind
{
    /// <summary>Emerald counter.</summary>
    Emerald,
    /// <summary>Immediate health restore.</summary>
    Food,
    /// <summary>Ammunition.</summary>
    Arrows,
}

/// <summary>Immutable pickup effect, independent of visual resources.</summary>
public sealed class PickupSpec
{
    /// <summary>Creates an effect; food rounds fractional restored health upward.</summary>
    public PickupSpec(PickupKind kind, int amount = 1, float healFraction = 0.25f)
    { if (!Enum.IsDefined(typeof(PickupKind), kind) || amount < 1 || !MathUtil.IsFinite(healFraction) || healFraction <= 0 || healFraction > 1) { throw new ArgumentException("Invalid pickup."); } Kind = kind; Amount = amount; HealFraction = healFraction; }
    /// <summary>Collection effect.</summary>
    public PickupKind Kind { get; }
    /// <summary>Default amount, overridden by authored emerald amount.</summary>
    public int Amount { get; }
    /// <summary>Fraction of maximum health restored by food.</summary>
    public float HealFraction { get; }
}
