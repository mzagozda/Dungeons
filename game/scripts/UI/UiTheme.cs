using Godot;

namespace Dungeons.Game.UI;

/// <summary>Installs one inherited pixel theme after imports, before application/QA scenes create controls.</summary>
public partial class UiTheme : Node
{
    public override void _Ready()
    {
        // ProjectSettings gui/theme/custom loads before a clean editor has imported
        // referenced textures/fonts. Runtime installation keeps first imports strict.
        if (Engine.IsEditorHint()) { return; }
        ApplyTextScale(GetTree().Root, 1);
    }
    /// <summary>Text accessibility scales the shared theme without discarding its font or control styles.</summary>
    public static void ApplyTextScale(Window window, float scale)
    {
        Theme source = GD.Load<Theme>("res://assets/ui/block_dungeons.tres")
            ?? throw new InvalidDataException("The shared pixel UI theme is missing.");
        var font = (FontFile)source.DefaultFont;
        font.Fallbacks = [GD.Load<FontFile>("res://assets/fonts/block_symbols.fnt")];
        int size = (int)MathF.Round(16 * scale);
        // Window/Control themes stop at intervening CanvasLayer/Node3D nodes.
        // Install the skin in the runtime fallback theme as well, so all HUD,
        // menu, popup and isolated preview branches receive the same resources.
        Theme global = ThemeDB.GetDefaultTheme(); global.MergeWith(source);
        global.DefaultFont = font; global.DefaultFontSize = size;
        foreach (string type in global.GetFontTypeList())
        { foreach (string name in global.GetFontList(type)) { global.SetFont(name, type, font); } }
        foreach (string type in global.GetFontSizeTypeList())
        { foreach (string name in global.GetFontSizeList(type)) { global.SetFontSize(name, type, size); } }
        ThemeDB.FallbackFont = font; ThemeDB.FallbackFontSize = size;
        var theme = (Theme)source.Duplicate(); theme.DefaultFontSize = size; window.Theme = theme;
    }
}
