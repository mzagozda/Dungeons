using System.Text.Json.Nodes;
using Dungeons.Core.Util;

namespace Dungeons.Core.Level;

// Raw authored values only: content-dependent normalization still runs in the
// validator. Public command adoption and legacy paint invalidate this index.
internal sealed class CellOverrideIndex
{
    private readonly JsonArray? _source;
    private readonly int _count, _width, _height;
    internal readonly Dictionary<int, (JsonObject Props, int Index)> Entries = [];
    internal readonly List<Diagnostic> Diagnostics = [];
    private CellOverrideIndex(JsonArray? source, int width, int height)
    {
        _source = source; _count = source?.Count ?? 0; _width = width; _height = height;
        if (source is null) { return; }
        for (int i = 0; i < source.Count; i++)
        {
            JsonObject? item = source[i] as JsonObject; double rawX = JsonData.Number(item?["x"], -1), rawZ = JsonData.Number(item?["z"], -1);
            bool inside = rawX >= 0 && rawX < width && rawX == System.Math.Truncate(rawX) && rawZ >= 0 && rawZ < height && rawZ == System.Math.Truncate(rawZ);
            if (!inside || item?["props"] is not JsonObject props || !Entries.TryAdd((int)rawZ * width + (int)rawX, (props, i)))
            { Diagnostics.Add(new(DiagnosticSeverity.Error, DiagnosticCode.LVL0003, "Cell override is out of bounds or duplicated.", "/cells/" + JsonData.Index(i))); }
        }
    }
    internal static CellOverrideIndex Get(LevelDocument document, JsonObject root, int width, int height)
    {
        JsonArray? source = root["cells"] as JsonArray; CellOverrideIndex? cached = document.OverrideIndex;
        if (cached is not null && ReferenceEquals(cached._source, source) && cached._count == (source?.Count ?? 0) && cached._width == width && cached._height == height) { return cached; }
        var index = new CellOverrideIndex(source, width, height);
        if (ReferenceEquals(document.Source, root)) { document.OverrideIndex = index; }
        return index;
    }
}
