using Dungeons.Core.Level;
using Godot;

namespace Dungeons.Game.UI;

/// <summary>Screen-owned LRU cache; at most 32 decoded 320x180 thumbnails (about 7 MiB RGBA).</summary>
public sealed class LevelPreviewPool : IDisposable
{
    public const int Capacity = 32;
    private readonly Dictionary<CatalogEntry, (Texture2D? texture, long used)> _cache = [];
    private long _clock;
    public int Count => _cache.Count;
    public Texture2D? Get(CatalogEntry entry)
    {
        if (_cache.TryGetValue(entry, out var found)) { _cache[entry] = (found.texture, ++_clock); return found.texture; }
        if (_cache.Count == Capacity)
        { CatalogEntry oldest = _cache.MinBy(pair => pair.Value.used).Key; _cache.Remove(oldest); }
        Texture2D? texture = null;
        if (PreviewPng.TrySanitize(entry.PreviewBytes.Span, out byte[] png))
        {
            using var image = new Image();
            if (image.LoadPngFromBuffer(png) == Error.Ok) { texture = ImageTexture.CreateFromImage(image); }
        }
        _cache.Add(entry, (texture, ++_clock)); return texture;
    }
    public void Dispose() { foreach (var value in _cache.Values) { value.texture?.Dispose(); } _cache.Clear(); }
}
