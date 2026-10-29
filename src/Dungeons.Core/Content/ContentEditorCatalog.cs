using System.Text.Json.Nodes;

namespace Dungeons.Core.Content;

/// <summary>Portable editor palette/property-schema dump built entirely from resolved registry definitions.</summary>
public static class ContentEditorCatalog
{
    /// <summary>Returns an independent, ordinal-id-sorted tile/entity catalogue with inspector schemas.
    /// Missing optional icon/character hints remain visible as empty/null; no art paths are invented.</summary>
    public static JsonObject ToJson(ContentRegistry content)
    {
        if (content is null) { throw new ArgumentNullException(nameof(content)); }
        if (!content.IsValid) { throw new ArgumentException("An editor catalogue requires valid content.", nameof(content)); }
        static JsonObject Describe(string id, DefinitionData data)
        {
            var properties = new JsonObject();
            foreach (var pair in data.Properties.Definitions.OrderBy(pair => pair.Key, StringComparer.Ordinal)) { properties.Add(pair.Key, pair.Value.ToJson()); }
            return new() { ["id"] = id, ["displayName"] = data.DisplayName, ["category"] = data.Category, ["icon"] = data.EditorIcon, ["char"] = data.EditorChar?.ToString(), ["properties"] = properties };
        }
        var tiles = new JsonArray(); foreach (var pair in content.Tiles.OrderBy(pair => pair.Key, StringComparer.Ordinal)) { tiles.Add((JsonNode)Describe(pair.Key, pair.Value.Data)); }
        var entities = new JsonArray(); foreach (var pair in content.Entities.OrderBy(pair => pair.Key, StringComparer.Ordinal)) { JsonObject entry = Describe(pair.Key, pair.Value.Data); entry["kind"] = pair.Value.Kind.ToString(); entities.Add((JsonNode)entry); }
        return new() { ["formatVersion"] = 1, ["contentHash"] = content.Hash, ["tiles"] = tiles, ["entities"] = entities };
    }
}
