using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Dungeons.Core.Level.Commands;

/// <summary>Private reversible JSON changes. Arrays use a compact middle splice when their length changes.</summary>
internal static class JsonDelta
{
    internal static JsonArray Between(JsonObject before, JsonObject after)
    { var changes = new JsonArray(); Diff(before, after, [], changes); return changes; }
    internal static bool Equivalent(JsonNode? left, JsonNode? right)
    {
        if (left is JsonObject a && right is JsonObject b)
        { return a.Count == b.Count && a.All(pair => b.TryGetPropertyValue(pair.Key, out JsonNode? value) && Equivalent(pair.Value, value)); }
        if (left is JsonArray x && right is JsonArray y)
        { return x.Count == y.Count && x.Select((item, index) => Equivalent(item, y[index])).All(equal => equal); }
        if (left is JsonValue l && right is JsonValue r && l.GetValueKind() == JsonValueKind.Number && r.GetValueKind() == JsonValueKind.Number)
        { return LevelSerializer.NormalizeNumber(l.ToJsonString()) == LevelSerializer.NormalizeNumber(r.ToJsonString()); }
        return JsonNode.DeepEquals(left, right);
    }
    private static void Diff(JsonNode? before, JsonNode? after, string[] path, JsonArray changes)
    {
        if (Equivalent(before, after)) { return; }
        if (before is JsonObject a && after is JsonObject b)
        {
            foreach (string key in a.Select(pair => pair.Key).Union(b.Select(pair => pair.Key), StringComparer.Ordinal).OrderBy(key => key, StringComparer.Ordinal))
            {
                bool had = a.TryGetPropertyValue(key, out JsonNode? old), has = b.TryGetPropertyValue(key, out JsonNode? value); string[] next = [.. path, key];
                if (had && has) { Diff(old, value, next, changes); }
                else { changes.Add((JsonNode)Value(next, had, old, has, value)); }
            }
            return;
        }
        if (before is JsonArray x && after is JsonArray y)
        {
            if (x.Count == y.Count)
            { for (int i = 0; i < x.Count; i++) { Diff(x[i], y[i], [.. path, i.ToString(CultureInfo.InvariantCulture)], changes); } return; }
            int prefix = 0, suffix = 0, shared = System.Math.Min(x.Count, y.Count);
            while (prefix < shared && Equivalent(x[prefix], y[prefix])) { prefix++; }
            while (suffix < shared - prefix && Equivalent(x[x.Count - 1 - suffix], y[y.Count - 1 - suffix])) { suffix++; }
            changes.Add((JsonNode)new JsonObject { ["kind"] = "splice", ["path"] = Path(path), ["index"] = prefix,
                ["before"] = new JsonArray(x.Skip(prefix).Take(x.Count - prefix - suffix).Select(item => item?.DeepClone()).ToArray()),
                ["after"] = new JsonArray(y.Skip(prefix).Take(y.Count - prefix - suffix).Select(item => item?.DeepClone()).ToArray()) }); return;
        }
        changes.Add((JsonNode)Value(path, true, before, true, after));
    }
    private static JsonObject Value(string[] path, bool had, JsonNode? before, bool has, JsonNode? after) => new()
    { ["kind"] = "value", ["path"] = Path(path), ["beforeExists"] = had, ["before"] = before?.DeepClone(), ["afterExists"] = has, ["after"] = after?.DeepClone() };
    private static JsonArray Path(string[] path) => new(path.Select(segment => (JsonNode?)JsonValue.Create(segment)).ToArray());
    internal static void Validate(JsonArray changes)
    {
        foreach (JsonNode? item in changes)
        {
            if (item is not JsonObject change || change["path"] is not JsonArray path || path.Count is < 1 or > 64 || path.Any(segment => segment is not JsonValue value || !value.TryGetValue<string>(out _)))
            { throw new JsonException("Invalid command change path."); }
            if (!change.ContainsKey("before") || !change.ContainsKey("after")) { throw new JsonException("A change needs both before and after values."); }
            string kind = Util.JsonData.Text(change["kind"]);
            if (kind == "value")
            {
                Keys(change, "kind", "path", "beforeExists", "before", "afterExists", "after");
                if (change["beforeExists"] is not JsonValue had || !had.TryGetValue<bool>(out _) || change["afterExists"] is not JsonValue has || !has.TryGetValue<bool>(out _)) { throw new JsonException("Invalid change existence flags."); }
            }
            else if (kind == "splice")
            {
                Keys(change, "kind", "path", "index", "before", "after");
                if (change["before"] is not JsonArray || change["after"] is not JsonArray || change["index"] is not JsonValue index || !index.TryGetValue<int>(out int at) || at < 0) { throw new JsonException("Invalid array splice."); }
            }
            else { throw new JsonException("Unknown command change kind."); }
        }
    }
    internal static void Keys(JsonObject obj, params string[] keys)
    { if (obj.Any(pair => !keys.Contains(pair.Key, StringComparer.Ordinal))) { throw new JsonException("Unknown command field."); } }
    internal static void Apply(JsonObject root, JsonArray changes, bool forward)
    {
        for (int step = 0; step < changes.Count; step++)
        {
            JsonObject change = changes[forward ? step : changes.Count - 1 - step]!.AsObject(); JsonArray path = change["path"]!.AsArray();
            string before = forward ? "before" : "after", after = forward ? "after" : "before";
            if (Util.JsonData.Text(change["kind"]) == "splice")
            {
                JsonArray array = Locate(root, path, path.Count).AsArray(), expected = change[before]!.AsArray(), replacement = change[after]!.AsArray(); int at = change["index"]!.GetValue<int>();
                if (at > array.Count - expected.Count || expected.Where((value, index) => !Equivalent(value, array[at + index])).Any()) { throw new LevelEditException("The array no longer matches this command's history."); }
                for (int i = 0; i < expected.Count; i++) { array.RemoveAt(at); }
                for (int i = 0; i < replacement.Count; i++) { array.Insert(at + i, replacement[i]?.DeepClone()); }
                continue;
            }
            JsonNode parent = Locate(root, path, path.Count - 1); string key = path[^1]!.GetValue<string>(); bool expectedExists = change[before + "Exists"]!.GetValue<bool>(), exists = change[after + "Exists"]!.GetValue<bool>();
            if (parent is JsonObject obj)
            {
                bool found = obj.TryGetPropertyValue(key, out JsonNode? value);
                if (found != expectedExists || found && !Equivalent(value, change[before])) { throw new LevelEditException("The property no longer matches this command's history."); }
                if (exists) { obj[key] = change[after]?.DeepClone(); } else { obj.Remove(key); }
            }
            else if (parent is JsonArray array)
            {
                int index = Index(key, array.Count);
                if (!expectedExists || !exists || !Equivalent(array[index], change[before])) { throw new LevelEditException("The array value no longer matches this command's history."); }
                array[index] = change[after]?.DeepClone();
            }
            else { throw new LevelEditException("A command path needs an object or array parent."); }
        }
    }
    private static JsonNode Locate(JsonObject root, JsonArray path, int length)
    {
        JsonNode node = root;
        for (int i = 0; i < length; i++)
        {
            string key = path[i]!.GetValue<string>();
            node = node is JsonObject obj ? obj[key] ?? throw new LevelEditException("A command path is missing.") : node is JsonArray array ? array[Index(key, array.Count)] ?? throw new LevelEditException("A command path is null.") : throw new LevelEditException("A command path is not traversable.");
        }
        return node;
    }
    private static int Index(string text, int count)
    { if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int index) || index >= count || text != index.ToString(CultureInfo.InvariantCulture)) { throw new LevelEditException("Invalid command array index."); } return index; }
}
