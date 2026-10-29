using Dungeons.Core.Sim;

namespace Dungeons.Game.Input;

/// <summary>Allocation-free RLE cursor for an already preflight-verified replay. Checks the rendered session again while advancing.</summary>
public sealed class ReplayPlayback
{
    private readonly GameSession _session;
    private readonly ReplayDocument _document;
    private int _frame, _checkpoint;
    public int ConsumedTicks { get; private set; }
    public bool Complete => ConsumedTicks == _document.Snapshot.TickCount;
    public ReplayPlayback(GameSession session, ReplayDocument document)
    {
        _session = session; _document = document;
        if (session.Tick != 0 || document.Snapshot.Truncated || session.World.SourceHash != document.LevelHash || session.World.ContentHash != document.ContentHash || session.Hash() != document.Snapshot.InitialHash)
        { throw new InvalidDataException("Replay initial state or level/content identity differs."); }
        if (Complete) { Check(document.Snapshot.FinalHash); }
    }
    public bool Step()
    {
        if (Complete) { return false; } ReplaySnapshot replay = _document.Snapshot;
        while (ConsumedTicks >= replay.Frames[_frame].StartTick + replay.Frames[_frame].Duration) { _frame++; }
        ReplayFrame frame = replay.Frames[_frame]; Span<PlayerInput> inputs = stackalloc PlayerInput[GameConstants.MaxPlayers];
        for (int p = 0; p < frame.PlayerCount; p++) { inputs[p] = frame[p]; }
        _session.Step(inputs[..frame.PlayerCount]); ConsumedTicks++;
        if (_session.Tick != ConsumedTicks) { throw new InvalidDataException("Replay stopped advancing at tick " + ConsumedTicks); }
        if (_checkpoint < replay.Checkpoints.Length && replay.Checkpoints[_checkpoint].Tick == ConsumedTicks) { Check(replay.Checkpoints[_checkpoint++].Hash); }
        if (Complete) { Check(replay.FinalHash); } return true;
    }
    private void Check(ulong expected)
    { ulong actual = _session.Hash(); if (actual != expected) { throw new InvalidDataException($"Replay diverged tick={ConsumedTicks} expected={expected:x16} actual={actual:x16}"); } }
}
