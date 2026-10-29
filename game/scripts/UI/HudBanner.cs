using Godot;

namespace Dungeons.Game.UI;

/// <summary>Non-interactive, bounded two-line dark pill; opacity applies to text and background together.</summary>
public partial class HudBanner : PanelContainer
{
    public Label TextLabel { get; } = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart, MaxLinesVisible = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
    private readonly HBoxContainer _body = new() { MouseFilter = MouseFilterEnum.Ignore };
    private Control? _icon, _iconHolder;
    private bool _leadingVisible;
    private readonly StyleBoxFlat _panel = new() { BgColor = new Color("#1a1a1f") with { A = .94f }, CornerRadiusTopLeft = 10, CornerRadiusTopRight = 10, CornerRadiusBottomLeft = 10, CornerRadiusBottomRight = 10, CornerDetail = 1, AntiAliasing = false };
    public HudBanner() { MouseFilter = MouseFilterEnum.Ignore; AddThemeStyleboxOverride("panel", _panel); AddChild(_body); _body.AddChild(TextLabel); _body.AddThemeConstantOverride("separation", 8); }
    public void AddLeading(Control icon)
    {
        _icon = icon; _iconHolder = new() { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        _body.AddChild(_iconHolder); _body.MoveChild(_iconHolder, 0); _iconHolder.AddChild(icon);
    }
    public void ShowLeading(bool visible) => _leadingVisible = visible;
    public float Configure(float width, int fontSize, int padding)
    {
        if (TextLabel.GetThemeFontSize("font_size") != fontSize) { TextLabel.AddThemeFontSizeOverride("font_size", fontSize); }
        if (_panel.ContentMarginLeft != padding * 2) { _panel.ContentMarginLeft = _panel.ContentMarginRight = padding * 2; _panel.ContentMarginTop = _panel.ContentMarginBottom = padding; }
        float iconSize = 0;
        if (_iconHolder is not null && _icon is not null)
        {
            _iconHolder.Visible = _leadingVisible && _icon.Visible;
            if (_iconHolder.Visible) { iconSize = fontSize * 4 / 3f; _iconHolder.CustomMinimumSize = Vector2.One * iconSize; _icon.Scale = Vector2.One * (iconSize / 32); }
        }
        TextLabel.Size = new(MathF.Max(1, width - padding * 4 - (iconSize > 0 ? iconSize + 8 : 0)), 0);
        float height = MathF.Max(TextLabel.GetCombinedMinimumSize().Y, iconSize) + padding * 2; Size = new(width, height); return height;
    }
    public void SetOpacity(float alpha)
    {
        Visible = TextLabel.Text.Length > 0 && alpha > 0;
        if (Modulate.A != alpha) { Modulate = Colors.White with { A = alpha }; }
    }
}
