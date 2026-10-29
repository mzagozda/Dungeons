using Dungeons.Core.Sim;

namespace Dungeons.Game.UI;

/// <summary>One fade rule for authored messages and presentation-only toasts.</summary>
public static class HudBannerFade
{
    public const double Seconds = .2;
    public static float Alpha(double age, double remaining) => (float)System.Math.Clamp(System.Math.Min(age / Seconds, remaining / Seconds), 0, 1);
    public static double Delta(double delta, bool paused) => !paused && double.IsFinite(delta) && delta > 0 ? delta : 0;
}

/// <summary>Core owns message expiry. This clock supplies only a smooth entrance and the final fade.</summary>
public sealed class MissionBannerState
{
    private double _age;
    private int _lastTicks;
    private bool _retrigger;
    public string Text { get; private set; } = "";
    public float Alpha { get; private set; }
    public void Retrigger() => _retrigger = true;
    public void Clear() { Text = ""; Alpha = 0; _age = 0; _lastTicks = 0; _retrigger = false; }
    public void Update(string text, int remainingTicks, double delta, bool paused)
    {
        if (text.Length == 0 || remainingTicks <= 0) { Clear(); return; }
        if (Text != text || remainingTicks > _lastTicks || _retrigger) { _age = 0; }
        Text = text; _lastTicks = remainingTicks; _retrigger = false;
        _age = System.Math.Min(HudBannerFade.Seconds, _age + HudBannerFade.Delta(delta, paused));
        Alpha = HudBannerFade.Alpha(_age, remainingTicks / (double)GameConstants.TickRate);
    }
}

/// <summary>Bounded newest-wins toast, independent of the authored mission message.</summary>
public sealed class HudToastState
{
    private double _age, _remaining;
    public string Text { get; private set; } = "";
    public float Alpha { get; private set; }
    public void Show(string text, double seconds)
    {
        Text = text.Length > 0 && double.IsFinite(seconds) && seconds > 0 ? text : "";
        _remaining = Text.Length == 0 ? 0 : seconds; _age = 0; Alpha = 0;
    }
    public void Advance(double delta, bool paused)
    {
        double step = HudBannerFade.Delta(delta, paused);
        _age = System.Math.Min(HudBannerFade.Seconds, _age + step); _remaining = System.Math.Max(0, _remaining - step);
        if (_remaining == 0) { Text = ""; Alpha = 0; } else { Alpha = HudBannerFade.Alpha(_age, _remaining); }
    }
}
