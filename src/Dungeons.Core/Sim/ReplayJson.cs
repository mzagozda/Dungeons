using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dungeons.Core.Math;
using Dungeons.Core.Util;

namespace Dungeons.Core.Sim;

/// <summary>Replay plus exact package/content identities.</summary>
public sealed class ReplayDocument
{
    /// <summary>Binds a capture to its SHA256 source and content identities.</summary>
    public ReplayDocument(ReplaySnapshot snapshot, string levelHash, string contentHash)
    {
        if (!ReplayJson.IsHex(levelHash, 64) || !ReplayJson.IsHex(contentHash, 64)) { throw new ArgumentException("SHA256 identities require 64 hexadecimal digits."); }
        Snapshot = snapshot; LevelHash = levelHash.ToLowerInvariant(); ContentHash = contentHash.ToLowerInvariant();
    }
    /// <summary>RLE commands and expected fingerprints.</summary>
    public ReplaySnapshot Snapshot { get; }
    /// <summary>SHA256 of exact level.json bytes.</summary>
    public string LevelHash { get; }
    /// <summary>Registry SHA256.</summary>
    public string ContentHash { get; }
}

/// <summary>Safe replay JSON parse result.</summary>
public sealed class ReplayLoadResult
{
    internal ReplayLoadResult(ReplayDocument? document, string? error) { Document = document; Error = error; }
    /// <summary>Bound replay when valid.</summary>
    public ReplayDocument? Document { get; }
    /// <summary>Author-facing parse failure.</summary>
    public string? Error { get; }
    /// <summary>Whether replay syntax and bounds passed.</summary>
    public bool Success => Document is not null;
}

