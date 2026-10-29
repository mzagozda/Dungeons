using Godot;

namespace Dungeons.Game.UI;

/// <summary>Original 32px chain glyph shown near a mission-locked exit.</summary>
public partial class ExitLockIndicator : Control
{
    public ExitLockIndicator() { MouseFilter = MouseFilterEnum.Ignore; CustomMinimumSize = new(32, 32); }
    public override void _Draw()
    {
        DrawRect(new Rect2(0, 0, 32, 32), new Color("#1a1a1f"));
        DrawSetTransform(new(16, 16), -.65f);
        var gold = new Color("#e2b644");
        DrawRect(new Rect2(-10, -8, 13, 10), gold, filled: false, width: 3);
        DrawRect(new Rect2(-3, -2, 13, 10), gold, filled: false, width: 3);
        DrawLine(new(-2, -2), new(2, 2), Colors.White, 2);
        DrawSetTransform(Vector2.Zero);
    }
}
