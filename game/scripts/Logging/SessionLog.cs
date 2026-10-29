using Dungeons.Core.Sim;
using Dungeons.Core.Util;

namespace Dungeons.Game.Logging;

/// <summary>Static loading context before binding, then a read-only live tick for Core diagnostics.</summary>
public sealed class SessionLog(RollingFileLogger destination, string levelId, ulong seed) : ILogger
{
    private GameSession? _session;
    public void Bind(GameSession session) => _session = session;
    public void Log(LogLevel level, string category, string message) => destination.Write(level, category, message, new(levelId, seed, _session?.Tick ?? 0));
}
