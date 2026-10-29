using System.Text.Json;
using Dungeons.Core.Content;
using Dungeons.Core.Level;

namespace Dungeons.Core.Tests;

internal static class MissionAssets
{
    internal static readonly string Root = FindRoot();
    internal static readonly ContentRegistry Content = LoadContent();
    internal static LevelDocument Load(string relative)
    {
        LevelLoadResult result = LevelLoader.Load(File.ReadAllText(Path.Combine(Root, relative)), Content);
        return result.Document ?? throw new InvalidOperationException(string.Join('\n', result.Diagnostics));
    }
    private static ContentRegistry LoadContent()
    {
        string directory = Path.Combine(Root, "game/content"); string[] files = JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(directory, "index.json")))!;
        ContentRegistry registry = ContentRegistry.Load(files.Select(relative => (relative, File.ReadAllText(Path.Combine(directory, relative)))));
        return registry.IsValid ? registry : throw new InvalidOperationException(string.Join('\n', registry.Diagnostics));
    }
    private static string FindRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "specification.md"))) { directory = directory.Parent; }
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
