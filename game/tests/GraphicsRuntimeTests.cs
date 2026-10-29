using Dungeons.Core.Sim;
using Dungeons.Game.Rendering;
using Dungeons.Game.Persistence;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace Dungeons.Game.Tests;

[TestSuite, RequireGodotRuntime]
public sealed class GraphicsRuntimeTests
{
    [TestCase, GodotExceptionMonitor]
    public async Task PresetsAndAdaptiveScaleBindToWorldWithoutChangingCoreOrCanvas()
    {
        GraphicsRuntime runtime = GraphicsRuntime.Instance ?? throw new InvalidOperationException("Graphics autoload did not start.");
        DeviceSelection original = runtime.Selection; bool mobile = runtime.Mobile; bool compatibility = runtime.CompatibilityRenderer; runtime.SetProcess(false);
        try
        {
            runtime.Configure(new(QualityPreset.Low, RendererOverride.None, "test-low"), false);
            var content = ContentBridge.LoadRegistry(); var game = new GameplayScreen(); using ISceneRunner runner = ISceneRunner.Load(game, autoFree: true);
            game.Build(ContentBridge.LoadBuiltins(content)[0], content, 42); game.SetProcess(false); await Ready(runner, game.View);
            var sun = game.View.GetChildren().OfType<DirectionalLight3D>().Single(); var environment = game.View.GetChildren().OfType<WorldEnvironment>().Single().Environment;
            AssertThat(sun.ShadowEnabled).IsFalse(); AssertThat(environment.GlowEnabled).IsFalse(); AssertThat(game.View.EnabledLightCount).IsEqual(4);
            AssertThat(game.View.GetViewport().Scaling3DScale).IsEqual(.75f); AssertThat(game.View.GetViewport().Msaa3D).IsEqual(Viewport.Msaa.Disabled);
            runtime.Configure(new(QualityPreset.Medium, RendererOverride.None, "test-medium"), true);
            AssertThat(runtime.Selection.Preset).IsEqual(QualityPreset.Medium);
            AssertThat(runtime.Options.Preset).IsEqual(compatibility ? QualityPreset.Low : QualityPreset.Medium);
            AssertThat(sun.ShadowEnabled).IsEqual(!compatibility); AssertThat(environment.GlowEnabled).IsEqual(!compatibility); AssertThat(game.View.EnabledLightCount).IsEqual(compatibility ? 4 : 7);
            ulong hash = game.Session.Hash(); Vector2 canvasSize = game.Hud.GetNode<Control>("HudRoot").Size; Rect2 viewport = game.GetViewport().GetVisibleRect();
            for (int frame = 0; frame < 60; frame++) { runtime._Process(.019); }
            float reducedScale = compatibility ? .7f : .95f;
            AssertThat(runtime.Adaptive.Scale).IsEqual(reducedScale); AssertThat(game.View.GetViewport().Scaling3DScale).IsEqual(reducedScale);
            runtime.ConfigureSettings(new VideoSettings().WithPreset(QualityPreset.Medium, true));
            AssertThat(runtime.Adaptive.Scale).IsEqual(reducedScale); AssertThat(game.View.GetViewport().Scaling3DScale).IsEqual(reducedScale);
            AssertThat(game.Hud.GetNode<Control>("HudRoot").Size).IsEqual(canvasSize); AssertThat(game.GetViewport().GetVisibleRect()).IsEqual(viewport); AssertThat(game.Session.Hash()).IsEqual(hash);
            game.Session.Pause(); double elapsed = runtime.Adaptive.ActiveSeconds; runtime._Process(100); AssertThat(runtime.Adaptive.ActiveSeconds).IsEqual(elapsed);
        }
        finally { runtime.Configure(original, mobile); runtime.SetProcess(true); }
    }

    [TestCase, GodotExceptionMonitor]
    public async Task LevelReplacementKeepsSharedThermalHistoryAndReleasesOldView()
    {
        GraphicsRuntime runtime = GraphicsRuntime.Instance ?? throw new InvalidOperationException("Graphics autoload did not start.");
        DeviceSelection original = runtime.Selection; bool mobile = runtime.Mobile; runtime.SetProcess(false);
        try
        {
            runtime.Configure(new(QualityPreset.Medium, RendererOverride.None, "test-mobile"), true);
            var content = ContentBridge.LoadRegistry(); var level = ContentBridge.LoadBuiltins(content)[0]; double elapsed;
            using (ISceneRunner first = ISceneRunner.Load(new WorldView(), autoFree: true))
            {
                WorldView view = (WorldView)first.Scene(); view.Build(level, content, new(level)); await Ready(first, view);
                for (int i = 0; i < 60; i++) { runtime._Process(.016); } elapsed = runtime.Adaptive.ActiveSeconds;
            }
            using ISceneRunner second = ISceneRunner.Load(new WorldView(), autoFree: true);
            WorldView next = (WorldView)second.Scene(); next.Build(level, content, new(level)); await Ready(second, next);
            AssertThat(runtime.Adaptive.ActiveSeconds).IsEqual(elapsed); runtime._Process(.016); AssertThat(runtime.Adaptive.ActiveSeconds > elapsed).IsTrue();
        }
        finally { runtime.Configure(original, mobile); runtime.SetProcess(true); }
    }
    private static async Task Ready(ISceneRunner runner, WorldView view)
    { for (int frame = 0; frame < 120 && !view.ReadyForPlay; frame++) { await runner.AwaitIdleFrame(); } AssertThat(view.ReadyForPlay).IsTrue(); await runner.SimulateFrames(2); }
}
