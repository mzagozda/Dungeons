using System.Text.Json.Nodes;
using Dungeons.Core.Content;
using Dungeons.Core.Level;
using Godot;
using Dungeons.Core.Util;

namespace Dungeons.Game;

/// <summary>Virtual packaged resources and platform user-folder IO feed the pure catalogue.</summary>
public static class CatalogBridge
{
    /// <summary>Scans built-ins plus direct user .dlvl files and level subdirectories.</summary>
    public static IReadOnlyList<CatalogEntry> Scan(ContentRegistry content, string? userDirectory = null, ILogger? log = null)
        => ScanCore(content, userDirectory ?? PlatformPaths.UserLevelsDir, log, null, CancellationToken.None);
    internal static IReadOnlyDictionary<string, byte[]> PrepareBuiltinPreviews(ILogger? log)
    {
        var previews = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (string directory in BuiltinDirectories()) { previews.Add(directory, ReadPreview(directory, log)); }
        return previews;
    }
    internal static IReadOnlyList<CatalogEntry> ScanForStartup(ContentRegistry content, string userDirectory, ILogger? log, IReadOnlyDictionary<string, byte[]> previews, CancellationToken cancellationToken)
        => ScanCore(content, userDirectory, log, previews, cancellationToken);
    private static IEnumerable<string> BuiltinDirectories()
    {
        foreach (JsonNode? node in JsonNode.Parse(ContentBridge.ReadText("res://levels/index.json"))!.AsArray())
        {
            string name = node!.GetValue<string>(); if (!LevelPackage.IsSafePath(name)) { throw new InvalidDataException("Unsafe built-in directory."); }
            yield return "res://levels/" + name;
        }
    }
    private static IReadOnlyList<CatalogEntry> ScanCore(ContentRegistry content, string directory, ILogger? log, IReadOnlyDictionary<string, byte[]>? previews, CancellationToken cancellationToken)
    {
        var candidates = new List<CatalogCandidate>();
        foreach (string builtin in BuiltinDirectories())
        {
            cancellationToken.ThrowIfCancellationRequested();
            candidates.Add(DirectoryCandidate(builtin, LevelSource.BuiltIn, log, previews));
        }
        if (DirAccess.MakeDirRecursiveAbsolute(directory) != Error.Ok) { throw new IOException("Cannot create user levels directory: " + directory); }
        foreach (string file in DirAccess.GetFilesAt(directory).Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (file.EndsWith(".dlvl", StringComparison.OrdinalIgnoreCase))
            { string path = directory.PathJoin(file); candidates.Add(new(path, LevelSource.User, Read(path), true)); }
        }
        foreach (string folder in DirAccess.GetDirectoriesAt(directory).Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = directory.PathJoin(folder).PathJoin("level.json");
            if (Godot.FileAccess.FileExists(path)) { candidates.Add(DirectoryCandidate(directory.PathJoin(folder), LevelSource.User, log)); }
        }
        IReadOnlyList<CatalogEntry> entries = LevelCatalog.Scan(candidates, content);
        cancellationToken.ThrowIfCancellationRequested();
        if (log is not null)
        {
            foreach (CatalogEntry entry in entries)
            { foreach (Diagnostic diagnostic in entry.Diagnostics) { log.Log(diagnostic.Severity switch { DiagnosticSeverity.Error => LogLevel.Error, DiagnosticSeverity.Warning => LogLevel.Warning, _ => LogLevel.Info }, "Level", diagnostic.ToString()); } }
        }
        return entries;
    }
    private static CatalogCandidate DirectoryCandidate(string directory, LevelSource source, ILogger? log, IReadOnlyDictionary<string, byte[]>? previews = null)
    {
        string path = directory.PathJoin("level.json"); byte[] json = Read(path);
        try
        {
            byte[] preview = previews is null ? ReadPreview(directory, log) : previews[directory];
            return new(path, source, json, assets: LevelAssetBridge.Read(directory, LevelPackage.MaxBytes - json.Length - preview.Length), previewBytes: preview);
        }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            log?.Log(LogLevel.Error, "Level", "Cannot read reserved assets for " + path + ": " + error.Message);
            return new(path, source, default); // Keep an invalid, disabled entry visible instead of dropping it.
        }
    }
    private static byte[] ReadPreview(string directory, ILogger? log)
    {
        string path = directory.PathJoin("preview.png");
        if (!Godot.FileAccess.FileExists(path))
        {
            // Exported built-in PNGs are resource-remapped to lossless .ctex data.
            // User files always take the bounded raw-byte path below.
            if (directory.StartsWith("res://", StringComparison.Ordinal) && ResourceLoader.Exists(path, "Texture2D"))
            {
                Texture2D texture = GD.Load<Texture2D>(path);
                if (texture.GetWidth() != UI.PreviewPng.Width || texture.GetHeight() != UI.PreviewPng.Height) { return []; }
                using Image image = texture.GetImage(); return image.SavePngToBuffer();
            }
            return [];
        }
        using DirAccess? parent = DirAccess.Open(directory);
        if (parent is null || parent.IsLink("preview.png")) { log?.Log(LogLevel.Warning, "Level", "Linked or unreadable preview ignored: " + path); return []; }
        using Godot.FileAccess? file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        if (file is null || file.GetLength() > UI.PreviewPng.MaxBytes) { log?.Log(LogLevel.Warning, "Level", "Unreadable or oversized preview ignored: " + path); return []; }
        return file.GetBuffer((long)file.GetLength());
    }
    private static byte[] Read(string path)
    {
        using Godot.FileAccess? file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        return file is null || file.GetLength() > LevelPackage.MaxBytes ? [] : file.GetBuffer((long)file.GetLength());
    }
}
