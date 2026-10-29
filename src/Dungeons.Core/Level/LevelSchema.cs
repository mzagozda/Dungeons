using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Dungeons.Core.Util;

namespace Dungeons.Core.Level;

/// <summary>Embedded v1 schema. Evaluates the draft-2020-12 vocabulary actually used by this schema.</summary>
public static class LevelSchema
{
    private static readonly string _json = ReadSchema();
    private static readonly JsonNode _schema = JsonData.Parse(_json);
    /// <summary>Returns the canonical schema for external authoring tools.</summary>
    public static string Json => _json;
    /// <summary>Checks types, constraints and local refs; unknown keys become §10.8 warnings.</summary>
    public static IReadOnlyList<Diagnostic> Validate(JsonNode? document)
    {
        var diagnostics = new List<Diagnostic>(); Check(document, _schema, "", diagnostics); return diagnostics.AsReadOnly();
    }
    private static string ReadSchema()
    {
        using Stream stream = typeof(LevelSchema).Assembly.GetManifestResourceStream("Dungeons.Core.Level.Schema.level.v1.schema.json")!;
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }
    private static void Check(JsonNode? value, JsonNode schema, string path, List<Diagnostic> diagnostics)
    {
        if (diagnostics.Count >= 256) { return; }
        if (schema["$ref"] is JsonValue reference)
        {
            string id = reference.GetValue<string>();
            Check(value, _schema["$defs"]![id.Substring("#/$defs/".Length)]!, path, diagnostics); return;
        }
        if (schema["type"] is JsonNode type)
        {
            bool matches = type is JsonArray types ? types.Any(t => IsType(value, JsonData.Text(t))) : IsType(value, JsonData.Text(type));
            if (!matches) { Error(path, "Value has the wrong JSON type.", diagnostics); return; }
        }
        if (schema.AsObject().ContainsKey("const") && !JsonNode.DeepEquals(value, schema["const"])) { Error(path, "Value does not match the required constant.", diagnostics); }
        if (schema["enum"] is JsonArray values && !values.Any(v => JsonNode.DeepEquals(value, v))) { Error(path, "Value is not one of the permitted choices.", diagnostics); }
        if (schema["oneOf"] is JsonArray alternatives)
        {
            int valid = 0;
            foreach (JsonNode? alternative in alternatives)
            {
                var trial = new List<Diagnostic>(); Check(value, alternative!, path, trial);
                if (!trial.Any(d => d.Severity == DiagnosticSeverity.Error)) { valid++; }
            }
            if (valid != 1) { Error(path, "Value must match exactly one schema alternative.", diagnostics); }
        }
        if (schema["allOf"] is JsonArray clauses) { foreach (JsonNode? clause in clauses) { Check(value, clause!, path, diagnostics); } }
        if (schema["if"] is JsonNode condition)
        {
            var trial = new List<Diagnostic>(); Check(value, condition, path, trial);
            if (!trial.Any(d => d.Severity == DiagnosticSeverity.Error) && schema["then"] is JsonNode then) { Check(value, then, path, diagnostics); }
        }
        if (value is JsonObject obj) { CheckObject(obj, schema, path, diagnostics); }
        else if (value is JsonArray array)
        {
            Limit(array.Count, schema, "minItems", "maxItems", path, diagnostics);
            if (schema["items"] is JsonNode items)
            { for (int i = 0; i < array.Count; i++) { Check(array[i], items, path + "/" + JsonData.Index(i), diagnostics); } }
        }
        else if (value is JsonValue scalar)
        {
            if (scalar.GetValueKind() == JsonValueKind.String)
            {
                string text = scalar.GetValue<string>(); Limit(text.Length, schema, "minLength", "maxLength", path, diagnostics);
                if (schema["pattern"] is JsonNode pattern && !Regex.IsMatch(text, pattern.GetValue<string>(), RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
                { Error(path, "String does not match the required pattern.", diagnostics, path.StartsWith("/layers/heights/", StringComparison.Ordinal) ? DiagnosticCode.LVL1005 : DiagnosticCode.LVL0003); }
            }
            else if (scalar.GetValueKind() == JsonValueKind.Number)
            {
                double number = JsonData.Number(scalar, double.NaN);
                if (double.IsNaN(number) || double.IsInfinity(number) ||
                    (schema["minimum"] is JsonNode min && number < JsonData.Number(min)) ||
                    (schema["maximum"] is JsonNode max && number > JsonData.Number(max)) ||
                    (schema["exclusiveMinimum"] is JsonNode exclusive && number <= JsonData.Number(exclusive)))
                { Error(path, "Number is outside the permitted finite range.", diagnostics); }
            }
        }
    }
    private static void CheckObject(JsonObject value, JsonNode schema, string path, List<Diagnostic> diagnostics)
    {
        Limit(value.Count, schema, "minProperties", "maxProperties", path, diagnostics);
        if (schema["required"] is JsonArray required)
        { foreach (JsonNode? key in required) { if (!value.ContainsKey(key!.GetValue<string>())) { Error(path + "/" + JsonData.Pointer(key.GetValue<string>()), "Required property is missing.", diagnostics); } } }
        JsonObject? properties = schema["properties"] as JsonObject;
        HashSet<string>? known = properties is not null && JsonData.Text(schema["type"]) == "object" ? KnownProperties(schema) : null;
        foreach (KeyValuePair<string, JsonNode?> property in value)
        {
            string next = path + "/" + JsonData.Pointer(property.Key);
            if (schema["propertyNames"] is JsonNode names) { Check(JsonValue.Create(property.Key), names, next, diagnostics); }
            if (properties?[property.Key] is JsonNode child) { Check(property.Value, child, next, diagnostics); }
            else if (schema["additionalProperties"] is JsonObject additional) { Check(property.Value, additional, next, diagnostics); }
            else if (known is not null && !known.Contains(property.Key))
            { if (diagnostics.Count < 256) { diagnostics.Add(new(DiagnosticSeverity.Warning, DiagnosticCode.LVL0100, "Unknown property is preserved for forward compatibility.", next)); } }
        }
    }
    private static HashSet<string> KnownProperties(JsonNode schema)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        void Add(JsonNode node)
        {
            if (node["properties"] is JsonObject fields) { foreach (string key in fields.Select(p => p.Key)) { keys.Add(key); } }
            if (node["allOf"] is JsonArray clauses) { foreach (JsonNode? clause in clauses) { Add(clause!); } }
            if (node["then"] is JsonNode then) { Add(then); }
        }
        Add(schema); return keys;
    }
    private static bool IsType(JsonNode? value, string type) => type switch
    {
        "null" => value is null,
        "object" => value is JsonObject,
        "array" => value is JsonArray,
        "string" => value is JsonValue text && text.GetValueKind() == JsonValueKind.String,
        "boolean" => value is JsonValue boolean && boolean.GetValueKind() is JsonValueKind.True or JsonValueKind.False,
        "number" => value is JsonValue number && number.GetValueKind() == JsonValueKind.Number,
        "integer" => value is JsonValue integer && integer.GetValueKind() == JsonValueKind.Number && JsonData.Number(integer, double.NaN) == System.Math.Truncate(JsonData.Number(integer, double.NaN)),
        _ => false,
    };
    private static void Limit(int value, JsonNode schema, string minimum, string maximum, string path, List<Diagnostic> diagnostics)
    {
        if (schema[minimum] is JsonNode min && value < JsonData.Number(min) || schema[maximum] is JsonNode max && value > JsonData.Number(max))
        { Error(path, "Collection/string size is outside the permitted range.", diagnostics, path.StartsWith("/layers/", StringComparison.Ordinal) ? DiagnosticCode.LVL1001 : DiagnosticCode.LVL0003); }
    }
    private static void Error(string path, string message, List<Diagnostic> diagnostics, DiagnosticCode code = DiagnosticCode.LVL0003)
    { if (diagnostics.Count < 256) { diagnostics.Add(new(DiagnosticSeverity.Error, code, message, path)); } }
}
