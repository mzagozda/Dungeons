using Dungeons.Game.UI;
using Godot;

namespace Dungeons.Game;

public partial class AppRoot
{
    private SettingsPanel? _settingsPanel;
    private void OpenSettings()
    {
        if (_settings is null || _settingsPanel is not null) { return; }
        _game?.PauseForExternalUi(); if (_game is not null) { _game.ExternalUiOpen = true; } if (_menu is not null) { _menu.Visible = false; }
        var panel = new SettingsPanel { Name = "Settings" }; _settingsPanel = panel; AddChild(panel); panel.Build(_settings);
        panel.Closed += () =>
        {
            panel.QueueFree(); _settingsPanel = null; if (_menu is not null) { _menu.Visible = true; }
            GameplayScreen? game = _game;
            // Back notifications are broadcast to the whole tree; don't also resume this frame.
            if (game is not null) { Callable.From(() => { if (GodotObject.IsInstanceValid(game)) { game.ExternalUiOpen = false; } }).CallDeferred(); }
        };
    }
}
