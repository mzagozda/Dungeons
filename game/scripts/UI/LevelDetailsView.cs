using Dungeons.Core.Level;
using Dungeons.Game.Persistence;
using Godot;

namespace Dungeons.Game.UI;

/// <summary>Scrollable description, preview and persisted records; invalid entries need no simulation document.</summary>
public partial class LevelDetailsView : ScrollContainer
{
    private readonly LevelPreviewPool _preview = new();
    private TextureRect _picture = null!;
    public void Build(CatalogEntry entry, LevelRecord? record, Action showDiagnostics)
    {
        Name = "LevelDetails"; FollowFocus = true; HorizontalScrollMode = ScrollMode.Disabled; SizeFlagsVertical = SizeFlags.ExpandFill;
        var body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; AddChild(body); body.AddThemeConstantOverride("separation", 16);
        _picture = new() { Name = "Preview", Texture = _preview.Get(entry), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, SizeFlagsHorizontal = SizeFlags.ShrinkCenter, TextureFilter = TextureFilterEnum.Nearest };
        body.AddChild(_picture); _picture.CustomMinimumSize = new(320, 180);
        if (_picture.Texture is null) { Caption(body, "NoPreview", Tr("LEVEL_NO_PREVIEW")); }
        Caption(body, "Author", string.Format(Tr("LEVEL_AUTHOR"), entry.Author.Length > 0 ? entry.Author : Tr("LEVEL_UNKNOWN_AUTHOR")));
        Caption(body, "Identity", entry.Id + "  v" + entry.Version);
        var pips = new DifficultyPips { TooltipText = string.Format(Tr("DIFFICULTY"), entry.Difficulty) }; body.AddChild(pips); pips.Bind(entry.Difficulty);
        Caption(body, "Description", entry.Description.Length > 0 ? entry.Description : Tr("LEVEL_NO_DESCRIPTION"));
        Caption(body, "Record", record is null ? Tr("LEVEL_NO_RECORD") : record.Completed ? string.Format(Tr("LEVEL_RECORD"), Tr("COMPLETED"), record.BestTimeTicks / 60d, record.BestEmeralds, record.Attempts) : string.Format(Tr("LEVEL_RECORD_UNFINISHED"), record.BestEmeralds, record.Attempts));
        if (entry.Source == LevelSource.User) { Caption(body, "UserBadge", Tr("USER_LEVEL")); }
        if (entry.Diagnostics.Any(d => d.Severity != DiagnosticSeverity.Info))
        {
            Caption(body, "FirstDiagnostic", entry.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error)?.ToString() ?? entry.Diagnostics.First(d => d.Severity != DiagnosticSeverity.Info).ToString());
            var show = new Button { Name = "ShowDiagnostics", Text = Tr("LEVEL_SHOW_DIAGNOSTICS"), CustomMinimumSize = new(0, 48) }; body.AddChild(show); show.Pressed += showDiagnostics;
        }
    }
    public override void _Process(double delta)
    { if (_picture is not null) { float width = MathF.Min(320, MathF.Max(1, Size.X - 16)); Vector2 target = new(width, width * 9 / 16); if (_picture.CustomMinimumSize != target) { _picture.CustomMinimumSize = target; } } }
    private static void Caption(Node parent, string name, string text)
    { parent.AddChild(new Label { Name = name, Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = MouseFilterEnum.Ignore }); }
    public override void _ExitTree() => _preview.Dispose();
}
