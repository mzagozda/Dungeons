using System.Collections.ObjectModel;

namespace Dungeons.Core.Level;

/// <summary>Package-relative reserved asset bytes. V1 preserves them and warns; rendering does not apply overrides.</summary>
public interface IAssetSource
{
    /// <summary>Ordinally sorted portable paths rooted at assets/.</summary>
    IReadOnlyList<string> Paths { get; }
    /// <summary>Reads a case-sensitive package path, without filesystem access. Missing/unsafe paths return false.</summary>
    bool TryRead(string path, out ReadOnlyMemory<byte> bytes);
}

/// <summary>Bounded, copied asset data for in-memory levels and host directory discovery.</summary>
public sealed class MemoryAssetSource : IAssetSource
{
    private readonly IReadOnlyDictionary<string, ReadOnlyMemory<byte>> _entries;
    /// <summary>Constructs a source with the same path/count/byte boundaries as a level package.</summary>
    public MemoryAssetSource(IEnumerable<(string path, ReadOnlyMemory<byte> bytes)> files)
        : this(Copy(files)) { }
    private MemoryAssetSource(Dictionary<string, ReadOnlyMemory<byte>> entries)
    {
        _entries = new ReadOnlyDictionary<string, ReadOnlyMemory<byte>>(entries);
        Paths = Array.AsReadOnly(entries.Keys.OrderBy(path => path, StringComparer.Ordinal).ToArray());
    }
    /// <summary>Shared empty source for levels with no reserved assets.</summary>
    public static IAssetSource Empty { get; } = new MemoryAssetSource(new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal));
    /// <inheritdoc/>
    public IReadOnlyList<string> Paths { get; }
    /// <inheritdoc/>
    public bool TryRead(string path, out ReadOnlyMemory<byte> bytes)
    {
        bytes = default;
        return IsAssetPath(path) && _entries.TryGetValue(path, out bytes);
    }
    internal static IAssetSource FromPackage(IReadOnlyDictionary<string, ReadOnlyMemory<byte>> entries)
    {
        var assets = new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal);
        foreach (var pair in entries) { if (IsAssetPath(pair.Key)) { assets.Add(pair.Key, pair.Value); } }
        return assets.Count == 0 ? Empty : new MemoryAssetSource(assets);
    }
    private static bool IsAssetPath(string? path) => path is not null && path.StartsWith("assets/", StringComparison.Ordinal) && LevelPackage.IsSafePath(path);
    private static Dictionary<string, ReadOnlyMemory<byte>> Copy(IEnumerable<(string path, ReadOnlyMemory<byte> bytes)> files)
    {
        if (files is null) { throw new ArgumentNullException(nameof(files)); }
        var entries = new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0;
        foreach ((string path, ReadOnlyMemory<byte> bytes) in files)
        {
            if (entries.Count == LevelPackage.MaxEntries || (total += bytes.Length) > LevelPackage.MaxBytes) { throw new InvalidDataException("Asset source exceeds package limits."); }
            if (!IsAssetPath(path) || !names.Add(path)) { throw new InvalidDataException("Unsafe or duplicate asset path: " + path); }
            entries.Add(path, bytes.ToArray());
        }
        foreach (string path in names)
        {
            for (int slash = path.IndexOf('/'); slash >= 0; slash = path.IndexOf('/', slash + 1))
            { if (names.Contains(path.Substring(0, slash))) { throw new InvalidDataException("Asset file conflicts with a parent directory."); } }
        }
        return entries;
    }
    internal static readonly Diagnostic UnsupportedWarning = new(DiagnosticSeverity.Warning, DiagnosticCode.LVL0100, "Package asset overrides are preserved but ignored in v1.", "/assets");
}
