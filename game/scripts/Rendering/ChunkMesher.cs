using System.Text.Json;
using System.Text.Json.Nodes;
using Dungeons.Core.Content;
using Dungeons.Core.Level;
using Dungeons.Core.Math;
using Dungeons.Core.Sim;
using Dungeons.Core.Util;

namespace Dungeons.Game.Rendering;

/// <summary>Engine-free mesh vertex; arrays may be built on a worker and committed on the main thread.</summary>
public readonly record struct TerrainVertex(Vec3 Position, Vec3 Normal, float U, float V, float R, float G, float B, float A = 1);
/// <summary>One material's indexed geometry.</summary>
public sealed record MeshBuffer(TerrainVertex[] Vertices, int[] Indices);
/// <summary>One 16x16 chunk's CPU-only geometry.</summary>
public sealed record ChunkGeometry(int X, int Z, MeshBuffer Terrain, MeshBuffer Water, MeshBuffer Foliage);
/// <summary>Normalized atlas rectangle.</summary>
public readonly record struct AtlasRect(float X, float Y, float Width, float Height);
/// <summary>Baked rendering cell, independent of Godot resources.</summary>
public readonly record struct TerrainCell(byte Height, bool Void, bool Water, bool Thin, bool Solid, StairsDirection Stairs,
    AtlasRect Top, AtlasRect Side, AtlasRect? Overlay, float R, float G, float B, string TileId, string? Model, float Rotation = 0);

