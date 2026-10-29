using Dungeons.Core.Level;

namespace Dungeons.Core.Sim;

public sealed partial class GameSession
{
    /// <summary>Number of successful development reloads. Not simulation state or a replay field.</summary>
    public long ReloadRevision { get; private set; }

    /// <summary>Starts a fresh development simulation epoch from the same level id, retaining the first
    /// living hero's position when safe, otherwise its nearest safe cell. Rebuilds navigation, triggers,
    /// prototypes, random streams and actors; ticks, mission progress, stats and loadout reset.
    /// Paused sessions stay paused unless new startup logic ends the mission. Recording sessions reject
    /// reloads because v1 replay inputs cannot encode source changes. Failure leaves this session intact.</summary>
    public void ReloadLevel(LevelDocument level)
    {
        if (level is null) { throw new ArgumentNullException(nameof(level)); }
        if (level.HasErrors) { throw new ArgumentException("The editing document has validation errors and cannot be reloaded.", nameof(level)); }
        if (Recorder is not null) { throw new InvalidOperationException("Stop recording before development level reload; v1 replays cannot encode level changes."); }
        if (level.Id != World.LevelId) { throw new ArgumentException("Reload requires the same level id; create a new session for another level.", nameof(level)); }
        var content = level.Registry ?? _contentOverride;
        // A temporary static world supplies the new gate-aware collision grid before startup actions.
        // Construct the final world at its retained origin so initial events, respawn and startup agree.
        var replacement = new World(level, World.Seed, World.Log, World.Difficulty, content);
        StartOverride? origin = null;
        if (World.Players.Count > 0 && World.Players[0].Alive && replacement.Players.Count > 0)
        {
            if (!replacement.TryNearestSafePosition(World.Players[0].Pos, replacement.Players[0].Radius, out var position))
            { throw new InvalidOperationException("Reloaded terrain has no safe hero position."); }
            origin = new(position);
            replacement = new World(level, World.Seed, World.Log, World.Difficulty, content, origin);
        }
        replacement.Mission.Initialize(replacement);
        SessionState state = replacement.Mission.Outcome is SessionState.Won or SessionState.Failed
            ? replacement.Mission.Outcome : State == SessionState.Paused ? SessionState.Paused : SessionState.Running;
        World = replacement; StartOverride = origin; _contentOverride = content; State = state; ReloadRevision++;
    }
}
