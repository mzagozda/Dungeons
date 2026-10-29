using System.Text.Json.Nodes;
using Dungeons.Core.Content;
using Dungeons.Core.Level;
using Dungeons.Core.Math;
using Dungeons.Core.Util;

namespace Dungeons.Core.Sim;

public sealed partial class World
{
    private readonly string[] _surfaceIds;
    internal string SurfaceAt(IntVec2 cell) => Grid.Contains(cell.X, cell.Z) ? _surfaceIds[cell.Z * Grid.Width + cell.X] : "";
    private static string[] BakeSurfaceIds(LevelDocument level, ContentRegistry? content)
    {
        var result = new string[level.Width * level.Height]; Array.Fill(result, "");
        // Code scenarios may attach a mission-only source over their explicitly baked grid.
        if (level.Source is not JsonObject source || content is null || source["palette"] is not JsonObject authoredPalette || source["layers"]?["floor"] is not JsonArray rows) { return result; }
        var palette = new Dictionary<char, string>();
        foreach (KeyValuePair<string, JsonNode?> entry in authoredPalette)
        {
            string id = entry.Value is JsonObject mapping ? JsonData.Text(mapping["tile"]) : JsonData.Text(entry.Value);
            TileDef tile = content.Tile(id); palette.Add(entry.Key[0], tile.BaseFloor ?? tile.Id);
        }
        for (int z = 0; z < level.Height; z++)
        {
            string row = JsonData.Text(rows[z]);
            for (int x = 0; x < level.Width; x++) { result[z * level.Width + x] = palette[row[x]]; }
        }
        return result;
    }
}
