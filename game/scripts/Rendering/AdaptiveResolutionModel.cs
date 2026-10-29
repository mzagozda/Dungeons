namespace Dungeons.Game.Rendering;

/// <summary>Frame-based mobile resolution policy, independent of Godot and simulation state.</summary>
public sealed class AdaptiveResolutionModel
{
    public const int WindowFrames = 60, GoodFramesToRaise = 300;
    public const double NormalThresholdMs = 18, ThermalThresholdMs = 16, ThermalSeconds = 600;
    private readonly double[] _samples = new double[WindowFrames];
    private double _sum, _baselineMs, _activeSeconds;
    private int _head, _count, _goodFrames, _scalePercent, _maximumPercent;
    public AdaptiveResolutionModel(float initial = 1, float maximum = 1) => Configure(initial, maximum);
    public float Scale => _scalePercent / 100f;
    public double AverageFrameMs { get; private set; }
    public bool ThermalLimited { get; private set; }
    public double ThresholdMs => ThermalLimited ? ThermalThresholdMs : NormalThresholdMs;
    public double ActiveSeconds => _activeSeconds;
    public void Configure(float initial, float maximum)
    {
        if (!float.IsFinite(initial) || !float.IsFinite(maximum) || maximum is < .6f or > 1 || initial < .6f || initial > maximum) { throw new ArgumentOutOfRangeException(nameof(initial)); }
        _maximumPercent = (int)MathF.Round(maximum * 100); _scalePercent = (int)MathF.Round(initial * 100); _baselineMs = 0; ResetWindow();
    }
    /// <summary>Discards pacing samples after pause/load without resetting the thermal history.</summary>
    public void ResetWindow() { Array.Clear(_samples); _head = _count = _goodFrames = 0; _sum = 0; AverageFrameMs = 0; }
    /// <summary>Returns true only when the renderer needs a different scale.</summary>
    public bool Sample(double frameMs)
    {
        if (!double.IsFinite(frameMs) || frameMs <= 0) { return false; }
        _activeSeconds += frameMs / 1000; _sum -= _samples[_head]; _samples[_head] = frameMs; _sum += frameMs;
        _head = (_head + 1) % WindowFrames; _count = System.Math.Min(_count + 1, WindowFrames); AverageFrameMs = _sum / _count;
        if (_count == WindowFrames && _baselineMs == 0) { _baselineMs = AverageFrameMs; }
        if (_activeSeconds >= ThermalSeconds && _baselineMs > 0 && _count == WindowFrames && AverageFrameMs > _baselineMs * 1.2) { ThermalLimited = true; }
        _goodFrames = frameMs <= ThresholdMs && AverageFrameMs <= ThresholdMs ? _goodFrames + 1 : 0;
        if (_count == WindowFrames && AverageFrameMs > ThresholdMs && _scalePercent > 60)
        { _scalePercent = System.Math.Max(60, _scalePercent - 5); ResetWindow(); return true; }
        if (_goodFrames >= GoodFramesToRaise && _scalePercent < _maximumPercent)
        { _scalePercent = System.Math.Min(_maximumPercent, _scalePercent + 5); ResetWindow(); return true; }
        return false;
    }
}
