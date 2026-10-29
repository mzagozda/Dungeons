using Dungeons.Game.Persistence;
using Godot;

namespace Dungeons.Game.UI;

/// <summary>Shape cue remains legible without hue; the owning bar clips it to current health.</summary>
public partial class HealthBarPattern : Control
{
    public HealthBarPattern() { MouseFilter = MouseFilterEnum.Ignore; ClipContents = true; }
    public override void _Draw()
    {
        for (float x = -Size.Y; x < Size.X; x += 12) { DrawLine(new(x, Size.Y), new(x + Size.Y, 0), new Color(1, 1, 1, .65f), 2); }
    }
    public static Color Tint(HealthPalette palette, bool enemy = false) => new(palette switch
    {
        HealthPalette.Protanopia => enemy ? "#e69f00" : "#0072b2",
        HealthPalette.Deuteranopia => enemy ? "#d55e00" : "#0072b2",
        HealthPalette.Tritanopia => enemy ? "#009e73" : "#cc79a7",
        _ => enemy ? "#d34444" : "#c94444",
    });
}
