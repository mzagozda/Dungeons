using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dungeons.Core.Util;

namespace Dungeons.Core.Level;

/// <summary>Immutable form field derived from the production level schema.</summary>
public sealed class TriggerParameter
{
    internal TriggerParameter(string name, JsonObject source, bool required, string ownerType, JsonObject definitions)
    {
        Name = name; Required = required || JsonData.Boolean(source["x-required"]);
        SchemaReference = JsonData.Text(source["$ref"]);
        JsonObject schema = source.DeepClone().AsObject();
        if (SchemaReference.Length > 0 && SchemaReference != "#/$defs/condition")
        {
            var resolved = definitions[SchemaReference.Substring("#/$defs/".Length)]!.DeepClone().AsObject();
            foreach (var pair in schema) { if (pair.Key != "$ref") { resolved[pair.Key] = pair.Value?.DeepClone(); } }
            schema = resolved;
        }
        RefKind = JsonData.Text(schema["x-refKindByType"]?[ownerType], JsonData.Text(schema["x-refKind"]));
        PropertySchemaFrom = JsonData.Text(schema["x-propertySchemaFrom"]);
        TargetKinds = Array.AsReadOnly(((schema["x-targetKindsByType"]?[ownerType] ?? schema["x-targetKinds"]) as JsonArray)?.Select(value => JsonData.Text(value)).ToArray() ?? []);
        Choices = Array.AsReadOnly((schema["enum"] as JsonArray)?.Select(value => JsonData.Text(value)).ToArray() ?? []);
        Type = SchemaReference == "#/$defs/condition" ? "condition" : Choices.Count > 0 ? "enum" : JsonData.Text(schema["type"], SchemaReference.Length > 0 ? SchemaReference.Substring("#/$defs/".Length) : "any");
        using (JsonDocument json = JsonDocument.Parse(JsonData.Stringify(schema))) { Schema = json.RootElement.Clone(); }
        var properties = new Dictionary<string, TriggerParameter>(StringComparer.Ordinal);
        if (schema["properties"] is JsonObject fields)
        {
            var requiredKeys = new HashSet<string>((schema["required"] as JsonArray)?.Select(value => JsonData.Text(value)) ?? [], StringComparer.Ordinal);
            foreach (var pair in fields) { properties.Add(pair.Key, new(pair.Key, pair.Value!.AsObject(), requiredKeys.Contains(pair.Key), ownerType, definitions)); }
        }
        Properties = new ReadOnlyDictionary<string, TriggerParameter>(properties);
        if (schema["items"] is JsonObject items) { Items = new("[]", items, false, ownerType, definitions); }
    }
    /// <summary>Authored JSON key.</summary>
    public string Name { get; }
    /// <summary>JSON type, enum, localizedString or condition (select from TriggerSchema.Conditions).</summary>
    public string Type { get; }
    /// <summary>Whether loading/semantic validation requires the field.</summary>
    public bool Required { get; }
    /// <summary>entity means a placed id; entityDefinition means a content prototype. Other kinds are objective, flag, tag, trigger and sound.</summary>
    public string RefKind { get; }
    /// <summary>Optional allowed entity kinds for a placed-id picker; further state/property restrictions still use LevelValidator.</summary>
    public IReadOnlyList<string> TargetKinds { get; }
    /// <summary>Permitted enum values in authored order.</summary>
    public IReadOnlyList<string> Choices { get; }
    /// <summary>Original local schema reference, or empty.</summary>
    public string SchemaReference { get; }
    /// <summary>Sibling prototype field whose content PropertySchema supplies this property bag, or empty.</summary>
    public string PropertySchemaFrom { get; }
    /// <summary>Immutable resolved JSON schema including ranges/defaults and authoring annotations. Recursive conditions keep their reference.</summary>
    public JsonElement Schema { get; }
    /// <summary>Nested object fields, including area coordinates and spawn prototype fields.</summary>
    public IReadOnlyDictionary<string, TriggerParameter> Properties { get; }
    /// <summary>Array item descriptor, or null.</summary>
    public TriggerParameter? Items { get; }
}

