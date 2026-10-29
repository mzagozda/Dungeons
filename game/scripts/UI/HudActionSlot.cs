using System.Globalization;
using Godot;

namespace Dungeons.Game.UI;

/// <summary>Original pixel bottle/bow, snapshot counters and clockwise cooldown sweep.</summary>
public partial class HudActionSlot : Control
{
    private readonly Vector2[] _wedge = new Vector2[34];
    private static readonly Vector2[] BowOutline = [new(18, 10), new(30, 14), new(38, 26), new(38, 38), new(30, 50), new(18, 54)];
    private bool _potion, _enabled = true;
    private int _remaining = -1, _arrows = -1;
    private int _displayValue = int.MinValue;
    private float _fraction = -1;
    private string _number = "";
    public float CooldownFraction => _fraction;
    public string CounterText => _number;
    public HudActionSlot() { MouseFilter = MouseFilterEnum.Ignore; ClipContents = true; }
    public void Bind(bool potion, int remaining, float fraction, int arrows, bool enabled)
    {
        if (_potion == potion && _remaining == remaining && _fraction == fraction && _arrows == arrows && _enabled == enabled) { return; }
        int value = potion ? HudCooldown.Seconds(remaining) : arrows;
        if (_displayValue != value || _potion != potion) { _number = potion && remaining <= 0 ? "" : value.ToString(CultureInfo.InvariantCulture); _displayValue = value; }
        _potion = potion; _remaining = remaining; _fraction = fraction; _arrows = arrows; _enabled = enabled;
        QueueRedraw();
    }
    public override void _Notification(int what) { if (what == NotificationResized || what == NotificationThemeChanged) { QueueRedraw(); } }
    public override void _Draw()
    {
        float scale = MathF.Min(Size.X, Size.Y) / 64;
        if (scale <= 0) { return; }
        DrawSetTransform(Vector2.Zero, 0, new(scale, scale));
        DrawRect(new(0, 0, 64, 64), new Color("#1a1a1f")); DrawRect(new(1, 1, 62, 62), new Color("#e2b644"), false, 2);
        bool cooling = !_enabled || (_potion ? _remaining > 0 : _arrows <= 0);
        Color ink = new(cooling ? "#7d8185" : "#e2b644");
        if (_potion)
        {
            DrawRect(new(24, 8, 16, 8), new Color("#b99563")); DrawRect(new(24, 16, 16, 12), ink);
            DrawRect(new(16, 24, 32, 28), ink); DrawRect(new(20, 28, 24, 20), new Color(cooling ? "#3c3c46" : "#ff5a3c"));
            DrawRect(new(23, 30, 4, 9), new Color("#ced0c5"));
            if (_fraction > 0) { CooldownSweep.Draw(this, _wedge, new(32, 32), 46, _fraction, new Color(.12f, .12f, .15f, .65f)); }
        }
        else
        {
            DrawPolyline(BowOutline, ink, 4);
            DrawLine(new(18, 10), new(18, 54), new Color("#ced0c5"), 2);
            DrawLine(new(10, 30), new(53, 30), ink, 3); DrawLine(new(46, 23), new(53, 30), ink, 3); DrawLine(new(46, 37), new(53, 30), ink, 3);
        }
        if (_number.Length > 0)
        {
            Font font = GetThemeDefaultFont(); int size = 18; float textWidth = font.GetStringSize(_number, fontSize: size).X;
            if (textWidth > 58) { size = Math.Max(8, (int)(size * 58 / textWidth)); textWidth = font.GetStringSize(_number, fontSize: size).X; }
            Vector2 baseline = new((64 - textWidth) / 2, 59);
            DrawStringOutline(font, baseline, _number, fontSize: size, size: 3, modulate: Colors.Black); DrawString(font, baseline, _number, fontSize: size, modulate: Colors.White);
        }
        DrawSetTransform(Vector2.Zero);
    }
}

/// <summary>Shared by the square desktop slot and the native touch potion target.</summary>
public static class CooldownSweep
{
    public static void Draw(CanvasItem canvas, Vector2[] wedge, Vector2 centre, float radius, float fraction, Color tint)
    {
        if (fraction <= 0) { return; }
        if (fraction >= 1) { canvas.DrawCircle(centre, radius, tint); return; }
        wedge[0] = centre;
        for (int i = 0; i <= 32; i++) { float angle = -Mathf.Pi / 2 + Mathf.Tau * fraction * i / 32; wedge[i + 1] = centre + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius; }
        canvas.DrawColoredPolygon(wedge, tint);
    }
}
