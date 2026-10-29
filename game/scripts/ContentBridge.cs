using System.Text.Json.Nodes;
using Dungeons.Core.Content;
using Dungeons.Core.Level;
using Godot;
using Dungeons.Core.Util;

namespace Dungeons.Game;

/// <summary>Godot resource IO boundary; validation and all data rules remain in Core.</summary>
public static class ContentBridge
{
    /// <summary>Reads the explicit packaged manifest; works with PCK/APK resources.</summary>
    public static ContentRegistry LoadRegistry(ILogger? log = null)
    {
        JsonArray manifest = JsonNode.Parse(ReadText("res://content/index.json"))!.AsArray();
        var sources = new List<(string path, string json)>();
        foreach (JsonNode? node in manifest)
        {
            string path = node!.GetValue<string>(); if (!LevelPackage.IsSafePath(path)) { throw new InvalidDataException("Unsafe content manifest path."); }
            sources.Add((path, ReadText("res://content/" + path)));
        }
        ContentRegistry registry = ContentRegistry.Load(sources, log);
        if (!registry.IsValid) { throw new InvalidDataException(string.Join('\n', registry.Diagnostics)); }
        return registry;
    }
    /// <summary>Loads and validates each directory in the built-in manifest without filesystem enumeration.</summary>
    public static IReadOnlyList<LevelDocument> LoadBuiltins(ContentRegistry content)
    {
        JsonArray manifest = JsonNode.Parse(ReadText("res://levels/index.json"))!.AsArray(); var levels = new List<LevelDocument>();
        foreach (JsonNode? node in manifest)
        {
            string directory = node!.GetValue<string>(); if (!LevelPackage.IsSafePath(directory)) { throw new InvalidDataException("Unsafe level manifest path."); }
            string root = "res://levels/" + directory, json = ReadText(root + "/level.json");
            LevelLoadResult result = LevelLoader.Load(json, content, assets: LevelAssetBridge.Read(root, LevelPackage.MaxBytes - System.Text.Encoding.UTF8.GetByteCount(json)));
            if (!result.Success || result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Warning)) { throw new InvalidDataException(string.Join('\n', result.Diagnostics)); }
            levels.Add(result.Document!);
        }
        return levels.AsReadOnly();
    }
    /// <summary>Reads bounded text through Godot's virtual resource paths.</summary>
    public static string ReadText(string path)
    {
        using Godot.FileAccess? input = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        if (input is null) { throw new IOException("Cannot read " + path + ": " + Godot.FileAccess.GetOpenError()); }
        if (input.GetLength() > LevelPackage.MaxBytes) { throw new InvalidDataException("Resource exceeds 64 MiB."); }
        return input.GetAsText();
    }
}
