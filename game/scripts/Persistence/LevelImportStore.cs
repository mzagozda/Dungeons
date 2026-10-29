using System.Security.Cryptography;
using Dungeons.Core.Content;
using Dungeons.Core.Level;

namespace Dungeons.Game.Persistence;

public sealed record LevelImportResult(string? Path, IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool Success => Path is not null;
}

/// <summary>Validates bounded package bytes before an atomic, content-addressed import. Never extracts ZIP paths.</summary>
public static class LevelImportStore
{
    public static LevelImportResult Import(Stream source, string directory, ContentRegistry content)
    {
        string? temporary = null;
        try
        {
            if (source.CanSeek && source.Length - source.Position > LevelPackage.MaxBytes) { return Failure("Package exceeds 64 MiB."); }
            using var buffer = new MemoryStream(); byte[] block = new byte[8192]; int read;
            while ((read = source.Read(block, 0, block.Length)) > 0)
            { if (buffer.Length + read > LevelPackage.MaxBytes) { return Failure("Package exceeds 64 MiB."); } buffer.Write(block, 0, read); }
            byte[] bytes = buffer.ToArray();
            CatalogEntry entry = LevelCatalog.Scan([new("import.dlvl", LevelSource.User, bytes, true)], content).Single();
            if (!entry.IsPlayable) { return new(null, entry.Diagnostics); }
            directory = System.IO.Path.GetFullPath(directory); Directory.CreateDirectory(directory);
            string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            string path = System.IO.Path.Combine(directory, "import-" + hash + ".dlvl");
            if (File.Exists(path))
            {
                using var existing = File.OpenRead(path);
                if (existing.Length != bytes.Length || !SHA256.HashData(existing).AsSpan().SequenceEqual(SHA256.HashData(bytes))) { return Failure("Import destination has different contents; existing file preserved."); }
                return new(path, entry.Diagnostics);
            }
            temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { output.Write(bytes); output.Flush(true); }
            File.Move(temporary, path); temporary = null; return new(path, entry.Diagnostics);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return Failure("Level import failed: " + error.Message); }
        finally
        { if (temporary is not null) { try { File.Delete(temporary); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* Failed temporary file is never a catalogue candidate. */ } } }
    }
    private static LevelImportResult Failure(string message) => new(null, [new(DiagnosticSeverity.Error, DiagnosticCode.LVL0004, message)]);
}
