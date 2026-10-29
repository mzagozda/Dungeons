using Dungeons.Game.Rendering;

namespace Dungeons.Game.Persistence;

public enum ShadowQuality { Off, Low, High }
public enum AimAssist { Off, Normal, Strong }
public enum HealthPalette { Default, Protanopia, Deuteranopia, Tritanopia }
public enum BindingKind { Key, MouseButton, JoyButton, JoyAxis }
public readonly record struct InputBinding(BindingKind Kind, int Code, int Direction = 1, bool Shift = false, bool Control = false, bool Alt = false, bool Meta = false);
public readonly record struct TouchPosition(float X, float Y);

public sealed record VideoSettings
{
    public QualityPreset Preset { get; init; } = QualityPreset.High;
    public ShadowQuality Shadows { get; init; } = ShadowQuality.High;
    public bool Glow { get; init; } = true;
    public float ResolutionScale { get; init; } = 1;
    public int OmniLights { get; init; } = 8;
    public bool Msaa { get; init; } = true;
    public bool Adaptive { get; init; }
    public bool Fullscreen { get; init; }
    public bool Vsync { get; init; } = true;
    public float UiScale { get; init; } = 1;
    public int WindowWidth { get; init; } = 1600;
    public int WindowHeight { get; init; } = 900;
    public int? WindowX { get; init; }
    public int? WindowY { get; init; }
    public VideoSettings WithPreset(QualityPreset preset, bool mobile)
    {
        GraphicsOptions options = GraphicsOptions.ForPreset(preset, mobile);
        return this with { Preset = preset, Shadows = !options.Shadows ? ShadowQuality.Off : options.ShadowAtlas == 4096 ? ShadowQuality.High : ShadowQuality.Low,
            Glow = options.Glow, ResolutionScale = options.ResolutionScale, OmniLights = options.OmniLights, Msaa = options.Msaa, Adaptive = options.Adaptive };
    }
    public GraphicsOptions Graphics(bool mobile) => new(Preset, Shadows != ShadowQuality.Off, Shadows == ShadowQuality.High ? 4096 : 1024, Glow, ResolutionScale, OmniLights, Msaa && !mobile, Adaptive && mobile);
}
public sealed record AudioSettings
{
    public int Master { get; init; } = 100;
    public int Music { get; init; } = 70;
    public int Sfx { get; init; } = 100;
    public int Ui { get; init; } = 80;
    public int Ambience { get; init; } = 65;
}
public sealed record ControlSettings
{
    public bool Vibration { get; init; } = true;
    public AimAssist AutoAim { get; init; } = AimAssist.Normal;
    public bool LeftHanded { get; init; }
    public float TouchScale { get; init; } = 1;
    public Dictionary<string, TouchPosition> TouchPositions { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, InputBinding[]> Bindings { get; init; } = new(StringComparer.Ordinal);
}
public sealed record AccessibilitySettings
{
    public bool DamageNumbers { get; init; } = true;
    public bool HighContrastHealth { get; init; }
    public HealthPalette HealthPalette { get; init; }
    public float TextScale { get; init; } = 1;
    public bool ReduceMotion { get; init; }
}
public sealed record GameSettings
{
    public int FormatVersion { get; init; } = 1;
    public VideoSettings Video { get; init; } = new();
    public AudioSettings Audio { get; init; } = new();
    public ControlSettings Controls { get; init; } = new();
    public AccessibilitySettings Accessibility { get; init; } = new();
    public static GameSettings Defaults(QualityPreset preset, bool mobile) => new() { Video = new VideoSettings().WithPreset(preset, mobile) };
}

/// <summary>One bounded settings contract shared by persistence and runtime/editor application.</summary>
public static class SettingsValidation
{
    public static readonly string[] Actions = ["move_up", "move_down", "move_left", "move_right", "attack", "ranged", "roll", "potion", "interact", "pause"];
    public static readonly string[] TouchActions = ["Attack", "Roll", "Ranged", "Potion"];
    public static void Validate(GameSettings settings)
    {
        if (settings.FormatVersion != 1 || settings.Video is null || settings.Audio is null || settings.Controls is null || settings.Accessibility is null) { throw new ArgumentException("Unsupported or incomplete settings."); }
        VideoSettings video = settings.Video; AudioSettings audio = settings.Audio; ControlSettings controls = settings.Controls; AccessibilitySettings access = settings.Accessibility;
        if (!Enum.IsDefined(video.Preset) || !Enum.IsDefined(video.Shadows) || !Range(video.ResolutionScale, .6f, 1) || !Range(video.UiScale, .8f, 1.5f) || video.OmniLights is not (4 or 8)
            || video.WindowWidth is < 640 or > 7680 || video.WindowHeight is < 360 or > 4320 || video.WindowX is < -100000 or > 100000 || video.WindowY is < -100000 or > 100000 || video.WindowX.HasValue != video.WindowY.HasValue) { throw new ArgumentException("Invalid video settings."); }
        if (audio.Master is < 0 or > 100 || audio.Music is < 0 or > 100 || audio.Sfx is < 0 or > 100 || audio.Ui is < 0 or > 100 || audio.Ambience is < 0 or > 100) { throw new ArgumentException("Volume must be between 0 and 100."); }
        if (!Enum.IsDefined(controls.AutoAim) || !Range(controls.TouchScale, .8f, 1.5f) || controls.TouchPositions is null || controls.TouchPositions.Count > 4 || controls.Bindings is null || controls.Bindings.Count > Actions.Length) { throw new ArgumentException("Invalid control settings."); }
        foreach (var pair in controls.TouchPositions)
        { if (!TouchActions.Contains(pair.Key, StringComparer.Ordinal) || !Range(pair.Value.X, 0, 1) || !Range(pair.Value.Y, 0, 1)) { throw new ArgumentException("Invalid touch position."); } }
        foreach (var pair in controls.Bindings)
        {
            if (!Actions.Contains(pair.Key, StringComparer.Ordinal) || pair.Value is null || pair.Value.Length > 16) { throw new ArgumentException("Invalid action binding list."); }
            foreach (InputBinding binding in pair.Value)
            {
                if (!IsValidBinding(binding)) { throw new ArgumentException("Invalid input binding."); }
            }
        }
        if (!Enum.IsDefined(access.HealthPalette) || !Range(access.TextScale, .8f, 1.5f)) { throw new ArgumentException("Invalid accessibility settings."); }
    }
    public static bool IsValidBinding(InputBinding binding)
    {
        bool valid = binding.Kind switch { BindingKind.Key => binding.Code is > 0 and <= 0x1ffffff, BindingKind.MouseButton => binding.Code is >= 1 and <= 9, BindingKind.JoyButton => binding.Code is >= 0 and < 128, BindingKind.JoyAxis => binding.Code is >= 0 and <= 9, _ => false };
        return valid && binding.Direction is -1 or 1 && (binding.Kind == BindingKind.Key || !(binding.Shift || binding.Control || binding.Alt || binding.Meta));
    }
    private static bool Range(float value, float minimum, float maximum) => float.IsFinite(value) && value >= minimum && value <= maximum;
}
