using System.Text;
using Dungeons.Core.Content;
using Dungeons.Core.Level;
using Dungeons.Core.Sim;
using Godot;

namespace Dungeons.Game;

/// <summary>Bounded virtual/absolute path boundary for desktop launch and validation commands.</summary>
public static class RuntimeLevelFiles
{
    public static LevelLoadResult Load(string path, ContentRegistry content)
    {
        path = Normalize(path); if (DirAccess.DirExistsAbsolute(path)) { path = path.PathJoin("level.json"); }
        byte[] bytes = Read(path);
        if (path.EndsWith(".dlvl", StringComparison.OrdinalIgnoreCase))
        {
            using var stream = new MemoryStream(bytes); PackageLoadResult packed = LevelPackage.Read(stream);
            if (packed.Package is null) { throw new InvalidDataException(string.Join('\n', packed.Diagnostics)); }
            return LevelLoader.LoadPackage(packed.Package, content);
        }
        return LevelLoader.Load(bytes, content, assets: LevelAssetBridge.Read(path.GetBaseDir().Length == 0 ? "." : path.GetBaseDir(), LevelPackage.MaxBytes - bytes.Length));
    }
    public static ReplayDocument Replay(string path)
    {
        byte[] bytes = Read(Normalize(path)); string json;
        try { json = new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException error) { throw new InvalidDataException("Replay is not valid UTF-8.", error); }
        ReplayLoadResult loaded = ReplayJson.Read(json); return loaded.Document ?? throw new InvalidDataException(loaded.Error ?? "Invalid replay.");
    }
    private static byte[] Read(string path)
    {
        using Godot.FileAccess? file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        if (file is null) { throw new IOException("Cannot read " + path + ": " + Godot.FileAccess.GetOpenError()); }
        ulong length = file.GetLength(); if (length > LevelPackage.MaxBytes) { throw new InvalidDataException("Input exceeds 64 MiB: " + path); }
        byte[] bytes = file.GetBuffer((long)length); if ((ulong)bytes.Length != length) { throw new IOException("Input read was incomplete: " + path); } return bytes;
    }
    private static string Normalize(string path)
    { path = path.Replace('\\', '/'); return path.StartsWith("res://", StringComparison.Ordinal) || path.StartsWith("user://", StringComparison.Ordinal) ? path : Path.GetFullPath(path).Replace('\\', '/'); }
}
