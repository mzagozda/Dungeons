using Dungeons.Core.Math;
using Dungeons.Core.Sim;
using Godot;

namespace Dungeons.Game.Rendering;

/// <summary>Reproducible action captures through the production scene and fixed-step session.</summary>
public partial class M4Preview : Node3D
{
    private GameplayScreen _game = null!;
    private string _mode = "", _path = "";
    private double _accumulator;
    private int _warmup;
    private bool _capturePending;
    public override void _Ready()
    {
        try
        {
            foreach (string argument in ScreenshotRun.Arguments)
            { if (argument.StartsWith("--m4-action=", StringComparison.Ordinal)) { _mode = argument[12..]; } if (argument.StartsWith("--m4-screenshot=", StringComparison.Ordinal)) { _path = argument[16..]; } }
            if (_mode is not ("roll" or "bow" or "melee" or "pickup") || _path.Length == 0) { throw new ArgumentException("M4 capture requires action and screenshot path."); }
            var content = ContentBridge.LoadRegistry(); _game = new CaptureGameplayScreen { Name = "Gameplay" }; AddChild(_game);
            _game.Build(ContentBridge.LoadBuiltins(content)[0], content, 1); _game.SetProcess(false);
        }
        catch (Exception error) { GD.PushError(error.ToString()); SetProcess(false); GetTree().Quit(1); }
    }
    public override void _Process(double delta)
    {
        try { Advance(ScreenshotRun.Active ? GameConstants.Dt : delta); }
        catch (Exception error) { GD.PushError(error.ToString()); SetProcess(false); GetTree().Quit(1); }
    }
    private void Advance(double delta)
    {
        if (!_game.View.ReadyForPlay) { return; }
        if (_warmup == 0) { _game._Process(0); } // Complete normal loading UI without advancing a tick.
        if (++_warmup < 12) { _game.View.Interpolate(1); return; }
        if (_capturePending)
        {
            using Image image = GetViewport().GetTexture().GetImage(); Error saved = ScreenshotRun.Save(image, _path);
            ScreenshotRun.Complete(saved == Error.Ok, image, _game.Session);
            Entity hero = _game.Session.World.Players[0];
            GD.Print($"M4_PREVIEW={(saved == Error.Ok ? "PASS" : "FAIL")} action={_mode} tick={_game.Session.Tick} phase={hero.Action.Phase} roll={hero.Action.RollRemaining} arrows={hero.Inventory.Arrows} emeralds={hero.Inventory.Emeralds}");
            GetTree().Quit(saved == Error.Ok ? 0 : 1); SetProcess(false); return;
        }
        _accumulator += System.Math.Min(delta, GameConstants.MaxFrameDelta);
        int target = _mode == "pickup" ? 60 : 8;
        int steps = 0;
        while (_accumulator >= GameConstants.Dt && steps++ < GameConstants.MaxTicksPerFrame && _game.Session.Tick < target)
        {
            InputButtons button = _game.Session.Tick > 0 ? InputButtons.None : _mode switch { "roll" => InputButtons.Roll, "bow" => InputButtons.Ranged, "melee" => InputButtons.Attack, _ => InputButtons.None };
            Vec2 move = _mode is "roll" or "pickup" ? new(1, 0) : Vec2.Zero;
            _game.Session.Step(new PlayerInput(move, new(1, 0), true, button)); _accumulator -= GameConstants.Dt;
        }
        _game.View.ConsumeEvents(_game.Session.DrainEvents()); _game.View.Interpolate(1); _game.Hud._Process(0);
        if (_game.Session.Tick == target) { _capturePending = true; }
    }
}
