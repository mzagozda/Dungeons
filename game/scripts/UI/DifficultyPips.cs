using Godot;

namespace Dungeons.Game.UI;

/// <summary>Five original square difficulty pips; invalid metadata never expands the drawing loop.</summary>
public partial class DifficultyPips : Control
{
    private int _difficulty;
    public DifficultyPips() { MouseFilter = MouseFilterEnum.Ignore; }
    public void Bind(int difficulty) { _difficulty = System.Math.Clamp(difficulty, 1, 5); QueueRedraw(); }
    public override void _Process(double delta)
    { float scale = GetThemeDefaultFontSize() / 16f; Vector2 size = new(100 * scale, 16 * scale); if (CustomMinimumSize != size) { CustomMinimumSize = size; QueueRedraw(); } }
    public override void _Draw()
    {
        float scale = GetThemeDefaultFontSize() / 16f;
        for (int i = 0; i < 5; i++) { DrawRect(new Rect2(new Vector2(i * 18, 2) * scale, new Vector2(12, 12) * scale), new Color(i < _difficulty ? "#e2b644" : "#3c3c46")); }
    }
}