/// <summary>Immutable render input. Reads authoring data once; worker meshing touches only arrays.</summary>
public sealed class TerrainSnapshot
{
    private readonly TerrainCell[][] _chunks;
    private readonly int _columns;
    private readonly ContentRegistry _content;
    private readonly Dictionary<string, AtlasRect> _atlas;
    private readonly Dictionary<string, Style> _styles;
    private sealed record Style(bool Void, bool Water, bool Thin, AtlasRect[] Tops, AtlasRect Side, AtlasRect? Overlay,
        float R, float G, float B, string? Model, int ModelVariants, bool RandomRotation);
    /// <summary>Copies the document/registry/atlas into immutable per-cell render inputs.</summary>
    public TerrainSnapshot(LevelDocument document, ContentRegistry content, string atlasJson)
    {
        if (document is null) { throw new ArgumentNullException(nameof(document)); }
        if (content is null || !content.IsValid) { throw new ArgumentException("A valid registry is required.", nameof(content)); }
        if (document.HasErrors) { throw new ArgumentException("Cannot render a document with validation errors.", nameof(document)); }
        if (document.ContentHash.Length > 0 && document.ContentHash != content.Hash) { throw new ArgumentException("Document and registry identities differ.", nameof(content)); }
        Width = document.Width; Height = document.Height; LevelId = document.Id; _content = content; _styles = new(StringComparer.Ordinal);
        _columns = (Width + ChunkMesher.ChunkSize - 1) / ChunkMesher.ChunkSize;
        _chunks = new TerrainCell[_columns * ((Height + ChunkMesher.ChunkSize - 1) / ChunkMesher.ChunkSize)][];
        for (int i = 0; i < _chunks.Length; i++) { _chunks[i] = new TerrainCell[ChunkMesher.ChunkSize * ChunkMesher.ChunkSize]; }
        _atlas = new(StringComparer.Ordinal);
        foreach (var pair in JsonNode.Parse(atlasJson)!["tiles"]!.AsObject())
        {
            JsonArray rect = pair.Value!.AsArray();
            // Half-texel centres plus the two-pixel bleed prevent filtering across adjacent tiles.
            _atlas.Add(pair.Key, new((rect[0]!.GetValue<int>() + .5f) / 1024, (rect[1]!.GetValue<int>() + .5f) / 1024, 15f / 1024, 15f / 1024));
        }
        Riverbed = Rect("atlas:sand_dark"); WaterTexture = Rect("atlas:water");
        for (int z = 0; z < Height; z++)
        {
            for (int x = 0; x < Width; x++) { WriteCell(document, x, z); }
        }
    }
    private TerrainSnapshot(TerrainSnapshot previous, LevelDocument document, CellRect area)
    {
        Width = previous.Width; Height = previous.Height; LevelId = previous.LevelId; _columns = previous._columns;
        _content = previous._content; _atlas = previous._atlas; _styles = new(previous._styles, StringComparer.Ordinal);
        Riverbed = previous.Riverbed; WaterTexture = previous.WaterTexture;
        _chunks = (TerrainCell[][])previous._chunks.Clone();
        int right = area.X + area.Width, bottom = area.Z + area.Height;
        for (int z = area.Z / 16; z <= (bottom - 1) / 16; z++)
        { for (int x = area.X / 16; x <= (right - 1) / 16; x++) { int index = z * _columns + x; _chunks[index] = (TerrainCell[])_chunks[index].Clone(); } }
        for (int z = area.Z; z < bottom; z++) { for (int x = area.X; x < right; x++) { WriteCell(document, x, z); } }
    }
    /// <summary>Copies changed cells and their containing chunks, sharing all other immutable storage.
    /// Mesh workers may keep reading older snapshots while a main-thread authoring edit produces this one.</summary>
    public TerrainSnapshot WithChanges(LevelDocument document, CellRect cells)
    {
        if (document is null) { throw new ArgumentNullException(nameof(document)); }
        if (document.HasErrors || document.Width != Width || document.Height != Height || document.Id != LevelId || document.ContentHash != _content.Hash)
        { throw new ArgumentException("Rebuild the snapshot after invalid, size, identity or registry changes.", nameof(document)); }
        int x = (int)System.Math.Clamp((long)cells.X, 0, Width), z = (int)System.Math.Clamp((long)cells.Z, 0, Height);
        int right = (int)System.Math.Clamp((long)cells.X + cells.Width, 0, Width), bottom = (int)System.Math.Clamp((long)cells.Z + cells.Height, 0, Height);
        return cells.IsEmpty || right <= x || bottom <= z ? this : new(this, document, new(x, z, right - x, bottom - z));
    }
    private void WriteCell(LevelDocument document, int x, int z)
    {
        var reference = document.GetTileReference(x, z); Style style = GetStyle(reference.Tile); uint hash = CellHash(x, z, LevelId); Cell cell = document.GetCell(x, z);
        AtlasRect? overlay = reference.Overlay is string overlayId ? GetStyle(overlayId).Overlay ?? throw new InvalidDataException("Overlay has no texture: " + overlayId) : null;
        string? model = style.Model;
        if (model is not null && hash % (uint)style.ModelVariants is uint variant && variant != 0) { model = model[..^4] + "_" + variant + ".glb"; }
        float rotation = style.RandomRotation ? ((hash >> 8) % 4) * MathF.PI / 2 : 0;
        _chunks[z / 16 * _columns + x / 16][z % 16 * 16 + x % 16] = new(cell.Height, style.Void, style.Water, style.Thin, cell.Solid, cell.StairsDir,
            style.Tops[hash % (uint)style.Tops.Length], style.Side, overlay, style.R, style.G, style.B, reference.Tile, model, rotation);
    }
    private Style GetStyle(string id)
    {
        if (_styles.TryGetValue(id, out Style? style)) { return style; }
        TileDef tile = _content.Tile(id), ground = tile.BaseFloor is string floor ? _content.Tile(floor) : tile;
        JsonElement render = ground.Data.Json.GetProperty("render"), original = tile.Data.Json.GetProperty("render");
        string kind = render.GetProperty("kind").GetString()!; bool water = kind == "water", none = kind == "none";
        string top = render.TryGetProperty("top", out JsonElement topId) ? topId.GetString()! : water && render.TryGetProperty("bed", out JsonElement bed) ? bed.GetString()! : water ? "atlas:sand_dark" : "atlas:stone_top";
        string side = render.TryGetProperty("side", out JsonElement sideId) ? sideId.GetString()! : top;
        var tops = new AtlasRect[render.TryGetProperty("topVariants", out JsonElement count) ? System.Math.Max(1, count.GetInt32()) : 1];
        for (int i = 0; i < tops.Length; i++) { tops[i] = none ? default : Rect(top, (uint)i); }
        (float r, float g, float b) = Tint(render.TryGetProperty("tint", out JsonElement tint) ? tint.GetString()! : "#ffffff");
        style = new(none, water, render.TryGetProperty("thickness", out _), tops, none ? default : Rect(side), render.TryGetProperty("texture", out JsonElement texture) ? Rect(texture.GetString()!) : null,
            r, g, b, original.TryGetProperty("model", out JsonElement model) ? model.GetString() : null, original.TryGetProperty("variants", out JsonElement models) ? models.GetInt32() : 1,
            original.TryGetProperty("randomRotation", out JsonElement rotation) && rotation.GetBoolean());
        if (style.ModelVariants < 1) { throw new InvalidDataException("Model variants must be positive."); }
        _styles.Add(id, style); return style;
    }
    private AtlasRect Rect(string id, uint variant = 0)
    {
        string key = id.StartsWith("atlas:", StringComparison.Ordinal) ? id[6..] : id;
        return _atlas.TryGetValue(key, out AtlasRect rect) || _atlas.TryGetValue(key + "_" + variant, out rect) ? rect : throw new InvalidDataException("Missing atlas tile: " + key);
    }
    /// <summary>Grid width.</summary>
    public int Width { get; }
    /// <summary>Grid height.</summary>
    public int Height { get; }
    /// <summary>Authored identity for stable visual variation.</summary>
    public string LevelId { get; }
    /// <summary>Bed under thin bridge slabs.</summary>
    public AtlasRect Riverbed { get; }
    /// <summary>Atlas source for the separate water material.</summary>
    public AtlasRect WaterTexture { get; }
    /// <summary>Cell view. Out of bounds is void.</summary>
    public TerrainCell this[int x, int z] => (uint)x < (uint)Width && (uint)z < (uint)Height ? _chunks[z / 16 * _columns + x / 16][z % 16 * 16 + x % 16] : new(0, true, false, false, false, StairsDirection.None, default, default, null, 1, 1, 1, "", null);
    /// <summary>Stable visual salt; never consumes simulation RNG.</summary>
    public static uint CellHash(int x, int z, string levelId) { var hash = new Fnv1a(); hash.Add(levelId); hash.Add(x); hash.Add(z); return (uint)hash.Value; }
    private static (float, float, float) Tint(string hex)
    {
        // Vertex COLOR is linear, while authored palette/tint hex values are sRGB.
        static float Linear(byte value) { float c = value / 255f; return c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f); }
        return (Linear(Convert.ToByte(hex.Substring(1, 2), 16)), Linear(Convert.ToByte(hex.Substring(3, 2), 16)), Linear(Convert.ToByte(hex.Substring(5, 2), 16)));
    }
}

