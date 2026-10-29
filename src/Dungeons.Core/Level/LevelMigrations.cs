using System.Text.Json.Nodes;

namespace Dungeons.Core.Level;

/// <summary>Version dispatch scaffold. V1 is the first published format; no fictitious legacy conversion.</summary>
public static class LevelMigrations
{
    /// <summary>Current authored format.</summary>
    public const int CurrentVersion = 1;
    /// <summary>Pure migration: returns an independent current-version tree or rejects unknown versions.</summary>
    public static JsonNode Migrate(JsonNode source, int fromVersion)
    {
        if (fromVersion != CurrentVersion) { throw new NotSupportedException("No migration exists for formatVersion " + fromVersion + "."); }
        return source.DeepClone();
    }
}
