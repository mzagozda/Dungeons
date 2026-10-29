using Dungeons.Core.Level;
using Dungeons.Core.Sim;
using Godot;

namespace Dungeons.Game.Rendering;

/// <summary>Actual enemy combat through a cloned authored level, used only by validation.</summary>
public partial class M5Preview : Node3D
{
    private GameplayScreen _game = null!;
    private string _path = "";
    private int _warmup;
    private bool _capture;
    public override void _Ready()
    {
        try
        {
            foreach (string arg in ScreenshotRun.Arguments) { if (arg.StartsWith("--m5-screenshot=", StringComparison.Ordinal)) { _path = arg[16..]; } }
            if (_path.Length == 0) { throw new ArgumentException("M5 preview requires --m5-screenshot=path."); }
            var content = ContentBridge.LoadRegistry(); var source = ContentBridge.LoadBuiltins(content)[0].ToJsonObject();
            var enemy = source["entities"]!.AsArray().Single(e => e!["id"]!.GetValue<string>() == "z_bank_1")!;
            enemy["x"] = 5.8; enemy["z"] = 36.5;
            LevelLoadResult loaded = LevelLoader.Load(source.ToJsonString(), content);
            if (!loaded.Success) { throw new InvalidOperationException("M5 preview fixture failed validation."); }
            _game = new CaptureGameplayScreen(); AddChild(_game); _game.Build(loaded.Document!, content, 1); _game.SetProcess(false);
        }
        catch (Exception error) { Fail(error); }
    }
    public override void _Process(double delta)
    {
        try
        {
            if (!_game.View.ReadyForPlay) { return; }
            if (_warmup++ == 0) { _game._Process(0); }
            if (_warmup < 12) { _game.View.Interpolate(1); return; }
            if (_capture)
            {
                using Image image = GetViewport().GetTexture().GetImage(); Error saved = ScreenshotRun.Save(image, _path);
                CombatOverlay overlay = _game.View.CombatOverlay;
                bool uploaded = ValidateParticles();
                bool passed = saved == Error.Ok && overlay.VisibleBarCount == 1 && overlay.ActiveNumberCount == 1 && uploaded;
                ScreenshotRun.Complete(passed, image, _game.Session);
                GD.Print($"M5_PREVIEW={(passed ? "PASS" : "FAIL")} bars={overlay.VisibleBarCount} numbers={overlay.ActiveNumberCount} alerts={overlay.VisibleAlertCount} tick={_game.Session.Tick} particles={_game.View.Particles.LiveCount} billboards={uploaded}");
                SetProcess(false); GetTree().Quit(passed ? 0 : 1); return;
            }
            // Step one authoritative tick per frame, preserving visible event reactions.
            _game.Session.Step(_game.Session.Tick == 0 ? new PlayerInput(default, new(1, 0), true, InputButtons.Attack) : default);
            _game.View.ConsumeEvents(_game.Session.DrainEvents()); _game.View.Interpolate(1); _game.Hud._Process(0);
            _capture = _game.Session.Tick == 8;
        }
        catch (Exception error) { Fail(error); }
    }
    private bool ValidateParticles()
    {
        bool any = false;
        foreach (Node child in _game.View.Particles.GetChildren())
        {
            var mesh = ((MultiMeshInstance3D)child).Multimesh;
            for (int i = 0; i < mesh.VisibleInstanceCount; i++)
            {
                any = true; Transform3D transform = mesh.GetInstanceTransform(i); float size = transform.Basis.Scale.X;
                if (size < .099f || size > .251f || MathF.Abs(transform.Basis.Z.Normalized().Dot(_game.View.Camera.GlobalBasis.Z)) < .999f) { return false; }
            }
        }
        return any;
    }
    private void Fail(Exception error) { GD.PushError(error.ToString()); SetProcess(false); GetTree().Quit(1); }
}
