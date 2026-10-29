using System.Text.Json.Nodes;
using Dungeons.Core.Content;
using Dungeons.Core.Util;

namespace Dungeons.Core.Level;

/// <summary>Discovery origin.</summary>
public enum LevelSource
{
    /// <summary>Packaged manifest entry.</summary>
    BuiltIn,
    /// <summary>User directory or .dlvl.</summary>
    User,
}

/// <summary>Host-supplied catalogue candidate bytes; paths are identity labels, never opened by Core.</summary>
public sealed class CatalogCandidate(string path, LevelSource source, ReadOnlyMemory<byte> bytes, bool packed = false, IAssetSource? assets = null, ReadOnlyMemory<byte> previewBytes = default)
{
    /// <summary>Host logical path.</summary>
    public string Path { get; } = path;
    /// <summary>Origin.</summary>
    public LevelSource Source { get; } = source;
    /// <summary>Raw level.json or .dlvl bytes.</summary>
    public ReadOnlyMemory<byte> Bytes { get; } = bytes;
    /// <summary>Whether bytes contain ZIP.</summary>
    public bool Packed { get; } = packed;
    /// <summary>Host-read directory assets; a packed candidate uses its own archive source.</summary>
    public IAssetSource Assets { get; } = assets ?? MemoryAssetSource.Empty;
    /// <summary>Optional host-read directory thumbnail; packed candidates use the archive's preview.png.</summary>
    public ReadOnlyMemory<byte> PreviewBytes { get; } = previewBytes;
}

/// <summary>Validated catalogue item, including broken packages for disabled selection UI.</summary>
public sealed class CatalogEntry
{
    internal CatalogEntry(CatalogCandidate candidate, LevelLoadResult result, ReadOnlyMemory<byte> preview, JsonObject? header, List<Diagnostic> diagnostics)
    {
        Path = candidate.Path; Source = candidate.Source; Document = result.Document; PreviewBytes = preview.ToArray(); Diagnostics = diagnostics.AsReadOnly();
        Id = JsonData.Text(header?["id"], Path); Name = JsonData.Localized(header?["name"]); if (Name.Length == 0) { Name = Id; }
        Author = JsonData.Text(header?["author"]); Version = JsonData.Text(header?["version"], "0.0.0"); Difficulty = JsonData.Integer(header?["difficulty"], 1);
        Description = JsonData.Localized(header?["description"]);
    }
    /// <summary>Authored id, or logical path for unreadable packages.</summary>
    public string Id { get; }
    /// <summary>Display name.</summary>
    public string Name { get; }
    /// <summary>Author metadata.</summary>
    public string Author { get; }
    /// <summary>Recovered display description, also available for invalid levels with readable metadata.</summary>
    public string Description { get; }
    /// <summary>SemVer package version.</summary>
    public string Version { get; }
    /// <summary>Informational difficulty.</summary>
    public int Difficulty { get; }
    /// <summary>Discovery origin.</summary>
    public LevelSource Source { get; }
    /// <summary>Host logical path.</summary>
    public string Path { get; }
    /// <summary>Optional PNG source bytes.</summary>
    public ReadOnlyMemory<byte> PreviewBytes { get; }
    /// <summary>Problem details for tooltips.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; }
    /// <summary>Validated document.</summary>
    public LevelDocument? Document { get; }
    /// <summary>Whether selection may launch this entry.</summary>
    public bool IsPlayable => Document is not null && !Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
}

/// <summary>Pure catalogue validation, precedence and stable display sorting.</summary>
public static class LevelCatalog
{
    /// <summary>User copies override built-ins, higher user versions win; invalid items stay visible.</summary>
    public static IReadOnlyList<CatalogEntry> Scan(IEnumerable<CatalogCandidate> candidates, ContentRegistry content)
    {
        var entries = new List<(CatalogEntry entry, List<Diagnostic> diagnostics)>();
        foreach (CatalogCandidate candidate in candidates)
        {
            var diagnostics = new List<Diagnostic>(); ReadOnlyMemory<byte> json = candidate.Bytes, preview = candidate.Packed ? default : candidate.PreviewBytes; IAssetSource assets = candidate.Assets;
            if (candidate.Packed)
            {
                using var stream = new MemoryStream(candidate.Bytes.ToArray()); PackageLoadResult package = LevelPackage.Read(stream); diagnostics.AddRange(package.Diagnostics);
                if (package.Package is not null) { json = package.Package.Entries["level.json"]; package.Package.Entries.TryGetValue("preview.png", out preview); assets = package.Package.Assets; }
                else { json = default; }
            }
            LevelLoadResult result = json.IsEmpty ? LevelLoader.Failure(DiagnosticCode.LVL0004, "Unreadable or empty package.") : LevelLoader.Load(json.Span, content, assets: assets);
            diagnostics.AddRange(result.Diagnostics.Where(diagnostic => !diagnostics.Contains(diagnostic))); JsonObject? header = null;
            try { header = JsonData.Parse(new System.Text.UTF8Encoding(false, true).GetString(json.Span)) as JsonObject; }
            catch (Exception ex) when (ex is ArgumentException or System.Text.Json.JsonException or InvalidOperationException) { /* Result already contains the error. */ }
            entries.Add((new(candidate, result, preview, header, diagnostics), diagnostics));
        }
        var winners = new List<CatalogEntry>();
        foreach (IGrouping<string, (CatalogEntry entry, List<Diagnostic> diagnostics)> group in entries.GroupBy(e => e.entry.Id, StringComparer.Ordinal))
        {
            var ordered = group.OrderByDescending(e => e.entry.Source).ThenByDescending(e => e.entry.Version, VersionComparer.Instance).ThenBy(e => e.entry.Path, StringComparer.Ordinal).ToArray();
            if (ordered.Length > 1) { ordered[0].diagnostics.Add(new(DiagnosticSeverity.Warning, DiagnosticCode.LVL0100, "Conflicting level id: user origin, higher version, then ordinal path determine precedence.", ordered[0].entry.Path)); }
            winners.Add(ordered[0].entry);
        }
        return winners.OrderBy(e => e.Source).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Id, StringComparer.Ordinal).ToList().AsReadOnly();
    }
    private sealed class VersionComparer : IComparer<string>
    {
        internal static readonly VersionComparer Instance = new();
        public int Compare(string? x, string? y)
        {
            string[] a = (x ?? "0.0.0").Split('.'), b = (y ?? "0.0.0").Split('.');
            if (a.Length != 3 || b.Length != 3 || a.Concat(b).Any(s => s.Length == 0 || s.Any(c => c is < '0' or > '9'))) { return StringComparer.Ordinal.Compare(x, y); }
            for (int i = 0; i < 3; i++)
            {
                string left = a[i].TrimStart('0'), right = b[i].TrimStart('0'); int compare = left.Length.CompareTo(right.Length);
                if (compare == 0) { compare = StringComparer.Ordinal.Compare(left, right); }
                if (compare != 0) { return compare; }
            }
            return 0;
        }
    }
}