/// <summary>Pure C# chunk mesher. No engine objects, IO, shared random state or runtime world mutations.</summary>
public static class ChunkMesher
{
    /// <summary>Cells along one chunk edge.</summary>
    public const int ChunkSize = 16;
    private static readonly float[] _ao = [1, 0.8f, 0.65f, 0.5f];
    /// <summary>Builds a chunk from an immutable snapshot.</summary>
    public static ChunkGeometry Build(TerrainSnapshot source, int chunkX, int chunkZ, AtlasRect waterTexture)
    {
        if (source is null) { throw new ArgumentNullException(nameof(source)); }
        if (chunkX < 0 || chunkZ < 0 || chunkX >= (source.Width + ChunkSize - 1) / ChunkSize || chunkZ >= (source.Height + ChunkSize - 1) / ChunkSize) { throw new ArgumentOutOfRangeException(nameof(chunkX)); }
        var terrain = new Builder(); var water = new Builder(); var foliage = new Builder();
        for (int z = chunkZ * ChunkSize; z < System.Math.Min(source.Height, (chunkZ + 1) * ChunkSize); z++)
        {
            for (int x = chunkX * ChunkSize; x < System.Math.Min(source.Width, (chunkX + 1) * ChunkSize); x++)
            {
                TerrainCell cell = source[x, z]; if (cell.Void) { continue; }
                float h = cell.Height;
                if (cell.Stairs != StairsDirection.None)
                {
                    for (int step = 0; step < 4; step++)
                    {
                        float low = step / 4f, high = (step + 1) / 4f;
                        float x0 = x, x1 = x + 1, z0 = z, z1 = z + 1;
                        switch (cell.Stairs)
                        {
                            case StairsDirection.East: x0 += low; x1 = x + high; break;
                            case StairsDirection.West: x0 += 1 - high; x1 = x + 1 - low; break;
                            case StairsDirection.South: z0 += low; z1 = z + high; break;
                            case StairsDirection.North: z0 += 1 - high; z1 = z + 1 - low; break;
                        }
                        Box(terrain, x0, z0, x1, z1, h - 0.01f, h + high, cell.Top, cell.Side, cell.R, cell.G, cell.B);
                    }
                }
                else
                {
                    float top = cell.Water ? h - 1 : h;
                    float[] ao = [Corner(source, x, z, -1, -1, h), Corner(source, x, z, 1, -1, h), Corner(source, x, z, 1, 1, h), Corner(source, x, z, -1, 1, h)];
                    terrain.Quad(new(x, top, z), new(x + 1, top, z), new(x + 1, top, z + 1), new(x, top, z + 1), new(0, 1, 0), cell.Top, cell.R, cell.G, cell.B, ao);
                    for (int side = 0; side < 4; side++)
                    {
                        int nx = x + (side == 1 ? 1 : side == 3 ? -1 : 0), nz = z + (side == 2 ? 1 : side == 0 ? -1 : 0);
                        TerrainCell neighbor = source[nx, nz]; float bottom = neighbor.Void ? -1 : neighbor.Water ? neighbor.Height - 1 : neighbor.Height;
                        if (cell.Thin) { bottom = MathF.Max(bottom, h - 0.1f); }
                        for (float y = top; y > bottom; y -= 1)
                        { Side(terrain, x, z, side, MathF.Max(bottom, y - 1), y, cell.Side, y - 1 <= bottom ? 0.8f : 1); }
                    }
                    if (cell.Thin)
                    {
                        terrain.Quad(new(x, h - 0.1f, z + 1), new(x + 1, h - 0.1f, z + 1), new(x + 1, h - 0.1f, z), new(x, h - 0.1f, z), new(0, -1, 0), cell.Side, 1, 1, 1);
                        terrain.Quad(new(x, h - 1, z), new(x + 1, h - 1, z), new(x + 1, h - 1, z + 1), new(x, h - 1, z + 1), new(0, 1, 0), source.Riverbed, 1, 1, 1);
                    }
                }
                if (cell.Water || cell.Thin)
                {
                    float y = h - 0.15f;
                    float[] foam = [Foam(source, x, z, -1, -1), Foam(source, x, z, 1, -1), Foam(source, x, z, 1, 1), Foam(source, x, z, -1, 1)];
                    water.Quad(new(x, y, z), new(x + 1, y, z), new(x + 1, y, z + 1), new(x, y, z + 1), new(0, 1, 0), waterTexture, 1, 1, 1, foam);
                }
                if (cell.Overlay is AtlasRect texture)
                {
                    const float height = 0.65f;
                    texture = new(texture.X, texture.Y + texture.Height, texture.Width, -texture.Height);
                    foliage.Quad(new(x + 0.15f, h, z + 0.15f), new(x + 0.85f, h, z + 0.85f), new(x + 0.85f, h + height, z + 0.85f), new(x + 0.15f, h + height, z + 0.15f), new(0.7071f, 0, -0.7071f), texture, 1, 1, 1, alpha: [0, 0, 1, 1]);
                    foliage.Quad(new(x + 0.85f, h, z + 0.15f), new(x + 0.15f, h, z + 0.85f), new(x + 0.15f, h + height, z + 0.85f), new(x + 0.85f, h + height, z + 0.15f), new(0.7071f, 0, 0.7071f), texture, 1, 1, 1, alpha: [0, 0, 1, 1]);
                }
            }
        }
        return new(chunkX, chunkZ, terrain.Finish(), water.Finish(), foliage.Finish());
    }
    private static float Corner(TerrainSnapshot s, int x, int z, int dx, int dz, float height)
    {
        bool Higher(int cx, int cz) { TerrainCell c = s[cx, cz]; return !c.Void && c.Height + (c.Solid ? 1 : 0) > height; }
        return _ao[(Higher(x + dx, z) ? 1 : 0) + (Higher(x, z + dz) ? 1 : 0) + (Higher(x + dx, z + dz) ? 1 : 0)];
    }
    private static float Foam(TerrainSnapshot s, int x, int z, int dx, int dz)
    { bool Land(TerrainCell c) => !c.Void && !c.Water && !c.Thin; return Land(s[x + dx, z]) || Land(s[x, z + dz]) ? 1 : 0; }
    private static void Box(Builder target, float x0, float z0, float x1, float z1, float bottom, float top, AtlasRect upper, AtlasRect side, float r, float g, float b)
    {
        target.Quad(new(x0, top, z0), new(x1, top, z0), new(x1, top, z1), new(x0, top, z1), new(0, 1, 0), upper, r, g, b);
        target.Quad(new(x0, bottom, z0), new(x1, bottom, z0), new(x1, top, z0), new(x0, top, z0), new(0, 0, -1), side, 1, 1, 1);
        target.Quad(new(x1, bottom, z0), new(x1, bottom, z1), new(x1, top, z1), new(x1, top, z0), new(1, 0, 0), side, 1, 1, 1);
        target.Quad(new(x1, bottom, z1), new(x0, bottom, z1), new(x0, top, z1), new(x1, top, z1), new(0, 0, 1), side, 1, 1, 1);
        target.Quad(new(x0, bottom, z1), new(x0, bottom, z0), new(x0, top, z0), new(x0, top, z1), new(-1, 0, 0), side, 1, 1, 1);
    }
    private static void Side(Builder target, int x, int z, int side, float bottom, float top, AtlasRect texture, float shade)
    {
        switch (side)
        {
            case 0: target.Quad(new(x, bottom, z), new(x + 1, bottom, z), new(x + 1, top, z), new(x, top, z), new(0, 0, -1), texture, shade, shade, shade); break;
            case 1: target.Quad(new(x + 1, bottom, z), new(x + 1, bottom, z + 1), new(x + 1, top, z + 1), new(x + 1, top, z), new(1, 0, 0), texture, shade, shade, shade); break;
            case 2: target.Quad(new(x + 1, bottom, z + 1), new(x, bottom, z + 1), new(x, top, z + 1), new(x + 1, top, z + 1), new(0, 0, 1), texture, shade, shade, shade); break;
            case 3: target.Quad(new(x, bottom, z + 1), new(x, bottom, z), new(x, top, z), new(x, top, z + 1), new(-1, 0, 0), texture, shade, shade, shade); break;
        }
    }
    private sealed class Builder
    {
        private readonly List<TerrainVertex> _vertices = [];
        private readonly List<int> _indices = [];
        internal void Quad(Vec3 a, Vec3 b, Vec3 c, Vec3 d, Vec3 normal, AtlasRect texture, float r, float g, float blue, float[]? brightness = null, float[]? alpha = null)
        {
            int start = _vertices.Count;
            Add(a, texture.X, texture.Y, 0); Add(b, texture.X + texture.Width, texture.Y, 1);
            Add(c, texture.X + texture.Width, texture.Y + texture.Height, 2); Add(d, texture.X, texture.Y + texture.Height, 3);
            _indices.Add(start); _indices.Add(start + 1); _indices.Add(start + 2); _indices.Add(start); _indices.Add(start + 2); _indices.Add(start + 3);
            void Add(Vec3 p, float u, float v, int corner) { float ao = brightness?[corner] ?? 1; _vertices.Add(new(p, normal, u, v, r * ao, g * ao, blue * ao, alpha?[corner] ?? 1)); }
        }
        internal MeshBuffer Finish() => new(_vertices.ToArray(), _indices.ToArray());
    }
}
