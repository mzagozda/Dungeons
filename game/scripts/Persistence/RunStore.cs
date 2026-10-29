using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Dungeons.Core.Sim;

namespace Dungeons.Game.Persistence;

/// <summary>Persisted v1 per-level records.</summary>
public sealed class LevelRecord
{
    public bool Completed { get; set; }
    public int BestTimeTicks { get; set; }
    public int BestEmeralds { get; set; }
    public int Attempts { get; set; }
    public ulong LastSeed { get; set; }
}

/// <summary>Versioned profile; run state is deliberately absent.</summary>
public sealed class PlayerProfile
{
    public int FormatVersion { get; set; } = 1;
    public ulong RunCounter { get; set; }
    public long TotalEmeralds { get; set; }
    public long TotalXp { get; set; }
    public Dictionary<string, LevelRecord> Levels { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>One attempt, counted at launch even when abandoned.</summary>
public sealed class RunTicket(string levelId, ulong seed)
{
    public string LevelId { get; } = levelId;
    public ulong Seed { get; } = seed;
    internal bool Closed;
}

/// <summary>Host-only atomic profile/replay IO; never called from a simulation stage.</summary>
public sealed class RunStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true, MaxDepth = 32 };
    private static readonly JsonTypeInfo<PlayerProfile> Metadata = new ProfileJsonContext(JsonOptions).PlayerProfile;
    private readonly string _directory;
    private readonly Action<string>? _warning;
    private bool _resetProfile;
    public PlayerProfile Profile { get; private set; }
    public RunStore(string directory, Action<string>? warning = null)
    {
        _directory = Path.GetFullPath(directory); _warning = warning;
        try { Directory.CreateDirectory(_directory); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { _warning?.Invoke("Profile directory is unavailable: " + error.Message); }
        Profile = Load(); if (_resetProfile) { SaveProfile(); }
    }
    public RunTicket Begin(string levelId, ulong? authoredSeed = null, ulong? overrideSeed = null)
    {
        if (string.IsNullOrWhiteSpace(levelId)) { throw new ArgumentException("A level id is required.", nameof(levelId)); }
        if (Profile.RunCounter == ulong.MaxValue) { throw new InvalidOperationException("Profile run counter exhausted."); }
        Profile.RunCounter++;
        ulong seed = overrideSeed ?? authoredSeed ?? unchecked(Profile.RunCounter * 0x9E3779B97F4A7C15UL);
        if (!Profile.Levels.TryGetValue(levelId, out LevelRecord? record)) { record = new(); Profile.Levels.Add(levelId, record); }
        record.Attempts = record.Attempts == int.MaxValue ? int.MaxValue : record.Attempts + 1; record.LastSeed = seed; SaveProfile();
        return new(levelId, seed);
    }
    public void Finish(RunTicket ticket, GameSession session, string levelHash, string contentHash)
    {
        if (ticket.Closed) { return; }
        if (session.World.LevelId != ticket.LevelId || session.World.Seed != ticket.Seed || session.World.Mission.Results is not MissionResults results) { throw new ArgumentException("Run ticket does not match a finished session."); }
        if (!Profile.Levels.TryGetValue(ticket.LevelId, out LevelRecord? record)) { throw new ArgumentException("Run ticket was not registered by this store.", nameof(ticket)); }
        ticket.Closed = true;
        Profile.TotalEmeralds = Add(Profile.TotalEmeralds, results.Emeralds); Profile.TotalXp = Add(Profile.TotalXp, results.Xp);
        record.BestEmeralds = System.Math.Max(record.BestEmeralds, results.Emeralds);
        if (session.State == SessionState.Won)
        { if (!record.Completed || results.Ticks < record.BestTimeTicks) { record.BestTimeTicks = results.Ticks; } record.Completed = true; }
        SaveProfile(); SaveReplay(session, levelHash, contentHash);
    }
    public void Abandon(RunTicket ticket, GameSession session, string levelHash, string contentHash)
    { if (ticket.Closed) { return; } ticket.Closed = true; SaveReplay(session, levelHash, contentHash); }
    private void SaveReplay(GameSession session, string levelHash, string contentHash)
    {
        if (session.Recorder is null) { return; }
        try
        {
            string directory = Path.Combine(_directory, "replays"); Directory.CreateDirectory(directory);
            string name = "run-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff", CultureInfo.InvariantCulture) + "-" + Profile.RunCounter.ToString("D20", CultureInfo.InvariantCulture) + ".dreplay";
            AtomicWrite(Path.Combine(directory, name), ReplayJson.Write(new(session.Recorder.Capture(), levelHash, contentHash)));
            string[] files = Directory.GetFiles(directory, "run-*.dreplay").OrderDescending(StringComparer.Ordinal).ToArray();
            for (int i = 5; i < files.Length; i++) { File.Delete(files[i]); }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { _warning?.Invoke("Replay could not be saved: " + error.Message); }
    }
    private PlayerProfile Load()
    {
        string path = Path.Combine(_directory, "profile.json"); if (!File.Exists(path)) { return new(); }
        try
        {
            if (new FileInfo(path).Length > 1024 * 1024) { throw new JsonException("Profile exceeds its size limit."); }
            PlayerProfile profile = JsonSerializer.Deserialize(File.ReadAllText(path), Metadata) ?? throw new JsonException("Profile is null.");
            if (profile.FormatVersion != 1 || profile.TotalEmeralds < 0 || profile.TotalXp < 0 || profile.Levels is null || profile.Levels.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null || pair.Value.Attempts < 0 || pair.Value.BestTimeTicks < 0 || pair.Value.BestEmeralds < 0)) { throw new JsonException("Invalid profile values."); }
            return profile;
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
        {
            try { if (File.Exists(path)) { File.Move(path, path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff", CultureInfo.InvariantCulture)); } }
            catch (Exception backupError) when (backupError is IOException or UnauthorizedAccessException) { _warning?.Invoke("Profile backup failed: " + backupError.Message); }
            _warning?.Invoke("Profile was reset after a read failure: " + error.Message); _resetProfile = true; return new();
        }
    }
    private void SaveProfile()
    {
        try { AtomicWrite(Path.Combine(_directory, "profile.json"), JsonSerializer.Serialize(Profile, Metadata)); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { _warning?.Invoke("Profile could not be saved: " + error.Message); }
    }
    private static long Add(long total, int amount) => total > long.MaxValue - amount ? long.MaxValue : total + amount;
    private static void AtomicWrite(string path, string text)
    {
        string temporary = path + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, System.IO.FileAccess.Write, FileShare.None))
        { using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false), leaveOpen: true)) { writer.Write(text); writer.Flush(); } stream.Flush(true); }
        File.Move(temporary, path, true);
    }
}
