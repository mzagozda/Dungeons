using Godot;

namespace Dungeons.Game;

/// <summary>Platform-owned paths; Core never resolves physical or Godot resource paths.</summary>
internal static class PlatformPaths
{
    public static IPlatformPaths Default { get; } = new GodotPlatformPaths();
    public static bool AndroidImportBusy
    {
        get
        {
            using JavaClass? bridge = JavaClassWrapper.Wrap("com.godot.game.LevelIntentInbox");
            if (bridge is null) { return false; }
            bool busy = bridge.Call("isBusy").AsBool(); using JavaObject? error = JavaClassWrapper.GetException();
            return error is null && busy;
        }
    }
    public static int AndroidSdk
    {
        get
        {
            using JavaClass? bridge = JavaClassWrapper.Wrap("com.godot.game.LevelIntentInbox");
            if (bridge is null) { return 0; }
            int sdk = bridge.Call("sdkInt").AsInt32(); using JavaObject? error = JavaClassWrapper.GetException();
            return error is null ? sdk : 0;
        }
    }
    public static string UserLevelsDir => Default.UserLevelsDir;
}

internal sealed class GodotPlatformPaths : IPlatformPaths
{
    public string UserLevelsDir => OS.HasFeature("ios")
        ? OS.GetSystemDir(OS.SystemDir.Documents).PathJoin("levels")
        : ProjectSettings.GlobalizePath("user://levels");
}
