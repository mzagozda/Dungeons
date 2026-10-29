using System.Collections.ObjectModel;
using System.IO.Compression;

namespace Dungeons.Core.Level;

/// <summary>Safe in-memory .dlvl package. Extraction to a filesystem is a host/tool responsibility.</summary>
public sealed class LevelPackage
{
    /// <summary>Maximum aggregate uncompressed bytes and compressed archive input.</summary>
    public const int MaxBytes = 64 * 1024 * 1024;
    /// <summary>Maximum ZIP entries including directory entries.</summary>
    public const int MaxEntries = 512;
    private readonly Dictionary<string, ReadOnlyMemory<byte>> _entries;
    private LevelPackage(Dictionary<string, ReadOnlyMemory<byte>> entries) { _entries = entries; Entries = new ReadOnlyDictionary<string, ReadOnlyMemory<byte>>(entries); Assets = MemoryAssetSource.FromPackage(Entries); }
    /// <summary>Portable relative file names and immutable byte views.</summary>
    public IReadOnlyDictionary<string, ReadOnlyMemory<byte>> Entries { get; }
    /// <summary>Reserved assets/ entries exposed through the host-independent extension point.</summary>
    public IAssetSource Assets { get; }
    /// <summary>Builds a bounded package from supplied data; duplicate/unsafe names are rejected.</summary>
    public static PackageLoadResult Create(IEnumerable<(string path, ReadOnlyMemory<byte> bytes)> files)
    {
        var entries = new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0; int count = 0;
        foreach ((string path, ReadOnlyMemory<byte> bytes) in files)
        {
            if (++count > MaxEntries || (total += bytes.Length) > MaxBytes) { return Failure(DiagnosticCode.LVL0005, "Package exceeds 512 entries or 64 MiB."); }
            if (!IsSafePath(path) || !names.Add(path)) { return Failure(DiagnosticCode.LVL0004, "Unsafe or duplicate package path: " + path); }
            entries.Add(path, bytes.ToArray());
        }
        return Finish(entries);
    }
    /// <summary>Reads a bounded ZIP stream without owning it. Malformed input returns diagnostics.</summary>
    public static PackageLoadResult Read(Stream stream)
    {
        try
        {
            // Copy also bounds non-seekable inputs before ZipArchive constructs its central directory.
            using var input = new MemoryStream(); byte[] buffer = new byte[8192]; int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            { if (input.Length + read > MaxBytes) { return Failure(DiagnosticCode.LVL0005, "Compressed package exceeds 64 MiB."); } input.Write(buffer, 0, read); }
            input.Position = 0; using var zip = new ZipArchive(input, ZipArchiveMode.Read, true);
            if (zip.Entries.Count > MaxEntries) { return Failure(DiagnosticCode.LVL0005, "Package exceeds 512 entries."); }
            var entries = new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0;
            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                string path = entry.FullName;
                bool directory = path.EndsWith("/", StringComparison.Ordinal);
                if (!IsSafePath(directory ? path.TrimEnd('/') : path) || !names.Add(path.TrimEnd('/'))) { return Failure(DiagnosticCode.LVL0004, "Unsafe or duplicate package path: " + path); }
                if (entry.Length < 0 || entry.Length > MaxBytes || (total += entry.Length) > MaxBytes) { return Failure(DiagnosticCode.LVL0005, "Uncompressed package exceeds 64 MiB."); }
                if (directory) { if (entry.Length != 0) { return Failure(DiagnosticCode.LVL0004, "Directory entry carries data."); } continue; }
                using Stream source = entry.Open(); using var bytes = new MemoryStream(); long actual = 0;
                while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                { actual += read; if (actual > entry.Length || actual > MaxBytes) { return Failure(DiagnosticCode.LVL0005, "ZIP entry exceeded its declared size."); } bytes.Write(buffer, 0, read); }
                if (actual != entry.Length) { return Failure(DiagnosticCode.LVL0004, "ZIP entry length mismatch."); }
                entries.Add(path, bytes.ToArray());
            }
            return Finish(entries);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException or InvalidOperationException or NotSupportedException or ObjectDisposedException)
        { return Failure(DiagnosticCode.LVL0004, ex.Message); }
    }
    /// <summary>Writes sorted UTF8 deflated entries with a stable timestamp; leaves destination open.</summary>
    public void Write(Stream destination)
    {
        using var zip = new ZipArchive(destination, ZipArchiveMode.Create, true);
        foreach (KeyValuePair<string, ReadOnlyMemory<byte>> file in _entries.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            ZipArchiveEntry entry = zip.CreateEntry(file.Key, CompressionLevel.Optimal); entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using Stream output = entry.Open(); output.Write(file.Value.Span);
        }
    }
    /// <summary>Checks one portable relative ZIP path, including Windows device/path restrictions.</summary>
    public static bool IsSafePath(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > 512 || path[0] == '/' || path.Contains('\\') || path.Any(c => c < ' ' || "<>:\"|?*".Contains(c))) { return false; }
        foreach (string segment in path.Split('/'))
        {
            if (segment.Length == 0 || segment is "." or ".." || segment.EndsWith(" ", StringComparison.Ordinal) || segment.EndsWith(".", StringComparison.Ordinal)) { return false; }
            string stem = segment.Split('.')[0].ToUpperInvariant();
            if (stem is "CON" or "PRN" or "AUX" or "NUL" || stem.Length == 4 && stem[3] is >= '1' and <= '9' && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal))) { return false; }
        }
        return true;
    }
    private static PackageLoadResult Finish(Dictionary<string, ReadOnlyMemory<byte>> entries)
    {
        if (!entries.ContainsKey("level.json")) { return Failure(DiagnosticCode.LVL0004, "Package must contain root level.json."); }
        var files = new HashSet<string>(entries.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (string path in files)
        {
            for (int slash = path.IndexOf('/'); slash >= 0; slash = path.IndexOf('/', slash + 1))
            { if (files.Contains(path.Substring(0, slash))) { return Failure(DiagnosticCode.LVL0004, "A package file conflicts with a parent directory."); } }
        }
        var diagnostics = new List<Diagnostic>();
        if (entries.Keys.Any(k => k.StartsWith("assets/", StringComparison.Ordinal))) { diagnostics.Add(MemoryAssetSource.UnsupportedWarning); }
        return new(new(entries), diagnostics);
    }
    private static PackageLoadResult Failure(DiagnosticCode code, string message) => new(null, [new(DiagnosticSeverity.Error, code, message)]);
}

/// <summary>Package boundary result.</summary>
public sealed class PackageLoadResult
{
    internal PackageLoadResult(LevelPackage? package, List<Diagnostic> diagnostics) { Package = package; Diagnostics = diagnostics.AsReadOnly(); }
    /// <summary>Validated archive structure, or null.</summary>
    public LevelPackage? Package { get; }
    /// <summary>Safe package errors/warnings.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; }
    /// <summary>Structure passed; level JSON still needs semantic loading.</summary>
    public bool Success => Package is not null;
}
