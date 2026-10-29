using Godot;

namespace Dungeons.Game.UI;

/// <summary>Live safe-area frame shared by menu, catalogue and details pages.</summary>
public partial class MenuLayout : MarginContainer
{
    private Label? _title;
    private (Vector2 start, Vector2 end)? _previewInsets;
    public void BindTitle(Label title) => _title = title;
    public void SetSafeInsets(Vector2 start, Vector2 end) => _previewInsets = (start, end);
    public override void _Process(double delta)
    {
        Vector2 start = Vector2.Zero, end = Vector2.Zero;
        if (_previewInsets is { } preview) { start = preview.start; end = preview.end; }
        else if (OS.HasFeature("mobile"))
        {
            Rect2I safe = DisplayServer.GetDisplaySafeArea(); Window window = GetWindow(); Vector2 pixels = window.Size;
            if (safe.Size.X > 0 && safe.Size.Y > 0 && pixels.X > 0 && pixels.Y > 0)
            {
                Vector2 position = (Vector2)safe.Position - (Vector2)window.Position, factor = GetViewport().GetVisibleRect().Size / pixels;
                start = position.Clamp(Vector2.Zero, pixels) * factor; end = (pixels - position - (Vector2)safe.Size).Clamp(Vector2.Zero, pixels) * factor;
            }
        }
        bool compact = GetViewport().GetVisibleRect().Size.Y < 420; int padding = compact ? 12 : 48;
        SetMargin("left", padding + (int)MathF.Ceiling(start.X)); SetMargin("top", padding + (int)MathF.Ceiling(start.Y));
        SetMargin("right", padding + (int)MathF.Ceiling(end.X)); SetMargin("bottom", padding + (int)MathF.Ceiling(end.Y));
        int titleSize = (int)MathF.Round((compact ? 16 : 36) * GetThemeDefaultFontSize() / 16f);
        if (_title is not null && _title.GetThemeFontSize("font_size") != titleSize) { _title.AddThemeFontSizeOverride("font_size", titleSize); }
    }
    private void SetMargin(string side, int value)
    { if (GetThemeConstant("margin_" + side) != value) { AddThemeConstantOverride("margin_" + side, value); } }
}