/// <summary>One supported event/action/condition and the parameters needed by a generated inspector.</summary>
public sealed class TriggerTypeSchema
{
    private readonly JsonObject _schema;
    internal TriggerTypeSchema(string type, JsonObject schema, JsonObject definitions)
    {
        Type = type; _schema = schema.DeepClone().AsObject();
        var required = new HashSet<string>((schema["required"] as JsonArray)?.Select(value => JsonData.Text(value)) ?? [], StringComparer.Ordinal);
        var parameters = new Dictionary<string, TriggerParameter>(StringComparer.Ordinal);
        foreach (var pair in schema["properties"]!.AsObject())
        { if (pair.Key != "type") { parameters.Add(pair.Key, new(pair.Key, pair.Value!.AsObject(), required.Contains(pair.Key), type, definitions)); } }
        Parameters = new ReadOnlyDictionary<string, TriggerParameter>(parameters);
    }
    /// <summary>Case-sensitive runtime discriminator.</summary>
    public string Type { get; }
    /// <summary>Read-only fields; the discriminator itself is supplied by Type.</summary>
    public IReadOnlyDictionary<string, TriggerParameter> Parameters { get; }
    /// <summary>Independent flattened schema. Local references resolve through TriggerSchema.ToJson's $defs.</summary>
    public JsonObject ToJson() => _schema.DeepClone().AsObject();
}

/// <summary>Generated-inspector contract for every supported v1 event, action and condition.
/// Type lists, field constraints and reference annotations come from the embedded production schema.</summary>
public static class TriggerSchema
{
    private static readonly JsonObject _definitions = JsonData.Parse(LevelSchema.Json)["$defs"]!.AsObject();
    /// <summary>Supported trigger events, excluding unknown/reserved runtime types.</summary>
    public static IReadOnlyDictionary<string, TriggerTypeSchema> Events { get; } = Build("event");
    /// <summary>Supported actions. camera_focus/dialogue are reserved and remain validation errors.</summary>
    public static IReadOnlyDictionary<string, TriggerTypeSchema> Actions { get; } = Build("action");
    /// <summary>Supported conditions; a recursive not field selects another entry in this collection.</summary>
    public static IReadOnlyDictionary<string, TriggerTypeSchema> Conditions { get; } = Build("condition");
    /// <summary>Independent machine-readable catalogue with all local reference definitions.</summary>
    public static JsonObject ToJson()
    {
        JsonObject Group(IReadOnlyDictionary<string, TriggerTypeSchema> types)
        { var group = new JsonObject(); foreach (var pair in types.OrderBy(pair => pair.Key, StringComparer.Ordinal)) { group.Add(pair.Key, pair.Value.ToJson()); } return group; }
        return new() { ["formatVersion"] = 1, ["events"] = Group(Events), ["actions"] = Group(Actions), ["conditions"] = Group(Conditions), ["$defs"] = _definitions.DeepClone() };
    }
    private static IReadOnlyDictionary<string, TriggerTypeSchema> Build(string category)
    {
        JsonObject source = _definitions[category]!.AsObject(); var result = new Dictionary<string, TriggerTypeSchema>(StringComparer.Ordinal);
        foreach (JsonNode? choice in source["properties"]!["type"]!["enum"]!.AsArray())
        {
            string type = JsonData.Text(choice); var fields = new JsonObject(); var required = new HashSet<string>(StringComparer.Ordinal) { "type" };
            void Merge(JsonObject part)
            {
                if (part["required"] is JsonArray keys) { foreach (JsonNode? key in keys) { required.Add(JsonData.Text(key)); } }
                if (part["properties"] is not JsonObject properties) { return; }
                foreach (var pair in properties)
                {
                    JsonObject parameter = pair.Value!.AsObject();
                    if (parameter["x-appliesTo"] is JsonArray applies && !applies.Any(value => JsonData.Text(value) == type)) { continue; }
                    var field = parameter.DeepClone().AsObject();
                    if (field["x-refKindByType"] is JsonObject refKinds) { field["x-refKind"] = refKinds[type]?.DeepClone(); field.Remove("x-refKindByType"); }
                    if (field["x-targetKindsByType"] is JsonObject targetKinds) { field["x-targetKinds"] = targetKinds[type]?.DeepClone(); field.Remove("x-targetKindsByType"); }
                    if (JsonData.Boolean(field["x-required"])) { required.Add(pair.Key); }
                    field.Remove("x-appliesTo"); field.Remove("x-required"); fields[pair.Key] = field;
                }
            }
            Merge(source);
            if (source["allOf"] is JsonArray clauses)
            {
                foreach (JsonNode? clause in clauses)
                {
                    JsonNode? filter = clause?["if"]?["properties"]?["type"];
                    if (JsonData.Text(filter?["const"]) == type || filter?["enum"] is JsonArray values && values.Any(value => JsonData.Text(value) == type)) { Merge(clause!["then"]!.AsObject()); }
                }
            }
            fields["type"] = new JsonObject { ["type"] = "string", ["const"] = type };
            var schema = new JsonObject { ["type"] = "object", ["properties"] = fields, ["required"] = new JsonArray(required.OrderBy(value => value, StringComparer.Ordinal).Select(value => (JsonNode?)JsonValue.Create(value)).ToArray()) };
            result.Add(type, new(type, schema, _definitions));
        }
        return new ReadOnlyDictionary<string, TriggerTypeSchema>(result);
    }
}
