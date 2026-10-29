using Dungeons.Core.Math;
using Dungeons.Core.Util;
using System.Text.Json.Nodes;

namespace Dungeons.Core.Content;

/// <summary>Built-in behaviour strategies. Prototypes choose these ids rather than named monster code.</summary>
public enum BrainKind
{
    /// <summary>Chase and strike in melee.</summary>
    MeleeChaser,
    /// <summary>Maintain distance, aim and fire.</summary>
    Archer,
}

/// <summary>Immutable perception, positioning and telegraph tuning parsed outside ticks.</summary>
public sealed class BrainDef
{
    /// <summary>Creates validated strategy settings; weapon timing stays in the associated loadout.</summary>
    public BrainDef(BrainKind kind, float aggroRadius, float hearingRadius, float triggerDistance = 1.3f, int trackingTicks = 12,
        float minimumRange = 5, float maximumRange = 8, float retreatBelow = 3.5f, int retreatTicks = 90, float patrolRadius = 2)
    {
        if (!Enum.IsDefined(typeof(BrainKind), kind) || !FiniteNonnegative(aggroRadius) || !FiniteNonnegative(hearingRadius) || !FiniteNonnegative(triggerDistance) || trackingTicks < 0 || !FiniteNonnegative(minimumRange) || !FiniteNonnegative(maximumRange) || maximumRange < minimumRange || !FiniteNonnegative(retreatBelow) || retreatTicks < 1 || !FiniteNonnegative(patrolRadius))
        { throw new ArgumentException("Invalid brain tuning."); }
        Kind = kind; AggroRadius = aggroRadius; HearingRadius = hearingRadius; TriggerDistance = triggerDistance; TrackingTicks = trackingTicks;
        MinimumRange = minimumRange; MaximumRange = maximumRange; RetreatBelow = retreatBelow; RetreatTicks = retreatTicks; PatrolRadius = patrolRadius;
    }
    /// <summary>Behaviour dispatch key.</summary>
    public BrainKind Kind { get; }
    /// <summary>Sight acquisition distance.</summary>
    public float AggroRadius { get; }
    /// <summary>Attack-noise hearing distance.</summary>
    public float HearingRadius { get; }
    /// <summary>Melee edge-to-edge attack trigger distance.</summary>
    public float TriggerDistance { get; }
    /// <summary>Initial windup ticks during which facing may track a target.</summary>
    public int TrackingTicks { get; }
    /// <summary>Preferred minimum archer distance.</summary>
    public float MinimumRange { get; }
    /// <summary>Preferred maximum archer distance.</summary>
    public float MaximumRange { get; }
    /// <summary>Distance below which an archer retreats.</summary>
    public float RetreatBelow { get; }
    /// <summary>Maximum consecutive retreat duration.</summary>
    public int RetreatTicks { get; }
    /// <summary>Default idle wander radius, overridden by validated instance properties.</summary>
    public float PatrolRadius { get; }
    private static bool FiniteNonnegative(float value) => MathUtil.IsFinite(value) && value >= 0;
    internal static BrainDef? Parse(JsonObject source)
    {
        string brain = JsonData.Text(source["brain"]);
        // Registry reference validation reports unknown brains alongside other missing refs.
        if (brain is not ("core:brain_melee_chaser" or "core:brain_archer")) { return null; }
        BrainKind kind = brain switch { "core:brain_melee_chaser" => BrainKind.MeleeChaser, "core:brain_archer" => BrainKind.Archer, _ => throw new ArgumentException("Unsupported brain.") };
        JsonObject stats = source["stats"]?.AsObject() ?? new(), attack = source["attack"]?.AsObject() ?? new();
        return new(kind, (float)JsonData.Number(stats["aggroRadius"], kind == BrainKind.Archer ? 10 : 7), (float)JsonData.Number(stats["hearingRadius"], kind == BrainKind.Archer ? 12 : 10),
            (float)JsonData.Number(attack["triggerDistance"], 1.3), kind == BrainKind.Archer ? 24 : 12,
            (float)JsonData.Number(attack["minRange"], 5), (float)JsonData.Number(attack["maxRange"], 8), (float)JsonData.Number(attack["retreatBelow"], 3.5), MathUtil.SecondsToTicks((float)JsonData.Number(attack["retreatMax"], 1.5)));
    }
    internal static WeaponLoadout ParseWeapons(JsonObject source)
    {
        JsonObject attack = source["attack"]?.AsObject() ?? throw new ArgumentException("A brain requires an attack definition.");
        string id = JsonData.Text(source["id"]) + "_attack";
        int Ticks(string field, double fallback) => MathUtil.SecondsToTicks((float)JsonData.Number(attack[field], fallback));
        return JsonData.Text(attack["kind"]) switch
        {
            "melee" => new(new WeaponDef(id, new MeleeSwing[] { new(Ticks("windup", .45), Ticks("active", .1), Ticks("recovery", .7), JsonData.Integer(attack["damage"], 8), (float)JsonData.Number(attack["knockback"], 1), (float)JsonData.Number(attack["halfAngleDeg"], 45), (float)JsonData.Number(attack["reach"], 1.4)) })),
            "ranged" => new(ranged: new WeaponDef(id, JsonData.Text(attack["projectile"]), Ticks("aim", .6), Ticks("recovery", 1.4), 0)),
            _ => throw new ArgumentException("Unsupported brain attack kind."),
        };
    }
    internal void Hash(ref Fnv1a hash)
    {
        hash.Add((int)Kind); hash.Add(BitConverter.SingleToInt32Bits(AggroRadius)); hash.Add(BitConverter.SingleToInt32Bits(HearingRadius)); hash.Add(BitConverter.SingleToInt32Bits(TriggerDistance)); hash.Add(TrackingTicks);
        hash.Add(BitConverter.SingleToInt32Bits(MinimumRange)); hash.Add(BitConverter.SingleToInt32Bits(MaximumRange)); hash.Add(BitConverter.SingleToInt32Bits(RetreatBelow)); hash.Add(RetreatTicks); hash.Add(BitConverter.SingleToInt32Bits(PatrolRadius));
    }
}
