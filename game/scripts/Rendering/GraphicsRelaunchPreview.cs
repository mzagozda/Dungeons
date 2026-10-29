using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;

namespace Dungeons.Game.Rendering;

/// <summary>Native two-process probe for preserved arguments, actual Compatibility selection and retry guard.</summary>
public partial class GraphicsRelaunchPreview : Node
{
    public override void _Ready()
    {
        try
        {
            string path = ProjectSettings.GlobalizePath(OS.GetCmdlineUserArgs().Single(arg => arg.StartsWith("--graphics-relaunch-report=", StringComparison.Ordinal))[27..]);
            GraphicsRuntime runtime = GraphicsRuntime.Instance ?? throw new InvalidOperationException("Missing graphics runtime.");
            if (runtime.CompatibilityRenderer)
            {
                if (!OS.GetCmdlineUserArgs().Contains(RendererArguments.AttemptedFlag)) { throw new InvalidOperationException("Child renderer started without its retry guard."); }
                using var started = JsonDocument.Parse(System.IO.File.ReadAllText(path + ".started"));
                int parent = started.RootElement.GetProperty("processId").GetInt32();
                if (parent == System.Environment.ProcessId) { throw new InvalidOperationException("Renderer did not create a new process."); }
                if (!OS.GetCmdlineUserArgs().Contains("--preserved-argument=kept")) { throw new InvalidOperationException("User arguments were not preserved."); }
                string logPath = path + ".native.log";
                System.IO.File.WriteAllText(path, new JsonObject { ["passed"] = true, ["renderer"] = runtime.Renderer, ["audioDriver"] = AudioServer.GetDriverName(), ["logPath"] = logPath, ["parentProcessId"] = parent, ["processId"] = System.Environment.ProcessId, ["retryGuard"] = true, ["userArgumentsPreserved"] = true }.ToJsonString());
                GD.Print("GRAPHICS_RELAUNCH_PREVIEW=PASS report=" + System.IO.Path.GetFileName(path)); GetTree().Quit(); return;
            }
            System.IO.File.WriteAllText(path + ".started", new JsonObject { ["processId"] = System.Environment.ProcessId, ["renderer"] = runtime.Renderer,
                ["engineArguments"] = new JsonArray(OS.GetCmdlineArgs().Select(arg => (JsonNode?)JsonValue.Create(arg)).ToArray()),
                ["userArguments"] = new JsonArray(OS.GetCmdlineUserArgs().Select(arg => (JsonNode?)JsonValue.Create(arg)).ToArray()) }.ToJsonString());
            runtime.Configure(new(QualityPreset.Low, RendererOverride.Compatibility, "native-relaunch-probe"), false);
            if (!runtime.TryRendererRelaunch(path + ".native.log")) { throw new InvalidOperationException("Native renderer relaunch was not dispatched."); }
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
}