/// <summary>Versioned .dreplay JSON with bounded RLE input, 60-tick checkpoints and optional every-tick diagnostics.</summary>
public static class ReplayJson
{
    /// <summary>Serializes outside simulation ticks using lossless round-trip float values.</summary>
    public static string Write(ReplayDocument replay)
    {
        ReplaySnapshot capture = replay.Snapshot;
        if (capture.Truncated) { throw new ArgumentException("Truncated captures cannot be published.", nameof(replay)); }
        var inputs = new JsonArray();
        foreach (ReplayFrame frame in capture.Frames)
        {
            var item = new JsonObject { ["t"] = frame.StartTick, ["duration"] = frame.Duration };
            if (frame.PlayerCount == 1) { AddInput(item, frame[0]); }
            else { var players = new JsonArray(); for (int i = 0; i < frame.PlayerCount; i++) { var player = new JsonObject(); AddInput(player, frame[i]); players.Add((JsonNode)player); } item["players"] = players; }
            inputs.Add((JsonNode)item);
        }
        var checkpoints = new JsonArray(); foreach (ReplayCheckpoint checkpoint in capture.Checkpoints) { checkpoints.Add((JsonNode)new JsonObject { ["t"] = checkpoint.Tick, ["hash"] = Hex(checkpoint.Hash) }); }
        var hashes = new JsonArray(); if (capture.HasEveryTickHashes) { foreach (ulong hash in capture.TickHashes) { hashes.Add((JsonNode)Hex(hash)); } }
        var root = new JsonObject { ["formatVersion"] = 1, ["levelId"] = capture.LevelId, ["levelHash"] = replay.LevelHash, ["contentHash"] = replay.ContentHash,
            ["seed"] = capture.Seed, ["tickCount"] = capture.TickCount, ["initialHash"] = Hex(capture.InitialHash), ["inputs"] = inputs,
            ["finalHash"] = Hex(capture.FinalHash), ["checkpoints"] = checkpoints };
        if (capture.HasEveryTickHashes) { root["tickHashes"] = hashes; }
        DifficultyProfile profile = capture.Difficulty;
        if (!profile.IsDefault) { root["difficulty"] = new JsonObject { ["hpMultiplier"] = profile.HpMultiplier, ["damageMultiplier"] = profile.DamageMultiplier, ["extraSpawnsMultiplier"] = profile.ExtraSpawnsMultiplier, ["maxMonsters"] = profile.MaxMonsters }; }
        if (capture.StartOverride is { } origin)
        {
            var start = new JsonObject { ["pos"] = new JsonArray(origin.Pos.X, origin.Pos.Z) };
            if (origin.Checkpoint is not null) { start["checkpoint"] = origin.Checkpoint; }
            root["startOverride"] = start;
        }
        return JsonData.Stringify(root, true);
    }
    /// <summary>Reads a bounded replay, accepting checkpoint-only external captures too.</summary>
    public static ReplayLoadResult Read(string json)
    {
        try
        {
            JsonObject root = JsonData.Parse(json).AsObject();
            if (root["formatVersion"]?.GetValue<int>() != 1) { throw new ArgumentException("Unsupported replay formatVersion."); }
            int ticks = root["tickCount"]!.GetValue<int>();
            if (ticks < 0 || ticks > GameConstants.DefaultReplayTicks) { throw new ArgumentException("Replay exceeds the tick limit."); }
            string levelId = root["levelId"]!.GetValue<string>(); if (levelId.Length == 0 || levelId.Length > 64) { throw new ArgumentException("Invalid levelId."); }
            ulong seed = root["seed"]!.GetValue<ulong>(), initial = ParseHash(root["initialHash"]), final = ParseHash(root["finalHash"]);
            JsonArray encoded = root["inputs"]!.AsArray();
            if (encoded.Count > ticks) { throw new ArgumentException("Too many RLE frames."); }
            var frames = new ReplayFrame[encoded.Count]; int consumed = 0;
            Span<PlayerInput> players = stackalloc PlayerInput[GameConstants.MaxPlayers];
            for (int i = 0; i < encoded.Count; i++)
            {
                JsonObject frame = encoded[i]!.AsObject(); int t = frame["t"]!.GetValue<int>();
                int duration = frame["duration"]?.GetValue<int>() ?? (i + 1 < encoded.Count ? encoded[i + 1]!["t"]!.GetValue<int>() : ticks) - t;
                if (t != consumed || duration <= 0 || duration > ticks - consumed) { throw new ArgumentException("RLE ticks must be contiguous and within tickCount."); }
                int count = 1;
                if (frame["players"] is JsonArray slots)
                { count = slots.Count; if (count > GameConstants.MaxPlayers) { throw new ArgumentException("Too many input slots."); } for (int p = 0; p < count; p++) { players[p] = ReadInput(slots[p]!.AsObject()); } }
                else { players[0] = ReadInput(frame); }
                frames[i] = new(players[..count], t, duration); consumed += duration;
            }
            if (consumed != ticks) { throw new ArgumentException("RLE does not cover tickCount."); }
            var hashes = new ulong[ticks]; bool everyTick = root["tickHashes"] is JsonArray;
            if (root["tickHashes"] is JsonArray values)
            { if (values.Count != ticks) { throw new ArgumentException("Tick hash count differs."); } for (int i = 0; i < ticks; i++) { hashes[i] = ParseHash(values[i]); } }
            if (ticks > 0) { if (everyTick && hashes[ticks - 1] != final) { throw new ArgumentException("Final and last tick hashes differ."); } hashes[ticks - 1] = final; }
            else if (initial != final) { throw new ArgumentException("Empty replay changes state."); }
            JsonArray checkpointJson = root["checkpoints"]!.AsArray();
            if (checkpointJson.Count != ticks / GameConstants.ReplayCheckpointInterval) { throw new ArgumentException("One checkpoint per 60 ticks is required."); }
            var checkpoints = new ReplayCheckpoint[checkpointJson.Count];
            for (int i = 0; i < checkpoints.Length; i++)
            {
                int tick = checkpointJson[i]!["t"]!.GetValue<int>(); ulong hash = ParseHash(checkpointJson[i]!["hash"]);
                if (tick != (i + 1) * GameConstants.ReplayCheckpointInterval || everyTick && hash != hashes[tick - 1]) { throw new ArgumentException("Checkpoint ticks/hashes differ."); }
                checkpoints[i] = new(tick, hash);
            }
            var snapshot = new ReplaySnapshot(seed, levelId, initial, frames, hashes, checkpoints, false) { HasEveryTickHashes = everyTick };
            if (root["difficulty"] is not null)
            { JsonObject profile = root["difficulty"]!.AsObject(); snapshot.Difficulty = new(profile["hpMultiplier"]!.GetValue<float>(), profile["damageMultiplier"]!.GetValue<float>(), profile["extraSpawnsMultiplier"]!.GetValue<float>(), profile["maxMonsters"]!.GetValue<int>()); }
            if (root.ContainsKey("startOverride"))
            {
                JsonObject start = root["startOverride"]?.AsObject() ?? throw new ArgumentException("Invalid editor start override.");
                if (start.Any(pair => pair.Key is not ("pos" or "checkpoint")) || start["pos"] is not JsonArray pos || pos.Count != 2 || start.ContainsKey("checkpoint") && start["checkpoint"] is null)
                { throw new ArgumentException("Invalid editor start override fields."); }
                snapshot.StartOverride = new(new(pos[0]!.GetValue<float>(), pos[1]!.GetValue<float>()), start["checkpoint"]?.GetValue<string>());
            }
            return new(new(snapshot, root["levelHash"]!.GetValue<string>(), root["contentHash"]!.GetValue<string>()), null);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or FormatException or OverflowException or NullReferenceException)
        { return new(null, ex.Message); }
    }
    private static void AddInput(JsonObject target, PlayerInput input)
    { target["move"] = new JsonArray(input.Move.X, input.Move.Z); target["aim"] = new JsonArray(input.Aim.X, input.Aim.Z); target["aimValid"] = input.AimValid; target["pressed"] = (int)input.Pressed; target["held"] = (int)input.Held; }
    private static PlayerInput ReadInput(JsonObject source)
    {
        Vec2 Vector(string key)
        { JsonArray array = source[key]!.AsArray(); if (array.Count != 2) { throw new ArgumentException("Input vectors need two components."); } return new(array[0]!.GetValue<float>(), array[1]!.GetValue<float>()); }
        int pressed = source["pressed"]!.GetValue<int>(), held = source["held"]!.GetValue<int>();
        if (pressed < 0 || pressed > ushort.MaxValue || held < 0 || held > ushort.MaxValue) { throw new ArgumentException("Input flags exceed ushort."); }
        return PlayerInput.FromRecorded(Vector("move"), Vector("aim"), source["aimValid"]!.GetValue<bool>(), (InputButtons)pressed, (InputButtons)held);
    }
    internal static bool IsHex(string value, int length) => value is not null && value.Length == length && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');
    private static string Hex(ulong value) => value.ToString("x16", CultureInfo.InvariantCulture);
    private static ulong ParseHash(JsonNode? value)
    { string text = JsonData.Text(value); if (!IsHex(text, 16)) { throw new ArgumentException("Fingerprint requires 16 hexadecimal digits."); } return ulong.Parse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture); }
}
