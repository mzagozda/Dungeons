using Godot;

namespace Dungeons.Game.Rendering;

/// <summary>Screenshot fixture; desktop input/focus changes must not alter the prescribed capture scenario.</summary>
public partial class CaptureGameplayScreen : GameplayScreen
{
    public override void _Ready() { if (ScreenshotRun.Active) { AddChild(new CaptureClock { Game = this }); } }
    public override void _UnhandledInput(InputEvent @event) { }
    public override void _Notification(int what)
    {
        if (what == NotificationApplicationFocusOut || what == NotificationApplicationPaused) { return; }
        base._Notification(what);
    }
}
