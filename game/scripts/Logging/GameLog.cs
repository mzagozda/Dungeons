using System.Diagnostics;
using Dungeons.Core.Util;
using Godot;

namespace Dungeons.Game.Logging;

/// <summary>App-scoped host sink. Routine disk work is outside GameSession.Step.</summary>
public partial class GameLog : Node
{
    private double _elapsed;
    private string? _reportedFailure;
    public static GameLog? Instance { get; private set; }
    public RollingFileLogger Logger { get; private set; } = null!;
    public override void _EnterTree() { Instance = this; }
    public void Initialize(string dataDirectory)
    {
        Logger = new(Path.Combine(dataDirectory, "logs"));
        Logger.Log(LogLevel.Info, "Io", "Application started.");
        Logger.Log(LogLevel.Info, "View", "Godot " + Engine.GetVersionInfo()["string"].AsString() + "; " + RenderingServer.GetCurrentRenderingMethod() + "; " + AudioServer.GetDriverName());
        if (Dungeons.Game.Rendering.GraphicsRuntime.Instance is Dungeons.Game.Rendering.GraphicsRuntime graphics)
        { Logger.Log(LogLevel.Info, "View", "Adapter " + graphics.Adapter + "; device rule " + graphics.DeviceRule); }
    }
    public override void _Process(double delta)
    { if (Logger is not null && (_elapsed += delta) >= .25) { _elapsed = 0; FlushNow(all: false); } }
    public void FlushNow(bool all = true)
    {
        if (Logger is null) { return; }
        if (Logger.Flush(all ? int.MaxValue : 64)) { _reportedFailure = null; return; }
        if (_reportedFailure != Logger.LastError) { _reportedFailure = Logger.LastError; GD.PushWarning("Log storage is unavailable: " + Logger.LastError); }
    }
    [Conditional("DEBUG")]
    public static void Debug(string category, string message) => Instance?.Logger?.Log(LogLevel.Debug, category, message);
    public static void Info(string category, string message) => Instance?.Logger?.Log(LogLevel.Info, category, message);
    public static void Warning(string category, string message) { Instance?.Logger?.Log(LogLevel.Warning, category, message); GD.PushWarning(message); }
    public static void Error(string category, string message) { Instance?.Logger?.Log(LogLevel.Error, category, message); Instance?.FlushNow(); GD.PushError(message); }
    public override void _Notification(int what)
    { if (what == NotificationApplicationPaused || what == NotificationWMCloseRequest) { FlushNow(); } }
    public override void _ExitTree() { Logger?.Dispose(); if (Instance == this) { Instance = null; } }
}
