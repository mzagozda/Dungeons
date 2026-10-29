using Godot;

namespace Dungeons.Game.UI;

/// <summary>Eight disjoint square edge bands keep the centre clear and alpha at or below 0.3.</summary>
public partial class LowHealthVignette : Control
{
    public LowHealthVignette() { MouseFilter = MouseFilterEnum.Ignore; }
    public override void _Notification(int what) { if (what == NotificationResized) { QueueRedraw(); } }
    public override void _Draw()
    {
        float width = Size.X, height = Size.Y, band = MathF.Min(width, height) * .12f / 8;
        if (band <= 0) { return; }
        for (int i = 0; i < 8; i++)
        {
            float inset = i * band, x = width - inset * 2, y = height - inset * 2;
            Color tint = new Color("#c94444") with { A = LowHealthFeedback.VignetteOpacity * (1 - i / 8f) * (1 - i / 8f) };
            DrawRect(new(inset, inset, x, band), tint); DrawRect(new(inset, height - inset - band, x, band), tint);
            DrawRect(new(inset, inset + band, band, y - band * 2), tint); DrawRect(new(width - inset - band, inset + band, band, y - band * 2), tint);
        }
    }
}
