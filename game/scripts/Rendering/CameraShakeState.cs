namespace Dungeons.Game.Rendering;

/// <summary>Independent, bounded presentation noise. Evaluating it never consumes simulation RNG.</summary>
public sealed class CameraShakeState
{
    private float _strength;
    private double _start, _end;
    public void Clear() { _strength = 0; _start = _end = 0; }
    public void Trigger(double time, float strength, float seconds)
    {
        if (!double.IsFinite(time) || !float.IsFinite(strength) || !float.IsFinite(seconds) || seconds <= 0 || strength <= 0) { return; }
        // Retain the stronger overlapping impulse; new events refresh its decay.
        _strength = System.Math.Min(.25f, System.Math.Max(Strength(time), strength)); _start = time; _end = time + seconds;
    }
    public float Strength(double time) => time >= _end || _end <= _start ? 0 : _strength * (float)System.Math.Clamp((_end - time) / (_end - _start), 0, 1);
    public (float X, float Y) Offset(double time)
    {
        float amplitude = Strength(time), t = (float)(time - _start);
        // Each axis is bounded by 1/sqrt(2), so combined displacement stays <= amplitude.
        return (.70710677f * amplitude * MathF.Sin(t * 113 + 1.7f), .70710677f * amplitude * MathF.Sin(t * 157 + .4f));
    }
}
