using Dungeons.Core.Math;
using Dungeons.Core.Sim;

namespace Dungeons.Game.Input;

/// <summary>One circular touch target in density-independent screen coordinates.</summary>
public readonly struct TouchTarget(InputButtons action, Vec2 centre, float diameter)
{
    public InputButtons Action { get; } = action;
    public Vec2 Centre { get; } = centre;
    public float Radius { get; } = diameter / 2;
    public bool Contains(Vec2 point) => Vec2.DistanceSquared(Centre, point) <= Radius * Radius;
}
public readonly record struct TouchPlacement(InputButtons Action, Vec2 Normalized);

/// <summary>Safe-area-aware arc, shared by input hit testing, drawing and layout editing.</summary>
public sealed class TouchLayout
{
    public const float JoystickRadiusDp = 60, JoystickDeadZoneDp = 20, FollowDistanceDp = 90;
    public const float BowDragThresholdDp = 16, BowDeadZoneDp = 12;
    public const double BowHoldSeconds = .12;
    private const float PaddingDp = 12;
    private readonly TouchTarget[] _targets = new TouchTarget[4];
    public Vec2 Minimum { get; private set; }
    public Vec2 Maximum { get; private set; }
    public float Scale { get; private set; } = 1;
    public bool LeftHanded { get; private set; }
    public ReadOnlySpan<TouchTarget> Targets => _targets;
    public float JoystickRadius => JoystickRadiusDp * Scale;
    public float JoystickDeadZone => JoystickDeadZoneDp * Scale;
    public static float FitScale(Vec2 availableDp, float requested)
    {
        if (!availableDp.IsFinite || !float.IsFinite(requested) || requested is < .8f or > 1.5f) { return 0; }
        float fit = MathF.Min(requested, MathF.Min(availableDp.X / 384, availableDp.Z / 220));
        return fit < .8f ? 0 : MathF.Floor(fit * 1000) / 1000;
    }
    public void Configure(Vec2 sizeDp, Vec2 insetTopLeftDp, Vec2 insetBottomRightDp, float scale = 1, bool leftHanded = false)
    {
        if (!sizeDp.IsFinite || !insetTopLeftDp.IsFinite || !insetBottomRightDp.IsFinite || !float.IsFinite(scale) || scale is < .8f or > 1.5f || insetTopLeftDp.X < 0 || insetTopLeftDp.Z < 0 || insetBottomRightDp.X < 0 || insetBottomRightDp.Z < 0)
        { throw new ArgumentOutOfRangeException(nameof(sizeDp)); }
        Minimum = insetTopLeftDp; Maximum = sizeDp - insetBottomRightDp; Scale = scale; LeftHanded = leftHanded;
        if (Maximum.X - Minimum.X < 384 * scale || Maximum.Z - Minimum.Z < 220 * scale) { throw new ArgumentException("Touch layout requires at least 384 by 220 dp of safe area at its configured scale."); }
        Vec2 attack = Maximum - new Vec2(54, 54) * scale;
        Set(0, InputButtons.Attack, attack, 84); Set(1, InputButtons.Roll, attack - new Vec2(86, 0) * scale, 64);
        Set(2, InputButtons.Ranged, attack - new Vec2(0, 86) * scale, 64); Set(3, InputButtons.Potion, attack - new Vec2(86, 78) * scale, 56);
    }
    public void SetPosition(InputButtons action, Vec2 normalizedSafePosition)
    {
        if (!normalizedSafePosition.IsFinite || normalizedSafePosition.X is < 0 or > 1 || normalizedSafePosition.Z is < 0 or > 1) { throw new ArgumentOutOfRangeException(nameof(normalizedSafePosition)); }
        for (int i = 0; i < _targets.Length; i++)
        {
            if (_targets[i].Action != action) { continue; }
            Vec2 centre = Minimum + new Vec2((Maximum.X - Minimum.X) * normalizedSafePosition.X, (Maximum.Z - Minimum.Z) * normalizedSafePosition.Z);
            float radius = _targets[i].Radius; centre = Clamp(centre, radius + PaddingDp * Scale); _targets[i] = new(action, centre, radius * 2); return;
        }
        throw new ArgumentOutOfRangeException(nameof(action));
    }
    /// <summary>Applies canonical right-handed positions together, rejecting overlap without partial edits.</summary>
    public bool TrySetPositions(ReadOnlySpan<TouchPlacement> positions)
    {
        Span<TouchTarget> targets = stackalloc TouchTarget[4]; _targets.CopyTo(targets); InputButtons seen = 0;
        foreach (TouchPlacement position in positions)
        {
            Vec2 point = position.Normalized;
            if (!point.IsFinite || point.X is < 0 or > 1 || point.Z is < 0 or > 1 || (seen & position.Action) != 0) { return false; }
            int index = -1; for (int i = 0; i < targets.Length; i++) { if (targets[i].Action == position.Action) { index = i; break; } }
            if (index < 0) { return false; } seen |= position.Action;
            if (LeftHanded) { point = new(1 - point.X, point.Z); }
            Vec2 centre = Minimum + new Vec2((Maximum.X - Minimum.X) * point.X, (Maximum.Z - Minimum.Z) * point.Z);
            targets[index] = new(position.Action, Clamp(centre, targets[index].Radius + PaddingDp * Scale), targets[index].Radius * 2);
        }
        for (int i = 0; i < targets.Length; i++) { for (int j = i + 1; j < targets.Length; j++) { if ((targets[i].Centre - targets[j].Centre).Length + .001f < targets[i].Radius + targets[j].Radius + PaddingDp * Scale) { return false; } } }
        targets.CopyTo(_targets); return true;
    }
    public bool MovementSide(Vec2 point) => point.X >= Minimum.X && point.X <= Maximum.X && point.Z >= Minimum.Z && point.Z <= Maximum.Z && (LeftHanded ? point.X > (Minimum.X + Maximum.X) / 2 : point.X < (Minimum.X + Maximum.X) / 2);
    public Vec2 ClampJoystick(Vec2 point) => Clamp(point, JoystickRadius);
    private Vec2 Clamp(Vec2 point, float margin) => new(MathUtil.Clamp(point.X, Minimum.X + margin, Maximum.X - margin), MathUtil.Clamp(point.Z, Minimum.Z + margin, Maximum.Z - margin));
    private void Set(int index, InputButtons action, Vec2 centre, float diameter)
    {
        if (LeftHanded) { centre = new(Minimum.X + Maximum.X - centre.X, centre.Z); }
        _targets[index] = new(action, centre, diameter * Scale);
    }
}
