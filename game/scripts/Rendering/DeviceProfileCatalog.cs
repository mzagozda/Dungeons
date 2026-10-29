using System.Text.Json;

namespace Dungeons.Game.Rendering;

/// <summary>Bounded, ordered device rules; wildcard matching has no regular-expression engine.</summary>
public sealed class DeviceProfileCatalog
{
    private readonly record struct Rule(string Id, string Platform, string Adapter, string Model, QualityPreset Preset, RendererOverride Renderer);
    private readonly Rule[] _rules;
    private DeviceProfileCatalog(Rule[] rules) { _rules = rules; }
    public static DeviceProfileCatalog Empty { get; } = new([]);
    public static DeviceProfileCatalog Parse(string json)
    {
        if (json.Length > 65536) { throw new JsonException("Device profiles exceed 64 KiB."); }
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 }); JsonElement root = document.RootElement;
        Fields(root, ["formatVersion", "rules"]); if (root.GetProperty("formatVersion").GetInt32() != 1) { throw new JsonException("Unsupported device profile version."); }
        JsonElement source = root.GetProperty("rules"); if (source.ValueKind != JsonValueKind.Array || source.GetArrayLength() > 64) { throw new JsonException("At most 64 device rules are allowed."); }
        var rules = new List<Rule>(); var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement entry in source.EnumerateArray())
        {
            Fields(entry, ["id", "platform", "adapter", "model", "preset", "renderer", "notes"]);
            string Text(string key, string fallback = "") => entry.TryGetProperty(key, out JsonElement value) ? value.GetString() ?? throw new JsonException("Rule fields cannot be null.") : fallback;
            string id = Text("id"), platform = Text("platform", "any"), adapter = Text("adapter", "*"), model = Text("model", "*");
            if (id.Length is < 1 or > 64 || !ids.Add(id) || platform is not ("any" or "android" or "ios" or "windows" or "linux" or "macos") || adapter.Length is < 1 or > 128 || model.Length is < 1 or > 128)
            { throw new JsonException("Invalid or duplicate device rule."); }
            QualityPreset preset = Text("preset") switch { "Low" => QualityPreset.Low, "Medium" => QualityPreset.Medium, "High" => QualityPreset.High, _ => throw new JsonException("Device preset must be Low, Medium or High.") };
            RendererOverride renderer = Text("renderer", "auto") switch { "auto" => RendererOverride.None, "compatibility" => RendererOverride.Compatibility, _ => throw new JsonException("Unknown renderer override.") };
            if (renderer == RendererOverride.Compatibility && platform is "ios" or "any") { throw new JsonException("Renderer relaunch rules must select a platform that supports process relaunch."); }
            rules.Add(new(id, platform, adapter, model, preset, renderer));
        }
        return new(rules.ToArray());
    }
    public DeviceSelection Select(DeviceCapabilities device)
    {
        foreach (Rule rule in _rules)
        {
            if (rule.Platform != "any" && rule.Platform != device.Platform || !Matches(rule.Adapter, device.Adapter) || !Matches(rule.Model, device.Model)) { continue; }
            return new(device.RenderingDevice ? rule.Preset : QualityPreset.Low, rule.Renderer, rule.Id);
        }
        return !device.RenderingDevice ? new(QualityPreset.Low, RendererOverride.None, "default-compatibility") : device.Mobile ? new(QualityPreset.Medium, RendererOverride.None, "default-mobile") : new(QualityPreset.High, RendererOverride.None, "default-desktop");
    }
    public static bool Matches(string pattern, string value)
    {
        int p = 0, v = 0, star = -1, retry = 0;
        while (v < value.Length)
        {
            if (p < pattern.Length && (pattern[p] == '?' || char.ToUpperInvariant(pattern[p]) == char.ToUpperInvariant(value[v]))) { p++; v++; }
            else if (p < pattern.Length && pattern[p] == '*') { star = p++; retry = v; }
            else if (star >= 0) { p = star + 1; v = ++retry; }
            else { return false; }
        }
        while (p < pattern.Length && pattern[p] == '*') { p++; } return p == pattern.Length;
    }
    private static void Fields(JsonElement element, string[] allowed)
    {
        if (element.ValueKind != JsonValueKind.Object) { throw new JsonException("Device profile must be an object."); }
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject()) { if (!names.Add(property.Name) || !allowed.Contains(property.Name, StringComparer.Ordinal)) { throw new JsonException("Unknown or duplicate profile field: " + property.Name); } }
    }
}
