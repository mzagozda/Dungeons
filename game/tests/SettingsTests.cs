using Dungeons.Core.Sim;
using Dungeons.Game.Input;
using Dungeons.Game.Persistence;
using Dungeons.Game.Rendering;
using Dungeons.Game.UI;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace Dungeons.Game.Tests;

[TestSuite, RequireGodotRuntime]
public sealed class SettingsTests
{
    [TestCase, GodotExceptionMonitor]
    public async Task ActualMenuPauseSettingsBackFlowLeavesGameplayPaused()
    {
        string folder = System.IO.Path.GetFullPath(System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "artifacts/m8/app-flow-tests", Guid.NewGuid().ToString("N")));
        var app = new AppRoot(folder); using ISceneRunner runner = ISceneRunner.Load(app, autoFree: true);
        await AppTestReady.Wait(runner, app);
        Button Find(string key) => app.FindChildren("*", "Button", true, false).OfType<Button>().First(button => button.IsVisibleInTree() && button.Text == app.Tr(key));
        Find("PLAY").EmitSignal(BaseButton.SignalName.Pressed); await runner.AwaitIdleFrame();
        app.FindChildren("*", "PanelContainer", true, false).OfType<LevelCard>().Single(card => card.Entry.Id == "core.mossy_hollow").SelectButton.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.AwaitIdleFrame(); Find("PLAY").EmitSignal(BaseButton.SignalName.Pressed);
        GameplayScreen game = app.GetChildren().OfType<GameplayScreen>().Single();
        for (int i = 0; i < 120 && !game.View.ReadyForPlay; i++) { await runner.AwaitIdleFrame(); } AssertThat(game.View.ReadyForPlay).IsTrue();
        game._Notification((int)Node.NotificationWMGoBackRequest); AssertThat(game.Session.State).IsEqual(SessionState.Paused); ulong hash = game.Session.Hash();
        Find("SETTINGS").EmitSignal(BaseButton.SignalName.Pressed); AssertThat(game.ExternalUiOpen).IsTrue();
        app.PropagateNotification((int)Node.NotificationWMGoBackRequest); await runner.AwaitIdleFrame();
        AssertThat(game.Session.State).IsEqual(SessionState.Paused); AssertThat(game.ExternalUiOpen).IsFalse(); AssertThat(game.Session.Hash()).IsEqual(hash);
        AssertThat(app.GetChildren().OfType<SettingsPanel>().Any()).IsFalse();
        app.PropagateNotification((int)Node.NotificationApplicationPaused);
        string logPath = System.IO.Path.Combine(folder, "logs", "dungeons.log"); AssertThat(System.IO.File.Exists(logPath)).IsTrue();
        using var missionLog = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadLines(logPath).Single(line => line.Contains("Mission started.", StringComparison.Ordinal)));
        AssertThat(missionLog.RootElement.GetProperty("levelId").GetString()).IsEqual(game.Session.World.LevelId);
        AssertThat(missionLog.RootElement.GetProperty("seed").GetUInt64()).IsEqual(game.Session.World.Seed);
        AssertThat(missionLog.RootElement.GetProperty("tick").GetInt32()).IsEqual(0); AssertThat(game.Session.Hash()).IsEqual(hash);
    }
    [TestCase, GodotExceptionMonitor]
    public async Task RealGuiDragMovesTheSharedTouchLayoutPreview()
    {
        var root = new Node(); using ISceneRunner runner = ISceneRunner.Load(root, autoFree: true);
        var editor = new TouchLayoutEditor { Size = new(880, 400) }; root.AddChild(editor); editor.Configure(new()); int edits = 0; editor.Edited += _ => edits++;
        await runner.AwaitIdleFrame();
        var before = editor.Layout.Targets[3].Centre; Viewport viewport = editor.GetViewport();
        viewport.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = new(before.X, before.Z) }, true);
        viewport.PushInput(new InputEventMouseMotion { ButtonMask = MouseButtonMask.Left, Position = new(440, 87.2f), Relative = new(440 - before.X, 87.2f - before.Z) }, true);
        viewport.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = new(440, 87.2f) }, true);
        await runner.AwaitIdleFrame(); AssertThat(edits > 0).IsTrue(); AssertThat(editor.Layout.Targets[3].Centre != before).IsTrue();
    }
    [TestCase, GodotExceptionMonitor]
    public async Task DraftApplyBindsGraphicsAudioControlsAndAccessibilityWithoutChangingCore()
    {
        GraphicsRuntime graphics = GraphicsRuntime.Instance!; DeviceSelection original = graphics.Selection; bool mobile = graphics.Mobile;
        var oldBindings = SettingsBindings.Snapshot(); string folder = ProjectSettings.GlobalizePath("user://settings-test-" + Guid.NewGuid().ToString("N"));
        var root = new Node(); using ISceneRunner runner = ISceneRunner.Load(root, autoFree: true);
        try
        {
            var runtime = new SettingsRuntime(); root.AddChild(runtime); runtime.Initialize(folder, GameSettings.Defaults(QualityPreset.High, false), applyWindow: false);
            var content = ContentBridge.LoadRegistry(); var game = new GameplayScreen(); root.AddChild(game); game.Build(ContentBridge.LoadBuiltins(content)[0], content, 42, touchPreview: true); game.SetProcess(false);
            for (int i = 0; i < 120 && !game.View.ReadyForPlay; i++) { await runner.AwaitIdleFrame(); } AssertThat(game.View.ReadyForPlay).IsTrue();
            game.Touch!.ConfigurePreview(new(880, 400), new(24, 0, 24, 16)); ulong hash = game.Session.Hash();
            var panel = new SettingsPanel(); root.AddChild(panel); panel.Build(runtime); await runner.AwaitIdleFrame();
            ((HSlider)panel.FindChild("RESOLUTION_SCALE", true, false)).Value = .8;
            ((CheckButton)panel.FindChild("VIBRATION", true, false)).ButtonPressed = false;
            ((CheckButton)panel.FindChild("LEFT_HANDED", true, false)).ButtonPressed = true;
            ((CheckButton)panel.FindChild("DAMAGE_NUMBERS", true, false)).ButtonPressed = false;
            ((CheckButton)panel.FindChild("HIGH_CONTRAST_HEALTH", true, false)).ButtonPressed = true;
            ((CheckButton)panel.FindChild("REDUCE_MOTION", true, false)).ButtonPressed = true;
            ((HSlider)panel.FindChild("MUSIC_VOLUME", true, false)).Value = 0;
            AssertThat(panel.Draft.Video.Preset).IsEqual(QualityPreset.Custom); AssertThat(runtime.Current.Video.Preset).IsEqual(QualityPreset.High);
            ((Button)panel.FindChild("Binding_attack", true, false)).EmitSignal(BaseButton.SignalName.Pressed);
            panel.GetViewport().PushInput(new InputEventKey { PhysicalKeycode = Key.L, Pressed = true }, true); await runner.AwaitIdleFrame();
            AssertThat(InputMap.ActionGetEvents("attack").OfType<InputEventKey>().Any(input => input.PhysicalKeycode == Key.J)).IsTrue();
            AssertThat(panel.Draft.Controls.Bindings["attack"].Any(binding => binding.Kind == BindingKind.Key && binding.Code == (int)Key.L)).IsTrue();
            AssertThat(panel.Draft.Controls.Bindings["attack"].Any(binding => binding.Kind == BindingKind.JoyButton)).IsTrue();
            panel.Apply(); await runner.AwaitIdleFrame();
            AssertThat(graphics.Selection.Preset).IsEqual(QualityPreset.Custom);
            AssertThat(runtime.Current.Video.ResolutionScale).IsEqual(.8f);
            AssertThat(graphics.Options.Preset).IsEqual(graphics.CompatibilityRenderer ? QualityPreset.Low : QualityPreset.Custom);
            AssertThat(game.View.GetViewport().Scaling3DScale).IsEqual(graphics.CompatibilityRenderer ? .75f : .8f);
            AssertThat(AudioServer.IsBusMute(AudioServer.GetBusIndex("Music"))).IsTrue(); AssertThat(game.VibrationEnabled).IsFalse(); AssertThat(game.Touch.State.Layout.LeftHanded).IsTrue();
            AssertThat(game.View.ReduceMotion).IsTrue(); AssertThat(game.View.CombatOverlay.DamageNumbersEnabled).IsFalse();
            AssertThat(game.Hud.GetNode<Control>("HudRoot").GetChildren().OfType<ProgressBar>().Single().GetChildren().OfType<HealthBarPattern>().Single().Visible).IsTrue();
            AssertThat(InputMap.ActionGetEvents("attack").OfType<InputEventKey>().Single().PhysicalKeycode).IsEqual(Key.L);
            AssertThat(game.Session.Hash()).IsEqual(hash); AssertThat(new SettingsStore(folder, runtime.Defaults).Current.Controls.LeftHanded).IsTrue();
            ((HSlider)panel.FindChild("RESOLUTION_SCALE", true, false)).Value = .6; panel.Close(); AssertThat(runtime.Current.Video.ResolutionScale).IsEqual(.8f);
        }
        finally
        {
            graphics.Configure(original, mobile); SettingsBindings.Apply(new() { Bindings = oldBindings });
            AudioServer.SetBusMute(AudioServer.GetBusIndex("Music"), false); AudioServer.SetBusVolumeDb(AudioServer.GetBusIndex("Music"), 0);
            System.IO.File.Delete(System.IO.Path.Combine(folder, "settings.json")); System.IO.Directory.Delete(folder);
        }
    }
    [TestCase, GodotExceptionMonitor]
    public async Task LayoutEditorRejectsOverlapAndSmallViewFitsWithoutLosingSavedPositions()
    {
        var root = new Node(); using ISceneRunner runner = ISceneRunner.Load(root, autoFree: true);
        var editor = new TouchLayoutEditor(); root.AddChild(editor); editor.Configure(new());
        AssertThat(editor.TryMove(InputButtons.Potion, new(.5f, .2f))).IsTrue();
        AssertThat(editor.TryMove(InputButtons.Attack, new(.5f, .2f))).IsFalse();
        var content = ContentBridge.LoadRegistry(); var game = new GameplayScreen(); root.AddChild(game); game.Build(ContentBridge.LoadBuiltins(content)[0], content, 42, touchPreview: true); game.SetProcess(false);
        await runner.AwaitIdleFrame();
        game.Touch!.ConfigurePreview(new(700, 300), Vector4.Zero); game.Touch.ConfigureLayout(new ControlSettings { TouchScale = 1.5f });
        AssertThat(game.Touch.LayoutAvailable).IsTrue(); AssertThat(game.Touch.State.Layout.Scale < 1.5f).IsTrue();
        game.Touch.ConfigurePreview(new(250, 150), Vector4.Zero); AssertThat(game.Touch.LayoutAvailable).IsFalse();
        game.Touch.ConfigurePreview(new(880, 400), Vector4.Zero); AssertThat(game.Touch.LayoutAvailable).IsTrue();
        AssertThat(SettingsBindings.Capture(new InputEventJoypadMotion { Axis = JoyAxis.LeftX, AxisValue = .1f }).HasValue).IsFalse();
        AssertThat(SettingsBindings.Capture(new InputEventMouseButton { Device = (int)InputEvent.DeviceIdEmulation, Pressed = true, ButtonIndex = MouseButton.Left }).HasValue).IsFalse();
    }
}
