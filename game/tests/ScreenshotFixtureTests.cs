using Dungeons.Game.Rendering;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace Dungeons.Game.Tests;

[TestSuite, RequireGodotRuntime]
public sealed class ScreenshotFixtureTests
{
    [TestCase, GodotExceptionMonitor]
    public void CaptureClockReleasesOnlyTheBindingItOwnsOnTreeExit()
    {
        var root = new Node(); using ISceneRunner runner = ISceneRunner.Load(root, autoFree: true);
        var firstGame = new CaptureGameplayScreen { ProcessMode = Node.ProcessModeEnum.Disabled }; root.AddChild(firstGame);
        var secondGame = new CaptureGameplayScreen { ProcessMode = Node.ProcessModeEnum.Disabled }; root.AddChild(secondGame);
        var first = new CaptureClock { Game = firstGame }; root.AddChild(first);
        var second = new CaptureClock { Game = secondGame }; root.AddChild(second);
        AssertThat(ScreenshotRun.HasBoundGame).IsTrue();
        first.Free(); AssertThat(ScreenshotRun.HasBoundGame).IsTrue();
        second.Free(); AssertThat(ScreenshotRun.HasBoundGame).IsFalse();
    }

    [TestCase, GodotExceptionMonitor]
    public async Task NormalPlayKeepsLiveShaderTimeAndEveryNamedSceneIsAnActualLoadableResource()
    {
        AssertThat(ScreenshotRun.Active).IsFalse();
        // The pinned engine's global-uniform getter is editor-only. Read the
        // setting and test live rendered water rather than calling it at runtime.
        var configured = ProjectSettings.GetSetting("shader_globals/capture_time").AsGodotDictionary();
        AssertThat(configured["value"].AsDouble()).IsEqual(-1d);
        foreach (string path in ScreenshotScenarios.All.Select(item => item.Scene).Distinct(StringComparer.Ordinal))
        { AssertThat(ResourceLoader.Exists(path, "PackedScene")).IsTrue(); AssertThat(GD.Load<PackedScene>(path) is not null).IsTrue(); }
        if (DisplayServer.GetName() != "headless")
        {
            var root = new Node(); using ISceneRunner runner = ISceneRunner.Load(root, autoFree: true);
            var viewport = new SubViewport { Size = new(64, 64), OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always }; root.AddChild(viewport);
            var camera = new Camera3D { Position = new(0, 0, 3), Projection = Camera3D.ProjectionType.Orthogonal, Size = 2.5f }; viewport.AddChild(camera);
            var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/water.gdshader") };
            material.SetShaderParameter("albedo_atlas", GD.Load<Texture2D>("res://textures/terrain_atlas.png")); material.SetShaderParameter("atlas_rect", new Vector4(0, 0, 1, 1));
            viewport.AddChild(new MeshInstance3D { Mesh = new QuadMesh { Size = new(2, 2) }, MaterialOverride = material });
            viewport.AddChild(new WorldEnvironment { Environment = new Godot.Environment { AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.White, AmbientLightEnergy = 1 } });
            await runner.SimulateFrames(3); using Image first = viewport.GetTexture().GetImage();
            await Task.Delay(250); await runner.SimulateFrames(2); using Image second = viewport.GetTexture().GetImage();
            AssertThat(first.GetData().SequenceEqual(second.GetData())).IsFalse();
        }
    }
}
