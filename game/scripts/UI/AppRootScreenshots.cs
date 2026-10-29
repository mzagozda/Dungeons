using Dungeons.Game.Rendering;
using Godot;

namespace Dungeons.Game;

public partial class AppRoot
{
    private void StartScreenshotScenario(ScreenshotScenario scenario)
    {
        try
        {
            if (DisplayServer.GetName() == "headless") { throw new InvalidOperationException("Named screenshot scenarios require a real renderer/display."); }
            string output = _launch.ScreenshotOutput is { } explicitPath ? ProjectSettings.GlobalizePath(explicitPath) : ProjectSettings.GlobalizePath($"user://screenshots/{Guid.NewGuid():N}/{scenario.Id}.png");
            output = Path.GetFullPath(output);
            if (!output.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) { throw new FormatException("Screenshot output must end with .png."); }
            foreach (string companion in new[] { output, Path.ChangeExtension(output, ".capture.json"), Path.ChangeExtension(output, ".json"), Path.ChangeExtension(output, ".dreplay"), Path.ChangeExtension(output, ".store") })
            { if (File.Exists(companion) || Directory.Exists(companion)) { throw new IOException("Screenshot output already exists: " + companion); } }
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            if (!ResourceLoader.Exists(scenario.Scene)) { throw new InvalidDataException("Missing screenshot scene: " + scenario.Scene); }
            if (scenario.Width > 0) { GetWindow().Size = new(scenario.Width, scenario.Height); }
            ScreenshotRun.Configure(scenario, output, _launch.Replay); _screenshotTest = null; SetProcess(false);
            GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, scenario.Scene);
        }
        catch (Exception error) { Console.Error.WriteLine("SCREENSHOT_SCENARIO=FAIL " + error.Message); GetTree().Quit(error is FormatException or InvalidOperationException ? 2 : 1); }
    }
}
