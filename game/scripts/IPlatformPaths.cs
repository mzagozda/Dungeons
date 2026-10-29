namespace Dungeons.Game;

/// <summary>Platform-owned physical directory shared by discovery, import and Open Levels Folder.</summary>
public interface IPlatformPaths
{
    string UserLevelsDir { get; }
}
