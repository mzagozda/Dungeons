namespace Dungeons.Core.Sim;

/// <summary>A run-length encoded input frame, with fixed reserved co-op slots.</summary>
public readonly struct ReplayFrame
{
    private readonly PlayerInput _input0, _input1, _input2, _input3;
    internal ReplayFrame(ReadOnlySpan<PlayerInput> inputs, int startTick, int duration)
    {
        PlayerCount = inputs.Length; StartTick = startTick; Duration = duration;
        _input0 = inputs.Length > 0 ? inputs[0] : default; _input1 = inputs.Length > 1 ? inputs[1] : default;
        _input2 = inputs.Length > 2 ? inputs[2] : default; _input3 = inputs.Length > 3 ? inputs[3] : default;
    }
    /// <summary>Zero-based input tick at which this run starts.</summary>
    public int StartTick { get; }
    /// <summary>Consecutive ticks with identical input.</summary>
    public int Duration { get; }
    /// <summary>Number of active input slots.</summary>
    public int PlayerCount { get; }
    /// <summary>Returns the input for one recorded player slot.</summary>
    public PlayerInput this[int player] => player >= 0 && player < PlayerCount ? player switch
    {
        0 => _input0, 1 => _input1, 2 => _input2, 3 => _input3, _ => throw new ArgumentOutOfRangeException(nameof(player)),
    } : throw new ArgumentOutOfRangeException(nameof(player));
    internal bool Matches(ReadOnlySpan<PlayerInput> inputs)
    {
        if (inputs.Length != PlayerCount) { return false; }
        for (int i = 0; i < PlayerCount; i++) { if (!this[i].Equals(inputs[i])) { return false; } }
        return true;
    }
}

/// <summary>Published replay fingerprint at a fixed tick.</summary>
public readonly struct ReplayCheckpoint(int tick, ulong hash)
{
    /// <summary>Completed tick number.</summary>
    public int Tick { get; } = tick;
    /// <summary>Fingerprint at this tick.</summary>
    public ulong Hash { get; } = hash;
}

/// <summary>Immutable captured input stream. JSON wire format/asset identity are added at M2.</summary>
public sealed class ReplaySnapshot
{
    /// <summary>Recorded spawn-time tier; omitted legacy JSON uses Default.</summary>
    public DifficultyProfile Difficulty { get; internal set; } = DifficultyProfile.Default;
    /// <summary>Recorded editor origin; null means ordinary authored spawning.</summary>
    public StartOverride? StartOverride { get; internal set; }
    internal bool HasEveryTickHashes { get; set; } = true;
    private readonly ReplayFrame[] _frames;
    private readonly ulong[] _hashes;
    private readonly ReplayCheckpoint[] _checkpoints;
    internal ReplaySnapshot(ulong seed, string levelId, ulong initialHash, ReplayFrame[] frames, ulong[] hashes, ReplayCheckpoint[] checkpoints, bool truncated)
    {
        Seed = seed; LevelId = levelId; InitialHash = initialHash; _frames = frames; _hashes = hashes; _checkpoints = checkpoints; Truncated = truncated;
    }
    /// <summary>Recorded seed.</summary>
    public ulong Seed { get; }
    /// <summary>Recorded authored level id.</summary>
    public string LevelId { get; }
    /// <summary>Exact initial document/content/runtime fingerprint.</summary>
    public ulong InitialHash { get; }
    /// <summary>Whether recorder capacity omitted any inputs; such captures cannot pass verification.</summary>
    public bool Truncated { get; }
    /// <summary>Number of recorded ticks.</summary>
    public int TickCount => _hashes.Length;
    /// <summary>Terminal recorded fingerprint.</summary>
    public ulong FinalHash => _hashes.Length > 0 ? _hashes[_hashes.Length - 1] : InitialHash;
    /// <summary>Run-length encoded frames.</summary>
    public ReadOnlySpan<ReplayFrame> Frames => _frames;
    /// <summary>Diagnostic every-tick hashes for finding the first divergence.</summary>
    public ReadOnlySpan<ulong> TickHashes => _hashes;
    /// <summary>Published hashes every 60 ticks.</summary>
    public ReadOnlySpan<ReplayCheckpoint> Checkpoints => _checkpoints;
}

