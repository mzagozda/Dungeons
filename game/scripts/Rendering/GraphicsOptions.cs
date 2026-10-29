namespace Dungeons.Game.Rendering;

public enum QualityPreset { Low, Medium, High, Custom }
public enum RendererOverride { None, Compatibility }

/// <summary>Presentation-only quality settings. Mutable overrides select Custom in the settings UI.</summary>
public readonly record struct GraphicsOptions(QualityPreset Preset, bool Shadows, int ShadowAtlas, bool Glow, float ResolutionScale, int OmniLights, bool Msaa, bool Adaptive)
{
    public static GraphicsOptions ForPreset(QualityPreset preset, bool mobile) => preset switch
    {
        QualityPreset.Low => new(preset, false, 1024, false, .75f, 4, false, mobile),
        QualityPreset.Medium => new(preset, true, 1024, true, 1, 8, false, mobile),
        QualityPreset.High => new(preset, true, 4096, true, 1, 8, !mobile, mobile),
        _ => throw new ArgumentOutOfRangeException(nameof(preset)),
    };
}

public readonly record struct DeviceCapabilities(string Platform, string Adapter, string Model, bool Mobile, bool RenderingDevice);
public readonly record struct DeviceSelection(QualityPreset Preset, RendererOverride Renderer, string Rule);

/// <summary>Builds a renderer relaunch without retaining a conflicting graphics driver.</summary>
public static class RendererArguments
{
    public const string AttemptedFlag = "--renderer-override-attempted";
    /// <summary>Godot removes the separator from GetCmdlineArgs; restore it using the separate user array.</summary>
    public static string[] FromGodot(IReadOnlyList<string> arguments, IReadOnlyList<string> userArguments)
    {
        int count = arguments.Count;
        for (int i = 0; i < count; i++) { if (arguments[i] is "--" or "++") { count = i; break; } }
        if (count == arguments.Count && userArguments.Count <= count)
        {
            bool suffix = true;
            for (int i = 0; i < userArguments.Count; i++) { if (arguments[count - userArguments.Count + i] != userArguments[i]) { suffix = false; break; } }
            if (suffix) { count -= userArguments.Count; }
        }
        var restored = new List<string>(count + userArguments.Count + 1);
        for (int i = 0; i < count; i++) { restored.Add(arguments[i]); }
        restored.Add("--"); for (int i = 0; i < userArguments.Count; i++) { restored.Add(userArguments[i]); }
        return Compatibility(restored);
    }
    public static string[] Compatibility(IReadOnlyList<string> arguments)
    {
        var engine = new List<string>(); var user = new List<string>(); bool afterSeparator = false;
        for (int i = 0; i < arguments.Count; i++)
        {
            string arg = arguments[i];
            if (arg == "--") { afterSeparator = true; continue; }
            if (afterSeparator) { if (arg != AttemptedFlag) { user.Add(arg); } continue; }
            if (arg is "--rendering-method" or "--rendering-driver") { if (i + 1 < arguments.Count && arguments[i + 1] != "--") { i++; } continue; }
            if (arg.StartsWith("--rendering-method=", StringComparison.Ordinal) || arg.StartsWith("--rendering-driver=", StringComparison.Ordinal)) { continue; }
            engine.Add(arg);
        }
        engine.Add("--rendering-method"); engine.Add("gl_compatibility"); engine.Add("--"); engine.AddRange(user); engine.Add(AttemptedFlag); return engine.ToArray();
    }
}
