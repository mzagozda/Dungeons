using System.Globalization;
using Godot;

namespace Dungeons.Game.UI;

/// <summary>Original emerald and cuboid hero-head counters avoid verbose labels on narrow HUDs.</summary>
public partial class HudCounters : Control
{
    private int _emeralds = -1, _lives;
    private float _scale = 1;
    private string _number = "0";
    public HudCounters() { MouseFilter = MouseFilterEnum.Ignore; }
    public float DesiredWidth { get; private set; } = 150;
    public void Bind(int emeralds, int lives, float textScale)
    {
        if (_emeralds == emeralds && _lives == lives && _scale == textScale) { return; }
        _emeralds = emeralds; _lives = lives; _scale = textScale; _number = emeralds.ToString(CultureInfo.InvariantCulture);
        DesiredWidth = 24 * _scale + GetThemeDefaultFont().GetStringSize(_number, fontSize: (int)(20 * _scale)).X + 16 + 3 * 20 * _scale; QueueRedraw();
    }
    public override void _Draw()
    {
        float s = _scale; Font font = GetThemeDefaultFont(); int fontSize = (int)(20 * s);
        Vector2 origin = new(0, 6 * s);
        DrawColoredPolygon([origin + new Vector2(8 * s, 0), origin + new Vector2(16 * s, 8 * s), origin + new Vector2(8 * s, 16 * s), origin + new Vector2(0, 8 * s)], new Color("#49cf77"));
        DrawLine(origin + new Vector2(8 * s, 2 * s), origin + new Vector2(3 * s, 8 * s), new Color("#c8ffb3"), 2 * s);
        Vector2 baseline = new(24 * s, font.GetAscent(fontSize)); DrawStringOutline(font, baseline, _number, fontSize: fontSize, size: 2, modulate: Colors.Black); DrawString(font, baseline, _number, fontSize: fontSize, modulate: Colors.White);
        float start = 24 * s + font.GetStringSize(_number, fontSize: fontSize).X + 16;
        for (int i = 0; i < 3; i++)
        {
            Vector2 head = new(start + i * 20 * s, 4 * s); Color face = i < _lives ? new("#a46d42") : new("#3c3c42");
            DrawRect(new(head, new(16 * s, 20 * s)), face); DrawRect(new(head, new(16 * s, 5 * s)), new Color("#352415"));
            DrawRect(new(head + new Vector2(3 * s, 8 * s), new(3 * s, 3 * s)), Colors.Black); DrawRect(new(head + new Vector2(10 * s, 8 * s), new(3 * s, 3 * s)), Colors.Black);
        }
    }
}
