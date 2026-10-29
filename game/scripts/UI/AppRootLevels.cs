using Dungeons.Core.Level;
using Dungeons.Game.Persistence;
using Dungeons.Game.UI;
using Godot;

namespace Dungeons.Game;

public partial class AppRoot
{
    private string? _selectedLevel;
    private AcceptDialog? _diagnostics;
    private void Levels()
    {
        if (_runs is null && _screenshotTest is null) { _runs = new(DataDirectory, message => Logging.GameLog.Warning("Io", message)); }
        _back = MainMenu; _catalog = ScanLevels(); VBoxContainer box = Menu(Tr("LEVEL_SELECT"));
        var browser = new LevelBrowserView(); box.AddChild(browser);
        browser.Build(_catalog, _runs?.Profile.Levels ?? new Dictionary<string, LevelRecord>(), _selectedLevel);
        browser.Selected += entry => { _selectedLevel = entry.Id; Details(entry); }; browser.DiagnosticRequested += entry => ShowDiagnostics(entry, all: false);
        if (_catalog.Count == 0) { box.AddChild(new Label { Text = Tr("LEVEL_NONE"), AutowrapMode = TextServer.AutowrapMode.WordSmart }); }
        var actions = new HFlowContainer(); box.AddChild(actions);
        MenuAction(actions, "REFRESH", Levels); MenuAction(actions, "IMPORT_LEVEL", ImportLevel);
        if (!OS.HasFeature("mobile")) { MenuAction(actions, "OPEN_LEVELS_FOLDER", () => OS.ShellOpen(LevelsDirectory)); }
        MenuAction(actions, "BACK", MainMenu);
    }
    private void Details(CatalogEntry entry)
    {
        _back = Levels; var box = Menu(entry.Name); LevelRecord? record = null; _runs?.Profile.Levels.TryGetValue(entry.Id, out record);
        var detail = new LevelDetailsView(); box.AddChild(detail); detail.Build(entry, record, () => ShowDiagnostics(entry, all: true));
        var actions = new HFlowContainer(); box.AddChild(actions);
        Button play = MenuAction(actions, "PLAY", () => Start(entry)); play.Disabled = !entry.IsPlayable;
        Button back = MenuAction(actions, "BACK", Levels); if (entry.IsPlayable) { play.GrabFocus(); } else { back.GrabFocus(); }
    }
    private Button MenuAction(Container parent, string key, Action pressed)
    {
        var button = new Button { Text = Tr(key), CustomMinimumSize = new(96, 48), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        parent.AddChild(button); button.Pressed += pressed; return button;
    }
    private void ShowDiagnostics(CatalogEntry entry, bool all)
    {
        _diagnostics?.QueueFree(); var dialog = new AcceptDialog { Name = "LevelDiagnostics", Title = entry.Name, OkButtonText = Tr("BACK") }; _diagnostics = dialog; AddChild(dialog);
        dialog.AddThemeStyleboxOverride("embedded_border", GD.Load<Theme>("res://assets/ui/block_dungeons.tres").GetStylebox("panel", "Panel"));
        dialog.AddThemeStyleboxOverride("panel", GD.Load<Theme>("res://assets/ui/block_dungeons.tres").GetStylebox("panel", "Panel"));
        var scroll = new ScrollContainer { FollowFocus = true, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; dialog.AddChild(scroll);
        var body = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; scroll.AddChild(body); body.AddThemeConstantOverride("separation", 12);
        IEnumerable<Diagnostic> diagnostics = all ? entry.Diagnostics : entry.Diagnostics.Where(d => d.Severity != DiagnosticSeverity.Info).OrderByDescending(d => d.Severity == DiagnosticSeverity.Error).Take(1);
        foreach (Diagnostic diagnostic in diagnostics)
        { body.AddChild(new Label { Text = diagnostic.ToString(), AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }); }
        if (!all) { MenuAction(body, "LEVEL_SHOW_DIAGNOSTICS", () => ShowDiagnostics(entry, all: true)); }
        dialog.PopupCenteredClamped(new(680, 420), .9f);
    }
}
