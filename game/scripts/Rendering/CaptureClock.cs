using Dungeons.Core.Sim;
using Godot;

namespace Dungeons.Game.Rendering;

/// <summary>Presentation clock for named visual fixtures. Terrain loading never advances it.</summary>
public partial class CaptureClock : Node
{
    public GameplayScreen Game { private get; set; } = null!;
    public override void _Ready() { ScreenshotRun.Bind(Game); ProcessPriority = -1000; }
    public override void _ExitTree() { ScreenshotRun.Unbind(Game); Game = null!; }
    public override void _Process(double delta)
    {
        WorldView? view = Game.View; if (view is null) { return; }
        view.SetProcess(false); Game.Hud.SetProcess(false); view.CombatOverlay.SetProcess(false);
        if (!view.ReadyForPlay) { view._Process(0); return; }
        view._Process(GameConstants.Dt); view.CombatOverlay._Process(GameConstants.Dt); Game.Hud._Process(GameConstants.Dt);
    }
}
