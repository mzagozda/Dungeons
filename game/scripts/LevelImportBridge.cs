using Dungeons.Core.Content;
using Dungeons.Core.Level;
using Dungeons.Game.Persistence;
using Godot;

namespace Dungeons.Game;

/// <summary>Godot reads Android picker paths, including content URIs; managed IO owns only the destination.</summary>
public static class LevelImportBridge
{
    public static LevelImportResult Import(string source, ContentRegistry content, string? directory = null)
    {
        using Godot.FileAccess? file = Godot.FileAccess.Open(source, Godot.FileAccess.ModeFlags.Read);
        if (file is null) { return Error("Cannot read the selected package."); }
        ulong length = file.GetLength(); if (length is 0 or > LevelPackage.MaxBytes) { return Error("Package is empty or exceeds 64 MiB."); }
        byte[] bytes = file.GetBuffer((long)length);
        if ((ulong)bytes.LongLength != length) { return Error("Selected package could not be read completely."); }
        using var stream = new MemoryStream(bytes, writable: false); return LevelImportStore.Import(stream, directory ?? PlatformPaths.UserLevelsDir, content);
    }
    private static LevelImportResult Error(string message) => new(null, [new(DiagnosticSeverity.Error, DiagnosticCode.LVL0004, message)]);
}
