using Dungeons.Core.Sim;
using Godot;

namespace Dungeons.Game.UI;

/// <summary>First active objective has a diamond; remaining active goals keep authored order in a scrollable grey list.</summary>
public partial class HudObjectives : Control
{
    public Label Primary { get; } = new() { Name = "Primary", AutowrapMode = TextServer.AutowrapMode.WordSmart, MaxLinesVisible = 2, MouseFilter = MouseFilterEnum.Ignore };
    private readonly Label _diamond = new() { Text = "◇", MouseFilter = MouseFilterEnum.Ignore };
    private readonly ScrollContainer _scroll = new() { Name = "SecondaryObjectives", HorizontalScrollMode = ScrollContainer.ScrollMode.ShowNever };
    private readonly VBoxContainer _list = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private readonly List<Label> _rows = [];
    private MissionState? _mission;
    private int _primaryIndex = -2, _secondaryFont;
    public int SecondaryCount { get; private set; }
    public HudObjectives()
    {
        MouseFilter = MouseFilterEnum.Ignore; AddChild(_diamond); AddChild(Primary); AddChild(_scroll); _scroll.AddChild(_list);
        _diamond.AddThemeColorOverride("font_color", new("#e2b644"));
    }
    public void Bind(MissionState mission, Func<string, string> localize)
    {
        if (ReferenceEquals(_mission, mission)) { return; }
        _mission = mission; _primaryIndex = -2; _secondaryFont = 0;
        while (_rows.Count < mission.Objectives.Count)
        {
            var label = new Label { Name = "Secondary_" + _rows.Count, AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = MouseFilterEnum.Ignore };
            label.AddThemeColorOverride("font_color", new("#a7aaad")); _list.AddChild(label); _rows.Add(label);
        }
        for (int i = 0; i < _rows.Count; i++) { _rows[i].Visible = false; if (i < mission.Objectives.Count) { _rows[i].Text = localize(mission.Objectives[i].Text); } }
    }
    public void Refresh(Func<string, string> localize)
    {
        if (_mission is null) { return; } int primary = -1; SecondaryCount = 0;
        for (int i = 0; i < _mission.Objectives.Count; i++)
        {
            bool active = _mission.Objectives[i].State == ObjectiveState.Active;
            if (active && primary < 0) { primary = i; }
            bool secondary = active && i != primary; _rows[i].Visible = secondary; if (secondary) { SecondaryCount++; }
        }
        if (_primaryIndex != primary) { _primaryIndex = primary; Primary.Text = primary < 0 ? "" : localize(_mission.Objectives[primary].Text); }
        Visible = primary >= 0; _diamond.Visible = primary >= 0;
    }
    public float Arrange(Vector2 position, float width, float limit, int fontSize, int secondaryFont, float gap)
    {
        Position = position; width = MathF.Max(1, width);
        if (Primary.GetThemeFontSize("font_size") != fontSize) { Primary.AddThemeFontSizeOverride("font_size", fontSize); }
        if (_diamond.GetThemeFontSize("font_size") != secondaryFont) { _diamond.AddThemeFontSizeOverride("font_size", secondaryFont); }
        if (_secondaryFont != secondaryFont)
        { foreach (Label row in _rows) { row.AddThemeFontSizeOverride("font_size", secondaryFont); } _secondaryFont = secondaryFont; }
        Primary.Position = new(fontSize, 0); Primary.Size = new(MathF.Max(1, width - fontSize), 0);
        float header = Visible ? Primary.GetCombinedMinimumSize().Y : 0; Primary.Size = new(Primary.Size.X, header); _diamond.Size = new(fontSize, header);
        _scroll.Position = new(fontSize, header + gap); _scroll.Size = new(MathF.Max(1, width - fontSize), MathF.Max(0, MathF.Min(_list.GetCombinedMinimumSize().Y, limit - header - gap)));
        _scroll.Visible = SecondaryCount > 0 && _scroll.Size.Y > 0;
        float height = header + (_scroll.Visible ? gap + _scroll.Size.Y : 0); Size = new(width, height); return height;
    }
}
