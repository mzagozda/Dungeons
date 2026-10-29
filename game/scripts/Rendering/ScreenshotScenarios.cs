namespace Dungeons.Game.Rendering;

/// <summary>Named visual fixtures use the same production scenes as milestone acceptance captures.</summary>
public sealed record ScreenshotScenario(string Id, string Scene, string OutputFlag, string ExpectedMarker, IReadOnlyList<string> Arguments, bool NeedsReplay = false, int Width = 0, int Height = 0);

/// <summary>Engine-free immutable command catalogue. Fixture inputs are fixed; renderer and desktop resolution remain caller choices.</summary>
public static class ScreenshotScenarios
{
    private static ScreenshotScenario Fixture(string id, string scene, string output, string marker, params string[] arguments)
        => new(id, "res://scenes/" + scene + ".tscn", output, marker, Array.AsReadOnly(arguments));
    public static IReadOnlyList<ScreenshotScenario> All { get; } = Array.AsReadOnly(new[]
    {
        Fixture("terrain-bridge", "m3_preview", "--m3-screenshot", "M3_PREVIEW=PASS", "--m3-location=16.5,34.5"),
        Fixture("terrain-stairs", "m3_preview", "--m3-screenshot", "M3_PREVIEW=PASS", "--m3-location=16.5,12.5"),
        Fixture("terrain-ruin", "m3_preview", "--m3-screenshot", "M3_PREVIEW=PASS", "--m3-location=16.5,6.5"),
        Fixture("hero-roll", "m4_preview", "--m4-screenshot", "M4_PREVIEW=PASS action=roll", "--m4-action=roll"),
        Fixture("hero-bow", "m4_preview", "--m4-screenshot", "M4_PREVIEW=PASS action=bow", "--m4-action=bow"),
        Fixture("hero-melee", "m4_preview", "--m4-screenshot", "M4_PREVIEW=PASS action=melee", "--m4-action=melee"),
        Fixture("hero-pickup", "m4_preview", "--m4-screenshot", "M4_PREVIEW=PASS action=pickup", "--m4-action=pickup"),
        Fixture("combat", "m5_preview", "--m5-screenshot", "M5_PREVIEW=PASS"),
        Fixture("mission-death", "m6_preview", "--m6-screenshot", "M6_PREVIEW=PASS mode=death", "--m6-mode=death"),
        Fixture("mission-failed", "m6_preview", "--m6-screenshot", "M6_PREVIEW=PASS mode=failed", "--m6-mode=failed"),
        Fixture("mission-objects", "m6_preview", "--m6-screenshot", "M6_PREVIEW=PASS mode=objects", "--m6-mode=objects"),
        Fixture("mission-complete", "m6_preview", "--m6-screenshot", "M6_PREVIEW=PASS mode=complete", "--m6-mode=complete") with { NeedsReplay = true },
        Fixture("touch-phone", "m7_preview", "--m7-screenshot", "M7_PREVIEW=PASS", "--m7-mode=phone") with { Width = 1760, Height = 800 },
        Fixture("touch-aim", "m7_preview", "--m7-screenshot", "M7_PREVIEW=PASS", "--m7-mode=aim") with { Width = 1760, Height = 800 },
        Fixture("touch-left", "m7_preview", "--m7-screenshot", "M7_PREVIEW=PASS", "--m7-mode=left") with { Width = 1760, Height = 800 },
        Fixture("touch-tablet", "m7_preview", "--m7-screenshot", "M7_PREVIEW=PASS", "--m7-mode=tablet") with { Width = 1280, Height = 960 },
        Fixture("settings-video", "m8_settings_preview", "--settings-screenshot", "M8_SETTINGS_PREVIEW=PASS mode=video", "--settings-mode=video"),
        Fixture("settings-audio", "m8_settings_preview", "--settings-screenshot", "M8_SETTINGS_PREVIEW=PASS mode=audio", "--settings-mode=audio"),
        Fixture("settings-controls", "m8_settings_preview", "--settings-screenshot", "M8_SETTINGS_PREVIEW=PASS mode=controls", "--settings-mode=controls"),
        Fixture("settings-accessibility", "m8_settings_preview", "--settings-screenshot", "M8_SETTINGS_PREVIEW=PASS mode=accessibility", "--settings-mode=accessibility"),
        Fixture("phone-hud", "m8_settings_preview", "--settings-screenshot", "M8_SETTINGS_PREVIEW=PASS mode=hud", "--settings-mode=hud", "--settings-phone", "--settings-ui=1.5", "--settings-text=1.5") with { Width = 1760, Height = 800 },
        Fixture("phone-layout", "m8_settings_preview", "--settings-screenshot", "M8_SETTINGS_PREVIEW=PASS mode=controls", "--settings-mode=controls", "--settings-focus=layout", "--settings-phone", "--settings-ui=1.5", "--settings-text=1.5") with { Width = 1760, Height = 800 }
    });
    public static ScreenshotScenario? Find(string id) => All.FirstOrDefault(item => item.Id == id);
    public static bool IsImagePath(string value) => value.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || value.Contains('/') || value.Contains('\\');
}
