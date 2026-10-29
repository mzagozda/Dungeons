namespace Dungeons.Core.Util;

/// <summary>Engine diagnostic severity.</summary>
public enum LogLevel
{
    /// <summary>Diagnostic detail.</summary>
    Debug,
    /// <summary>Normal information.</summary>
    Info,
    /// <summary>Recoverable capacity/data problem.</summary>
    Warning,
    /// <summary>Operation failure.</summary>
    Error,
}

/// <summary>Injected diagnostic destination; Core never writes files or consoles.</summary>
public interface ILogger
{
    /// <summary>Receives a category (Level, Sim, Ai, View or Io) and a static diagnostic message.</summary>
    void Log(LogLevel level, string category, string message);
}

/// <summary>Allocation-free disabled logger.</summary>
public sealed class NullLogger : ILogger
{
    /// <summary>Shared no-op destination.</summary>
    public static NullLogger Instance { get; } = new();
    private NullLogger() { }
    /// <inheritdoc />
    public void Log(LogLevel level, string category, string message) { }
}