/// <summary>Bounded, allocation-free recording during fixed steps; capture allocates outside the tick.</summary>
public sealed class ReplayRecorder
{
    private readonly ReplayFrame[] _frames;
    private readonly ulong[] _hashes;
    private readonly ReplayCheckpoint[] _checkpoints;
    private readonly ulong _seed, _initialHash;
    private readonly string _levelId;
    private readonly DifficultyProfile _difficulty;
    private readonly StartOverride? _startOverride;
    private int _frameCount, _ticks, _checkpointCount;
    internal ReplayRecorder(ulong seed, string levelId, ulong initialHash, int capacity, DifficultyProfile? difficulty = null, StartOverride? startOverride = null)
    {
        if (capacity <= 0) { throw new ArgumentOutOfRangeException(nameof(capacity)); }
        _seed = seed; _levelId = levelId; _initialHash = initialHash;
        _difficulty = difficulty ?? DifficultyProfile.Default;
        _startOverride = startOverride;
        _frames = new ReplayFrame[capacity]; _hashes = new ulong[capacity];
        _checkpoints = new ReplayCheckpoint[capacity / GameConstants.ReplayCheckpointInterval];
    }
    /// <summary>Whether the input cap was exhausted.</summary>
    public bool Overflowed { get; private set; }
    /// <summary>Number of run-length frames used.</summary>
    public int FrameCount => _frameCount;
    internal void Record(ReadOnlySpan<PlayerInput> inputs, ulong hash)
    {
        if (_ticks == _hashes.Length) { Overflowed = true; return; }
        if (_frameCount > 0 && _frames[_frameCount - 1].Matches(inputs))
        {
            ReplayFrame previous = _frames[_frameCount - 1];
            _frames[_frameCount - 1] = new ReplayFrame(inputs, previous.StartTick, previous.Duration + 1);
        }
        else { _frames[_frameCount++] = new ReplayFrame(inputs, _ticks, 1); }
        _hashes[_ticks++] = hash;
        if (_ticks % GameConstants.ReplayCheckpointInterval == 0) { _checkpoints[_checkpointCount++] = new ReplayCheckpoint(_ticks, hash); }
    }
    /// <summary>Copies a stable replay snapshot for tests/tools; never call from a simulation system.</summary>
    public ReplaySnapshot Capture() => new(_seed, _levelId, _initialHash, _frames.AsSpan(0, _frameCount).ToArray(),
        _hashes.AsSpan(0, _ticks).ToArray(), _checkpoints.AsSpan(0, _checkpointCount).ToArray(), Overflowed) { Difficulty = _difficulty, StartOverride = _startOverride };
}

/// <summary>First replay mismatch, or successful verification.</summary>
public readonly struct ReplayVerification(bool success, int divergingTick, ulong expectedHash, ulong actualHash, string message)
{
    /// <summary>All initial/tick/final fingerprints matched.</summary>
    public bool Success { get; } = success;
    /// <summary>First differing completed tick, or zero for incompatible initial state.</summary>
    public int DivergingTick { get; } = divergingTick;
    /// <summary>Recorded fingerprint.</summary>
    public ulong ExpectedHash { get; } = expectedHash;
    /// <summary>Playback fingerprint.</summary>
    public ulong ActualHash { get; } = actualHash;
    /// <summary>Diagnostic reason.</summary>
    public string Message { get; } = message;
}

/// <summary>Verifies the entire captured stream against a fresh matching session.</summary>
public static class ReplayVerifier
{
    /// <summary>Replays sanitized commands and reports the first diverging tick.</summary>
    public static ReplayVerification Verify(GameSession playback, ReplaySnapshot replay)
    {
        if (replay.Truncated) { return new(false, 0, replay.InitialHash, playback.Hash(), "Recorder capacity was exceeded."); }
        if (!Equals(playback.StartOverride, replay.StartOverride)) { return new(false, 0, replay.InitialHash, playback.Hash(), "Editor start override differs."); }
        ulong initial = playback.Hash();
        if (playback.Tick != 0 || playback.State != SessionState.Running && replay.TickCount != 0 || initial != replay.InitialHash)
        { return new(false, 0, replay.InitialHash, initial, "Initial level, content, seed or session state differs."); }
        Span<PlayerInput> inputs = stackalloc PlayerInput[GameConstants.MaxPlayers];
        int frameIndex = 0, checkpointIndex = 0;
        for (int tick = 0; tick < replay.TickCount; tick++)
        {
            while (tick >= replay.Frames[frameIndex].StartTick + replay.Frames[frameIndex].Duration) { frameIndex++; }
            ReplayFrame frame = replay.Frames[frameIndex];
            for (int player = 0; player < frame.PlayerCount; player++) { inputs[player] = frame[player]; }
            playback.Step(inputs[..frame.PlayerCount]);
            ulong actual = playback.Hash();
            if (replay.HasEveryTickHashes && actual != replay.TickHashes[tick]) { return new(false, tick + 1, replay.TickHashes[tick], actual, "State first diverged at this tick."); }
            if (checkpointIndex < replay.Checkpoints.Length && replay.Checkpoints[checkpointIndex].Tick == tick + 1)
            {
                ulong expected = replay.Checkpoints[checkpointIndex++].Hash;
                if (actual != expected) { return new(false, tick + 1, expected, actual, "State differs at this recorded checkpoint."); }
            }
        }
        if (playback.Hash() != replay.FinalHash) { return new(false, replay.TickCount, replay.FinalHash, playback.Hash(), "Final fingerprint differs."); }
        return new(true, 0, replay.FinalHash, playback.Hash(), "Replay verified.");
    }
    /// <summary>Checks exact level/content identities before executing a serialized replay.</summary>
    public static ReplayVerification Verify(GameSession playback, ReplayDocument replay)
    {
        if (playback.World.SourceHash != replay.LevelHash || playback.World.ContentHash != replay.ContentHash)
        { return new(false, 0, replay.Snapshot.InitialHash, playback.Hash(), "Level bytes or content identity differs."); }
        return Verify(playback, replay.Snapshot);
    }
}
