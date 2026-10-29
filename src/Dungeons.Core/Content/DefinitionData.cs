using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Dungeons.Core.Level;
using Dungeons.Core.Util;

namespace Dungeons.Core.Content;

/// <summary>Immutable preserved definition metadata for rendering, audio and future authoring tools.</summary>
public sealed class DefinitionData
{
    internal DefinitionData(JsonObject source)
    {
        using JsonDocument document = JsonDocument.Parse(JsonData.Stringify(source)); Json = document.RootElement.Clone();
        Properties = new PropertySchema(source["props"] as JsonObject ?? new JsonObject());
    }
    /// <summary>Metadata for code-authored fixtures.</summary>
    public static DefinitionData Empty { get; } = new(new JsonObject());
    /// <summary>Preserved read-only source tree, including extension fields.</summary>
    public JsonElement Json { get; }
    /// <summary>Self-describing inspector properties.</summary>
    public PropertySchema Properties { get; }
    /// <summary>Display name from content, or its id.</summary>
    public string DisplayName => Json.TryGetProperty("displayName", out JsonElement value) ? value.GetString()! : Json.TryGetProperty("id", out value) ? value.GetString()! : "";
    /// <summary>Editor palette category.</summary>
    public string Category => Json.TryGetProperty("category", out JsonElement value) ? value.GetString()! : "";
    /// <summary>Authored relative editor icon path, or empty when icon art is absent.</summary>
    public string EditorIcon => EditorValue("icon");
    /// <summary>Authored single-character palette hint, or null when not supplied.</summary>
    public char? EditorChar => EditorValue("char") is { Length: 1 } text ? text[0] : null;
    private string EditorValue(string key)
        => Json.TryGetProperty("editor", out JsonElement editor) && editor.ValueKind == JsonValueKind.Object && editor.TryGetProperty(key, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
}

/// <summary>An immutable property definition, shared by validators and generated inspectors.</summary>
public sealed class PropertyDefinition
{
    internal PropertyDefinition(string name, JsonObject schema)
    { Name = name; Schema = schema.DeepClone().AsObject(); Type = JsonData.Text(schema["type"]); RefKind = JsonData.Text(schema["refKind"]); Label = JsonData.Text(schema["label"], name); }
    internal JsonObject Schema { get; }
    /// <summary>Case-sensitive property key.</summary>
    public string Name { get; }
    /// <summary>int, float, bool, string, enum, color, ref or vec2.</summary>
    public string Type { get; }
    /// <summary>Referenced catalogue/level collection, or empty.</summary>
    public string RefKind { get; }
    /// <summary>Human-readable inspector label.</summary>
    public string Label { get; }
    /// <summary>Copy of the schema for an external editor.</summary>
    public JsonObject ToJson() => Schema.DeepClone().AsObject();
}

/// <summary>Closed typed property bag with defaults and ranges (§11.4).</summary>
public sealed class PropertySchema
{
    private static readonly HashSet<string> _types = new(["int", "float", "bool", "string", "enum", "color", "ref", "vec2"], StringComparer.Ordinal);
    private readonly Dictionary<string, PropertyDefinition> _definitions = new(StringComparer.Ordinal);
    internal PropertySchema(JsonObject schema)
    {
        foreach (KeyValuePair<string, JsonNode?> pair in schema)
        {
            if (pair.Value is not JsonObject obj || !_types.Contains(JsonData.Text(obj["type"]))) { throw new ArgumentException("Invalid property schema: " + pair.Key); }
            _definitions.Add(pair.Key, new(pair.Key, obj));
        }
        Definitions = new ReadOnlyDictionary<string, PropertyDefinition>(_definitions);
        foreach (PropertyDefinition property in _definitions.Values)
        {
            foreach (string key in new[] { "min", "max", "step" })
            {
                if (property.Schema.ContainsKey(key) && (property.Schema[key]?.GetValueKind() != JsonValueKind.Number || !double.IsFinite(JsonData.Number(property.Schema[key], double.NaN)) || key == "step" && JsonData.Number(property.Schema[key]) <= 0)) { throw new ArgumentException("Invalid numeric inspector bound."); }
            }
            if (property.Schema.ContainsKey("maxLength") && (JsonData.Integer(property.Schema["maxLength"], -1) < 0)) { throw new ArgumentException("Invalid maxLength."); }
            if (property.Type == "enum" && (property.Schema["values"] is not JsonArray choices || choices.Count == 0 || choices.Any(c => c?.GetValueKind() != JsonValueKind.String) || choices.Select(c => JsonData.Text(c)).Distinct(StringComparer.Ordinal).Count() != choices.Count)) { throw new ArgumentException("Distinct enum choices are required."); }
            if (property.Type == "ref" && !new[] { "entity", "loot", "sound", "tag", "objective", "flag", "marker" }.Contains(property.RefKind, StringComparer.Ordinal)) { throw new ArgumentException("Unknown reference kind."); }
            if (JsonData.Number(property.Schema["min"], double.NegativeInfinity) > JsonData.Number(property.Schema["max"], double.PositiveInfinity)) { throw new ArgumentException("Property minimum exceeds maximum."); }
            if (property.Schema.ContainsKey("default") && !Accepts(property, property.Schema["default"])) { throw new ArgumentException("Invalid property default: " + property.Name); }
        }
    }
    /// <summary>Read-only named descriptors.</summary>
    public IReadOnlyDictionary<string, PropertyDefinition> Definitions { get; }
    /// <summary>Copies, checks and fills defaults. Level-specific references are checked by LevelValidator.</summary>
    public JsonObject Normalize(JsonObject? supplied, ICollection<Diagnostic> diagnostics, string path, DiagnosticCode errorCode = DiagnosticCode.LVL1011)
    {
        JsonObject result = supplied?.DeepClone().AsObject() ?? new();
        foreach (KeyValuePair<string, JsonNode?> pair in result)
        {
            if (!_definitions.TryGetValue(pair.Key, out PropertyDefinition? definition) || !Accepts(definition, pair.Value))
            { diagnostics.Add(new(DiagnosticSeverity.Error, errorCode, "Unknown property or value outside its schema.", path + "/" + JsonData.Pointer(pair.Key))); }
        }
        foreach (PropertyDefinition definition in _definitions.Values)
        {
            if (!result.ContainsKey(definition.Name) && definition.Schema.ContainsKey("default"))
            {
                result[definition.Name] = definition.Schema["default"]?.DeepClone();
                diagnostics.Add(new(DiagnosticSeverity.Info, DiagnosticCode.LVL1060, "Property default applied.", path + "/" + JsonData.Pointer(definition.Name)));
            }
        }
        return result;
    }
    private static bool Accepts(PropertyDefinition definition, JsonNode? value)
    {
        JsonObject schema = definition.Schema; JsonValueKind kind = value?.GetValueKind() ?? JsonValueKind.Null;
        double number = JsonData.Number(value, double.NaN);
        bool valid = definition.Type switch
        {
            "int" => kind == JsonValueKind.Number && number == System.Math.Truncate(number),
            "float" => kind == JsonValueKind.Number,
            "bool" => kind is JsonValueKind.True or JsonValueKind.False,
            "string" or "ref" => kind == JsonValueKind.String,
            "enum" => kind == JsonValueKind.String && schema["values"] is JsonArray choices && choices.Any(v => JsonNode.DeepEquals(v, value)),
            "color" => kind == JsonValueKind.String && Regex.IsMatch(JsonData.Text(value), "^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant),
            "vec2" => value is JsonArray vector && vector.Count == 2 && vector.All(v => v?.GetValueKind() == JsonValueKind.Number && !double.IsInfinity(JsonData.Number(v, double.NaN)) && !double.IsNaN(JsonData.Number(v, double.NaN))),
            _ => false,
        };
        if (!valid) { return false; }
        if (kind == JsonValueKind.Number && (double.IsInfinity(number) || double.IsNaN(number) || number < JsonData.Number(schema["min"], double.NegativeInfinity) || number > JsonData.Number(schema["max"], double.PositiveInfinity))) { return false; }
        return kind != JsonValueKind.String || JsonData.Text(value).Length <= JsonData.Integer(schema["maxLength"], 512);
    }
}
