using Dungeons.Core.Level;
using Dungeons.Core.Sim;
using Dungeons.Game.Persistence;
using Godot;

namespace Dungeons.Game;

public partial class GameplayScreen
{
    private CanvasLayer _death = null!;
    private Label _deathText = null!;
    private CanvasLayer? _results;
    private LevelDocument _level = null!;
    private RunStore? _store;
    private RunTicket? _ticket;
    private bool _finished;
    /// <summary>Starts another attempt with this run's seed.</summary>
    public event Action? RestartRequested;
    /// <summary>Whether the death overlay is visible.</summary>
    public bool DeathVisible => _death.Visible;
    /// <summary>Whether frozen result controls exist.</summary>
    public bool ResultsVisible => _results is not null;
    /// <summary>Rendered result text.</summary>
    public string ResultsText { get; private set; } = "";
    private void BuildDeathOverlay()
    {
        _death = new CanvasLayer { Name = "DeathScreen", Layer = 9, Visible = false }; AddChild(_death);
        var shade = new ColorRect { Color = new(0, 0, 0, .45f), MouseFilter = Control.MouseFilterEnum.Stop }; _death.AddChild(shade); shade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _deathText = new Label { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _death.AddChild(_deathText); _deathText.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); _deathText.AddThemeFontSizeOverride("font_size", 32);
    }
    private void RefreshFlow()
    {
        _death.Visible = !ReplayComplete && Session.State == SessionState.Running && Session.World.Players.Count > 0 && !Session.World.Players[0].Alive;
        _deathText.Text = Tr(Session.World.Lives > 0 ? "DEAD_RESPAWNING" : "YOU_DIED");
        if (Session.State is not (SessionState.Won or SessionState.Failed) || _finished) { return; }
        _finished = true; _death.Visible = false; _input.Clear(); _accumulator = 0; _pause?.QueueFree(); _pause = null; View.ProcessMode = ProcessModeEnum.Disabled;
        _sessionLog?.Log(Dungeons.Core.Util.LogLevel.Info, "Sim", Session.State == SessionState.Won ? "Mission complete." : "Mission failed.");
        if (OS.HasFeature("mobile")) { DisplayServer.ScreenSetKeepOn(false); }
        if (_store is not null && _ticket is not null) { _store.Finish(_ticket, Session, _level.SourceHash, Session.World.ContentHash); }
        MissionResults result = Session.World.Mission.Results ?? throw new InvalidOperationException("Terminal session has no frozen results.");
        long xp = _store?.Profile.TotalXp ?? result.Xp; int heroLevel = (int)System.Math.Floor(System.Math.Sqrt(xp / 50d));
        ResultsText = string.Format(Tr("RESULT_STATS").Replace("\\n", "\n", StringComparison.Ordinal), result.Ticks / 3600, result.Ticks / 60 % 60, result.Emeralds, result.Kills, result.Deaths, result.Xp, heroLevel, Session.World.Seed.ToString("x16"));
        _results = new CanvasLayer { Name = "ResultsScreen", Layer = 10 }; AddChild(_results);
        var shade = new ColorRect { Color = new(0.04f, .04f, .06f, .94f) }; _results.AddChild(shade); shade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var centre = new CenterContainer(); _results.AddChild(centre); centre.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var panel = new PanelContainer { CustomMinimumSize = new(500, 360) }; centre.AddChild(panel);
        var margin = new MarginContainer(); panel.AddChild(margin); foreach (string side in new[] { "left", "right", "top", "bottom" }) { margin.AddThemeConstantOverride("margin_" + side, 24); }
        var box = new VBoxContainer(); margin.AddChild(box); box.AddThemeConstantOverride("separation", 16);
        var title = new Label { Text = Tr(Session.State == SessionState.Won ? "MISSION_COMPLETE" : "MISSION_FAILED"), HorizontalAlignment = HorizontalAlignment.Center }; title.AddThemeFontSizeOverride("font_size", 32); box.AddChild(title);
        box.AddChild(new Label { Text = ResultsText, HorizontalAlignment = HorizontalAlignment.Center });
        var action = new Button { Name = "PrimaryAction", Text = Tr(Session.State == SessionState.Won ? "CONTINUE" : "RETRY"), CustomMinimumSize = new(0, 52) }; box.AddChild(action);
        action.Pressed += () => { if (Session.State == SessionState.Won) { QuitRequested?.Invoke(); } else { RestartRequested?.Invoke(); } }; action.GrabFocus();
        if (Session.State == SessionState.Failed)
        { var quit = new Button { Text = Tr("QUIT_TO_LEVELS"), CustomMinimumSize = new(0, 52) }; box.AddChild(quit); quit.Pressed += () => QuitRequested?.Invoke(); }
    }
    /// <summary>Archives an abandoned attempt once; the attempt was counted at launch.</summary>
    public void CloseAttempt()
    {
        if (_store is not null && _ticket is not null && !_ticket.Closed)
        { _sessionLog?.Log(Dungeons.Core.Util.LogLevel.Info, "Sim", "Mission attempt closed."); _store.Abandon(_ticket, Session, _level.SourceHash, Session.World.ContentHash); }
    }
    public override void _ExitTree() { SettingsRuntime.Instance?.Detach(this); StopVibration(); CloseAttempt(); if (OS.HasFeature("mobile")) { DisplayServer.ScreenSetKeepOn(false); } }
}
