using System.Text.Json;
using Dungeons.Game.Persistence;
using Godot;

namespace Dungeons.Game.Rendering;

/// <summary>Shared presentation policy; thermal history survives level changes and pauses.</summary>
public partial class GraphicsRuntime : Node
{
    private readonly List<WorldView> _views = [];
    private bool _measuring;
    public static GraphicsRuntime? Instance { get; private set; }
    public DeviceSelection Selection { get; private set; }
    public DeviceSelection InitialSelection { get; private set; }
    public string DeviceRule { get; private set; } = "";
    public GraphicsOptions Options { get; private set; }
    public AdaptiveResolutionModel Adaptive { get; } = new();
    public bool Mobile { get; private set; }
    public bool CompatibilityRenderer { get; private set; }
    public string Renderer { get; private set; } = "";
    public string Adapter { get; private set; } = "";
    public override void _Ready()
    {
        Instance = this; ProcessPriority = 1000;
        Mobile = OS.HasFeature("mobile"); Renderer = RenderingServer.GetCurrentRenderingMethod();
        CompatibilityRenderer = DisplayServer.GetName() != "headless" && Renderer == "gl_compatibility";
        Adapter = RenderingServer.GetVideoAdapterName(); DeviceProfileCatalog profiles;
        try { profiles = DeviceProfileCatalog.Parse(ContentBridge.ReadText("res://content/device_profiles.json")); }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException or KeyNotFoundException or FormatException or IOException)
        { GD.PushWarning("Device profile table rejected; using platform defaults: " + error.Message); profiles = DeviceProfileCatalog.Empty; }
        DeviceSelection selection = profiles.Select(new(OS.GetName().ToLowerInvariant(), Adapter, OS.GetModelName(), Mobile, !CompatibilityRenderer));
        if (OS.HasFeature("android_low")) { selection = new(QualityPreset.Low, RendererOverride.Compatibility, "android-low-export"); }
        DeviceRule = selection.Rule;
        string[] args = OS.GetCmdlineUserArgs();
        for (int i = 0; i < args.Length; i++)
        {
            string? value = args[i].StartsWith("--quality=", StringComparison.Ordinal) ? args[i][10..] : args[i] == "--quality" && i + 1 < args.Length ? args[++i] : null;
            if (value is null) { continue; }
            if (!Enum.TryParse(value, true, out QualityPreset preset) || preset == QualityPreset.Custom) { GD.PushWarning("Unknown quality preset: " + value); continue; }
            selection = selection with { Preset = preset, Rule = "command-line" };
        }
        InitialSelection = selection; Configure(selection, Mobile);
        GD.Print($"GRAPHICS_PROFILE preset={Options.Preset} rule={Selection.Rule} renderer={Renderer} adapter={Adapter} scale={Adaptive.Scale:F2}");
    }
    public void Configure(DeviceSelection selection, bool mobile)
    {
        Selection = selection; Mobile = mobile; ApplyOptions(GraphicsOptions.ForPreset(selection.Preset, mobile));
    }
    public void ConfigureSettings(VideoSettings video)
    {
        Selection = Selection with { Preset = video.Preset, Rule = "settings" };
        GraphicsOptions requested = CompatibilityRenderer ? GraphicsOptions.ForPreset(QualityPreset.Low, Mobile) : video.Graphics(Mobile);
        if (requested != Options) { ApplyOptions(requested); }
    }
    private void ApplyOptions(GraphicsOptions options)
    {
        Options = CompatibilityRenderer ? GraphicsOptions.ForPreset(QualityPreset.Low, Mobile) : options;
        Adaptive.Configure(Options.ResolutionScale, Options.ResolutionScale); _measuring = false;
        RenderingServer.DirectionalShadowAtlasSetSize(Options.ShadowAtlas, false);
        for (int i = 0; i < _views.Count; i++) { if (GodotObject.IsInstanceValid(_views[i])) { _views[i].ApplyGraphics(Options, Adaptive.Scale, CompatibilityRenderer); } }
    }
    public void Attach(WorldView view)
    { _views.Add(view); view.ApplyGraphics(Options, Adaptive.Scale, CompatibilityRenderer); }
    public void Detach(WorldView view) => _views.Remove(view);
    public override void _Process(double delta)
    {
        bool active = false;
        for (int i = _views.Count - 1; i >= 0; i--)
        { if (!GodotObject.IsInstanceValid(_views[i])) { _views.RemoveAt(i); } else if (_views[i].CanMeasureGraphics) { active = true; } }
        if (!Options.Adaptive || !active)
        { if (_measuring) { Adaptive.ResetWindow(); _measuring = false; } return; }
        _measuring = true;
        if (Adaptive.Sample(delta * 1000)) { for (int i = 0; i < _views.Count; i++) { _views[i].ApplyResolution(Adaptive.Scale); } }
    }
    /// <summary>Runs before catalogue/profile IO; mobile uses its supported create-instance relaunch.</summary>
    public bool TryRendererRelaunch(string? diagnosticLogPath = null)
    {
        if (Selection.Renderer != RendererOverride.Compatibility || CompatibilityRenderer || DisplayServer.GetName() == "headless") { return false; }
        if (OS.GetCmdlineUserArgs().Contains(RendererArguments.AttemptedFlag)) { GD.PushWarning("Compatibility relaunch did not select the requested renderer; using Low settings on the available backend."); return false; }
        if (OS.HasFeature("ios")) { GD.PushWarning("Renderer relaunch is unavailable on iOS."); return false; }
        // The Android host kills this process during rebirth. Finish granted-URI
        // copies first so the new process can validate their committed inbox files.
        if (OS.HasFeature("android") && PlatformPaths.AndroidImportBusy) { return true; }
        string[] arguments = RendererArguments.FromGodot(OS.GetCmdlineArgs(), OS.GetCmdlineUserArgs());
        // The pinned host consumes built-in flags before exposing GetCmdlineArgs.
        // Keep the selected audio backend when replacing the renderer process.
        int separator = Array.IndexOf(arguments, "--");
        if (diagnosticLogPath is not null)
        { var diagnosticArguments = arguments.ToList(); diagnosticArguments.InsertRange(separator, ["--log-file", diagnosticLogPath]); arguments = diagnosticArguments.ToArray(); separator += 2; }
        if (!arguments.Take(separator).Contains("--audio-driver"))
        { var preserved = arguments.ToList(); preserved.InsertRange(separator, ["--audio-driver", AudioServer.GetDriverName()]); arguments = preserved.ToArray(); }
        int process = OS.CreateInstance(arguments);
        if (process < 0) { GD.PushWarning("Could not relaunch with the device profile's Compatibility renderer."); return false; }
        GD.Print("GRAPHICS_RELAUNCH=compatibility");
        // Android's GodotActivity replaces the process itself. Desktop runs independently.
        if (!OS.HasFeature("android")) { GetTree().Quit(); } return true;
    }
    public override void _ExitTree() { if (Instance == this) { Instance = null; } _views.Clear(); }
}
