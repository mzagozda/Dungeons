using System.Globalization;
using Dungeons.Core.Level;
using Dungeons.Game.Persistence;
using Godot;

namespace Dungeons.Game.UI;

/// <summary>Focusable card; broken levels remain selectable for diagnosis, while Play is gated by details.</summary>
public partial class LevelCard : PanelContainer
{
    public CatalogEntry Entry { get; private set; } = null!;
    public Button SelectButton { get; private set; } = null!;
    public TextureRect Picture { get; private set; } = null!;
    public bool Completed { get; private set; }
    public event Action<CatalogEntry>? Activated;
    public event Action<CatalogEntry>? DiagnosticRequested;
    private AspectRatioContainer _ratio = null!;
    private Label _placeholder = null!, _title = null!;
    private int _finger = -1;
    private Vector2 _pressPosition;
    private double _held;
    private bool _suppress;
    public void Build(CatalogEntry entry, LevelRecord? record)
    {
        Entry = entry; Completed = record?.Completed == true; Name = "LevelCard";
        AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        string first = entry.Diagnostics.FirstOrDefault(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)?.ToString()
            ?? entry.Diagnostics.FirstOrDefault(diagnostic => diagnostic.Severity != DiagnosticSeverity.Info)?.ToString() ?? "";
        SelectButton = new() { Name = "Select", TooltipText = entry.Name + (first.Length == 0 ? "" : "\n" + first), FocusMode = FocusModeEnum.All };
        AddChild(SelectButton); SelectButton.GuiInput += CardInput;
        SelectButton.Pressed += () => { if (!_suppress) { Activated?.Invoke(Entry); } _suppress = false; };
        var margin = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore }; AddChild(margin);
        foreach (string side in new[] { "left", "right", "top", "bottom" }) { margin.AddThemeConstantOverride("margin_" + side, 12); }
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore }; margin.AddChild(box); box.AddThemeConstantOverride("separation", 8);
        _ratio = new() { Ratio = 16f / 9, StretchMode = AspectRatioContainer.StretchModeEnum.WidthControlsHeight, MouseFilter = MouseFilterEnum.Ignore }; box.AddChild(_ratio);
        var frame = new Control { MouseFilter = MouseFilterEnum.Ignore }; _ratio.AddChild(frame);
        var backdrop = new ColorRect { Color = new("#3c3c46"), MouseFilter = MouseFilterEnum.Ignore }; frame.AddChild(backdrop); backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Picture = new() { Name = "Preview", ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = MouseFilterEnum.Ignore, TextureFilter = TextureFilterEnum.Nearest };
        frame.AddChild(Picture); Picture.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _placeholder = new() { Text = Tr("LEVEL_NO_PREVIEW"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = MouseFilterEnum.Ignore };
        frame.AddChild(_placeholder); _placeholder.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        if (!entry.IsPlayable)
        {
            var shade = new ColorRect { Color = new Color("#1a1a1f") with { A = .8f }, MouseFilter = MouseFilterEnum.Ignore }; frame.AddChild(shade); shade.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            var warning = new Label { Text = "⚠ " + Tr("LEVEL_INVALID"), AutowrapMode = TextServer.AutowrapMode.WordSmart, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
            warning.AddThemeColorOverride("font_color", new("#ff5a3c")); frame.AddChild(warning); warning.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        }
        _title = Caption(box, entry.Name, 2); _title.Name = "LevelName";
        Caption(box, string.Format(Tr("LEVEL_AUTHOR"), entry.Author.Length == 0 ? Tr("LEVEL_UNKNOWN_AUTHOR") : entry.Author), 1);
        var pips = new DifficultyPips { TooltipText = string.Format(Tr("DIFFICULTY"), entry.Difficulty) }; box.AddChild(pips); pips.Bind(entry.Difficulty);
        Caption(box, Completed ? string.Format(Tr("LEVEL_BEST_TIME"), FormatTime(record!.BestTimeTicks)) : Tr("LEVEL_NO_RECORD"), 1);
        var badges = new HFlowContainer { MouseFilter = MouseFilterEnum.Ignore }; box.AddChild(badges);
        if (Completed) { Badge(badges, "COMPLETED", "#e2b644"); }
        if (entry.Source == LevelSource.User) { Badge(badges, "USER_LEVEL", "#92c25d"); }
        SetCardWidth(340);
    }
    public void SetCardWidth(float width, bool compact = false)
    {
        Vector2 minimum = new(MathF.Max(80, width), 0); if (CustomMinimumSize != minimum) { CustomMinimumSize = minimum; }
        float height = MathF.Max(1, width - 24) * 9 / 16;
        var stretch = compact ? AspectRatioContainer.StretchModeEnum.Fit : AspectRatioContainer.StretchModeEnum.WidthControlsHeight;
        if (_ratio.StretchMode != stretch) { _ratio.StretchMode = stretch; }
        if (compact) { height = MathF.Min(height, 80); }
        if (_ratio.CustomMinimumSize.Y != height) { _ratio.CustomMinimumSize = new(0, height); }
        var box = (VBoxContainer)_title.GetParent(); int titleIndex = compact ? 0 : 1;
        if (_title.GetIndex() != titleIndex) { box.MoveChild(_title, titleIndex); }
        int font = (int)MathF.Round(GetThemeDefaultFontSize() * (compact ? 1.25f : 1.5f)); if (_title.GetThemeFontSize("font_size") != font) { _title.AddThemeFontSizeOverride("font_size", font); }
    }
    public void SetPreview(Texture2D? texture)
    { Picture.Texture = texture; _placeholder.Visible = texture is null && Entry.IsPlayable; }
    public override void _Process(double delta)
    {
        if (_finger < 0) { return; } _held += delta;
        if (_held >= .55 && Entry.Diagnostics.Any(diagnostic => diagnostic.Severity != DiagnosticSeverity.Info))
        { _finger = -1; _suppress = true; DiagnosticRequested?.Invoke(Entry); }
    }
    private void CardInput(InputEvent input)
    {
        if (input is InputEventMouseButton { Pressed: true }) { _suppress = false; }
        if (input is InputEventScreenTouch touch)
        {
            if (touch.Pressed && _finger < 0) { _finger = touch.Index; _pressPosition = touch.Position; _held = 0; _suppress = false; }
            else if (!touch.Pressed && touch.Index == _finger) { _finger = -1; }
        }
        else if (input is InputEventScreenDrag drag && drag.Index == _finger && drag.Position.DistanceTo(_pressPosition) > 12)
        { _finger = -1; _suppress = true; }
    }
    private static Label Caption(Node parent, string text, int lines)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, MaxLinesVisible = lines, MouseFilter = MouseFilterEnum.Ignore };
        parent.AddChild(label); return label;
    }
    private void Badge(Node parent, string key, string colour)
    {
        var ribbon = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore }; parent.AddChild(ribbon);
        ribbon.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new(colour), ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 4, ContentMarginBottom = 4 });
        var label = new Label { Text = Tr(key), MouseFilter = MouseFilterEnum.Ignore }; ribbon.AddChild(label);
        label.AddThemeColorOverride("font_color", new("#1a1a1f"));
    }
    public static string FormatTime(int ticks) => string.Create(CultureInfo.InvariantCulture, $"{ticks / 3600:00}:{ticks / 60 % 60:00}");
}
