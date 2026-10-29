using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Dungeons.Core.Util;

internal static class JsonData
{
    internal const int MaxDepth = 32;
    internal const int MaxBytes = 64 * 1024 * 1024;
    internal static JsonNode Parse(string json)
        => Parse(json, MaxDepth);
    internal static JsonNode Parse(string json, int maxDepth, int maxBytes = MaxBytes)
    {
        if (maxDepth is < 1 or > 64) { throw new ArgumentOutOfRangeException(nameof(maxDepth)); }
        if (maxBytes is < 1 or > MaxBytes * 4) { throw new ArgumentOutOfRangeException(nameof(maxBytes)); }
        // The reader catches duplicates before the DOM can discard their authoring order/value.
        byte[] bytes = new UTF8Encoding(false, true).GetBytes(json);
        if (bytes.Length > maxBytes) { throw new JsonException("JSON exceeds its byte limit."); }
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = maxDepth });
        var keys = new Stack<HashSet<string>>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject) { keys.Push(new(StringComparer.Ordinal)); }
            else if (reader.TokenType == JsonTokenType.EndObject) { keys.Pop(); }
            else if (reader.TokenType == JsonTokenType.PropertyName && !keys.Peek().Add(reader.GetString()!)) { throw new JsonException("Duplicate JSON property."); }
        }
        return JsonNode.Parse(bytes, documentOptions: new JsonDocumentOptions { MaxDepth = maxDepth }) ?? throw new JsonException("JSON root cannot be null.");
    }
    internal static string Text(JsonNode? node, string fallback = "") => node is JsonValue v && v.TryGetValue<string>(out string? text) ? text : fallback;
    internal static bool Boolean(JsonNode? node, bool fallback = false) => node is JsonValue v && v.TryGetValue<bool>(out bool value) ? value : fallback;
    internal static double Number(JsonNode? node, double fallback = 0)
    {
        if (node is not JsonValue value) { return fallback; }
        if (value.TryGetValue<double>(out double number)) { return number; }
        if (value.TryGetValue<int>(out int integer)) { return integer; }
        if (value.TryGetValue<long>(out long large)) { return large; }
        if (value.TryGetValue<float>(out float single)) { return single; }
        return fallback;
    }
    internal static int Integer(JsonNode? node, int fallback = 0)
    {
        double number = Number(node, fallback);
        return number >= int.MinValue && number <= int.MaxValue && number == System.Math.Truncate(number) ? (int)number : fallback;
    }
    internal static string Pointer(string name) => name.Replace("~", "~0").Replace("/", "~1");
    internal static string Index(int index) => index.ToString(CultureInfo.InvariantCulture);
    internal static string Localized(JsonNode? node, string language = "en") => node is JsonObject localized ? Text(localized[language], Text(localized["en"])) : Text(node);
    internal static string Stringify(JsonNode node, bool indented = false, bool canonical = false)
    {
        // Explicit primitive writing also works under NativeAOT without reflection metadata.
        using var stream = new MemoryStream(); using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = indented })) { Write(node, writer, canonical); }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
    private static void Write(JsonNode? node, Utf8JsonWriter writer, bool canonical)
    {
        if (node is null) { writer.WriteNullValue(); return; }
        if (node is JsonObject obj)
        { writer.WriteStartObject(); foreach (KeyValuePair<string, JsonNode?> pair in canonical ? obj.OrderBy(pair => pair.Key, StringComparer.Ordinal) : (IEnumerable<KeyValuePair<string, JsonNode?>>)obj) { writer.WritePropertyName(pair.Key); Write(pair.Value, writer, canonical); } writer.WriteEndObject(); return; }
        if (node is JsonArray array) { writer.WriteStartArray(); foreach (JsonNode? item in array) { Write(item, writer, canonical); } writer.WriteEndArray(); return; }
        var value = (JsonValue)node;
        if (value.TryGetValue<JsonElement>(out JsonElement element)) { element.WriteTo(writer); }
        else if (value.TryGetValue<string>(out string? text)) { writer.WriteStringValue(text); }
        else if (value.TryGetValue<bool>(out bool boolean)) { writer.WriteBooleanValue(boolean); }
        else if (value.TryGetValue<int>(out int integer)) { writer.WriteNumberValue(integer); }
        else if (value.TryGetValue<long>(out long large)) { writer.WriteNumberValue(large); }
        else if (value.TryGetValue<ulong>(out ulong unsigned)) { writer.WriteNumberValue(unsigned); }
        else if (value.TryGetValue<float>(out float single)) { writer.WriteNumberValue(single); }
        else if (value.TryGetValue<double>(out double number)) { writer.WriteNumberValue(number); }
        else { throw new ArgumentException("Unsupported JSON primitive."); }
    }
}
