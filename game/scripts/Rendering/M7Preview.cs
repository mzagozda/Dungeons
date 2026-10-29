using System.Text.Json.Nodes;
using Dungeons.Core.Level;
using Dungeons.Core.Sim;
using Dungeons.Game.Input;
using Godot;
using CoreVec2 = Dungeons.Core.Math.Vec2;

namespace Dungeons.Game.Rendering;

/// <summary>Native visual/input/replay probe for phone, mirrored and tablet touch layouts.</summary>
public partial class M7Preview : Node3D
{
    private GameplayScreen _game = null!;
    private LevelDocument _level = null!;
    private string _mode = "", _path = "";
    private int _frames, _captureFrames, _ignoredHardwareInputs;
    private bool _ready, _aim;
    private CoreVec2 _bowPoint;
    private Dungeons.Game.Audio.AudioManager? _audio;
    public override void _Ready()
    {
        try
        {
            foreach (string arg in ScreenshotRun.Arguments)
            {
                if (arg.StartsWith("--m7-mode=", StringComparison.Ordinal)) { _mode = arg[10..]; }
                if (arg.StartsWith("--m7-screenshot=", StringComparison.Ordinal)) { _path = arg[16..]; }
            }
            if (_mode is not ("phone" or "aim" or "left" or "tablet" or "device") || _path.Length == 0) { throw new ArgumentException("M7 mode and screenshot path required."); }
            _path = ProjectSettings.GlobalizePath(_path);
            var content = ContentBridge.LoadRegistry(); _level = ContentBridge.LoadBuiltins(content)[0];
            if (_mode == "device") { _audio = new(); AddChild(_audio); _audio.Initialize(new(content)); }
            _game = _mode == "device" ? new GameplayScreen() : new CaptureGameplayScreen(); AddChild(_game);
            _game.Build(_level, content, 42, touchPreview: true); _game.SetProcess(false); _aim = _mode != "phone";
        }
        catch (Exception error) { Fail(error); }
    }
    public override void _Process(double delta)
    {
        try
        {
            if (!_game.View.ReadyForPlay || _frames++ < 12) { return; }
            TouchControls touch = _game.Touch!;
            if (!_ready)
            {
                _game._Process(0); touch.ConfigureLayout(1, _mode == "left");
                if (_mode != "device") { touch.ConfigurePreview(_mode == "tablet" ? new(1024, 768) : new(880, 400), _mode == "tablet" ? new(0, 24, 0, 20) : new(28, 4, 12, 16)); }
                if (_aim)
                {
                    CoreVec2 origin = new(_mode == "left" ? 700 : 180, _mode == "tablet" ? 620 : 290);
                    if (_mode == "device") { origin = touch.State.Layout.Minimum + new CoreVec2((touch.State.Layout.Maximum.X - touch.State.Layout.Minimum.X) * .2f, (touch.State.Layout.Maximum.Z - touch.State.Layout.Minimum.Z) * .7f); }
                    Push(new InputEventScreenTouch { Index = 0, Position = touch.DpToViewport(origin), Pressed = true });
                    Push(new InputEventScreenDrag { Index = 0, Position = touch.DpToViewport(origin + new CoreVec2(40, 0)) });
                    foreach (TouchTarget target in touch.State.Layout.Targets) { if (target.Action == InputButtons.Ranged) { _bowPoint = target.Centre; break; } }
                    Push(new InputEventScreenTouch { Index = 1, Position = touch.DpToViewport(_bowPoint), Pressed = true });
                    Push(new InputEventScreenDrag { Index = 1, Position = touch.DpToViewport(_bowPoint + new CoreVec2(35, -25)) });
                }
                _ready = true;
                if (_aim && ScreenshotRun.Arguments.Contains("--m7-input-interference"))
                {
                    GD.Print($"M7_INTERFERENCE before joystick={touch.State.JoystickActive} bow={touch.State.Held(InputButtons.Ranged)}");
                    Push(new InputEventMouseMotion { Position = new(50, 50), Relative = new(1, 0) });
                    GD.Print($"M7_INTERFERENCE after joystick={touch.State.JoystickActive} bow={touch.State.Held(InputButtons.Ranged)}");
                }
            }
            _game._Process(GameConstants.Dt / 4);
            if (_captureFrames++ < 12) { return; }
            if (_game.Session.State != SessionState.Running || _aim && (!touch.State.JoystickActive || !touch.State.Held(InputButtons.Ranged)))
            { throw new InvalidOperationException($"Touch capture state differs: state={_game.Session.State} joystick={touch.State.JoystickActive} bow={touch.State.Held(InputButtons.Ranged)} focus={DisplayServer.WindowIsFocused()} mode={_mode}."); }
            using Image image = GetViewport().GetTexture().GetImage(); if (ScreenshotRun.Save(image, _path) != Error.Ok) { throw new IOException("Could not save M7 image."); }
            double expectedAspect = (double)touch.CanvasDpSize.X / touch.CanvasDpSize.Y;
            if (System.Math.Abs((double)image.GetWidth() / image.GetHeight() - expectedAspect) > .002) { throw new InvalidOperationException("Native viewport was letterboxed or did not match the requested touch aspect."); }
            int capturedTick = _game.Session.Tick;
            ulong captureHash = _game.Session.Hash();
            GraphicsRuntime graphics = GraphicsRuntime.Instance ?? throw new InvalidOperationException("Graphics profile runtime is missing.");
            if (graphics.Options.Preset == QualityPreset.Low && (_game.View.EnabledLightCount > 4 || _game.View.GetChildren().OfType<DirectionalLight3D>().Single().ShadowEnabled || _game.View.GetChildren().OfType<WorldEnvironment>().Single().Environment.GlowEnabled))
            { throw new InvalidOperationException("Low preset did not bind its light/shadow/glow settings."); }
            float blackFraction = 0;
            if (graphics.Options.Preset == QualityPreset.Low)
            {
                int black = 0, samples = 0;
                for (int y = image.GetHeight() * 15 / 100; y < image.GetHeight() * 55 / 100; y += 16)
                { for (int x = image.GetWidth() / 10; x < image.GetWidth() * 85 / 100; x += 16) { Color pixel = image.GetPixel(x, y); if (pixel.R < .02f && pixel.G < .02f && pixel.B < .02f) { black++; } samples++; } }
                blackFraction = black / (float)samples;
                if (blackFraction > .1f) { throw new InvalidOperationException("Low capture contains black static-model surfaces."); }
                foreach (MultiMeshInstance3D batch in _game.View.GetChildren().OfType<MultiMeshInstance3D>())
                { if (!batch.Multimesh.UseColors || batch.Multimesh.InstanceCount > 0 && batch.Multimesh.GetInstanceColor(0).R < .99f) { throw new InvalidOperationException("Static batch lacks an explicit neutral instance colour."); } }
            }
            if (_aim)
            {
                Push(new InputEventScreenTouch { Index = 1, Position = touch.DpToViewport(_bowPoint + new CoreVec2(35, -25)), Pressed = false });
                for (int i = 0; i < 8; i++) { _game._Process(GameConstants.Dt); }
            }
            ReplaySnapshot replay = _game.Session.Recorder!.Capture(); ReplayVerification verified = ReplayVerifier.Verify(new GameSession(_level, 42), replay);
            if (!verified.Success || _aim && _game.Session.World.Players[0].Inventory.Arrows != 29) { throw new InvalidOperationException("Touch gesture replay or bow release failed: " + verified.Message); }
            bool debugGestureVerified = false;
            if (OS.IsDebugBuild())
            {
                touch.ClearInputs(); ulong beforeGesture = _game.Session.Hash();
                for (int i = 0; i < 4; i++) { Push(new InputEventScreenTouch { Index = 40 + i, Position = touch.DpToViewport(touch.State.Layout.Minimum + new CoreVec2(80 + i * 30, 100)), Pressed = true }); }
                for (int i = 0; i < 4; i++) { Push(new InputEventScreenTouch { Index = 40 + i, Position = touch.DpToViewport(touch.State.Layout.Minimum + new CoreVec2(80 + i * 30, 100)), Pressed = false }); }
                debugGestureVerified = _game.Hud.DebugVisible && _game.Session.Hash() == beforeGesture && touch.State.Sample(0).Pressed == InputButtons.None;
                if (!debugGestureVerified) { throw new InvalidOperationException("Four-finger debug gesture changed Core state, leaked an action or failed to toggle."); }
                _game.Hud.ToggleDebugOverlay();
            }
            ScreenshotRun.WriteText(System.IO.Path.ChangeExtension(_path, ".dreplay"), ReplayJson.Write(new(replay, _level.SourceHash, _game.Session.World.ContentHash)));
            ScreenshotRun.WriteText(System.IO.Path.ChangeExtension(_path, ".json"), new JsonObject { ["mode"] = _mode, ["capturedTick"] = capturedTick, ["replayTicks"] = replay.TickCount, ["replayVerified"] = verified.Success,
                ["debugGestureVerified"] = debugGestureVerified, ["dpWidth"] = touch.CanvasDpSize.X, ["dpHeight"] = touch.CanvasDpSize.Y, ["safeLeft"] = touch.State.Layout.Minimum.X, ["safeTop"] = touch.State.Layout.Minimum.Z, ["safeRight"] = touch.State.Layout.Maximum.X, ["safeBottom"] = touch.State.Layout.Maximum.Z, ["pixelWidth"] = image.GetWidth(), ["pixelHeight"] = image.GetHeight(), ["leftHanded"] = touch.State.Layout.LeftHanded,
                ["androidSdk"] = OS.HasFeature("android") ? PlatformPaths.AndroidSdk : 0, ["nativeFilePicker"] = DisplayServer.HasFeature(DisplayServer.Feature.NativeDialogFile),
                ["audioVoicesPrepared"] = _audio?.PreparedVoiceCount ?? 0, ["audioDriver"] = _audio?.Driver ?? "", ["music"] = _audio?.MusicId ?? "",
                ["quality"] = graphics.Options.Preset.ToString(), ["rule"] = graphics.Selection.Rule, ["renderer"] = graphics.Renderer, ["scale"] = _game.GetViewport().Scaling3DScale, ["lights"] = _game.View.EnabledLightCount, ["shadowAtlas"] = graphics.Options.ShadowAtlas, ["blackFraction"] = blackFraction,
                ["ignoredHardwareInputs"] = _ignoredHardwareInputs, ["finalHash"] = _game.Session.Hash().ToString("x16") }.ToJsonString());
            ScreenshotRun.Complete(true, image, _game.Session, capturedTick, captureHash);
            GD.Print($"M7_PREVIEW=PASS mode={_mode} captureTick={capturedTick} replayTicks={replay.TickCount} manualBow={_aim} replayVerified={verified.Success}"); SetProcess(false); GetTree().Quit();
        }
        catch (Exception error) { Fail(error); }
    }
    private void Push(InputEvent input) => GetViewport().PushInput(input, true);
    public override void _Input(InputEvent input)
    {
        // This harness drives one deterministic touch stream. Desktop pointer/keyboard/gamepad
        // activity must not switch the production input adapter away from that stream mid-capture.
        if (input is InputEventMouse or InputEventKey or InputEventJoypadButton or InputEventJoypadMotion)
        { _ignoredHardwareInputs++; GetViewport().SetInputAsHandled(); }
    }
    private void Fail(Exception error) { GD.PushError(error.ToString()); SetProcess(false); GetTree().Quit(1); }
}
