using System.Text.Json;
using Dungeons.Core.Content;

namespace Dungeons.Game.Audio;

/// <summary>Validated presentation metadata, compiled before any playback.</summary>
public sealed record SoundDefinition(string Id, IReadOnlyList<string> Files, string Bus, float VolumeDb, float MinimumPitch, float MaximumPitch, int MaximumInstances, bool Loop, float LoopOffset, bool Positional);

public sealed class SoundBank
{
    public IReadOnlyDictionary<string, SoundDefinition> Sounds { get; }
    public SoundBank(ContentRegistry registry)
    {
        var sounds = new Dictionary<string, SoundDefinition>(StringComparer.Ordinal);
        int voices = 0;
        foreach ((string id, DefinitionData data) in registry.Sounds)
        {
            JsonElement json = data.Json;
            string[] files = json.GetProperty("files").EnumerateArray().Select(f => f.GetString()!).ToArray();
            string bus = json.TryGetProperty("bus", out var b) ? b.GetString()! : "SFX";
            float volume = Number(json, "volumeDb", 0), offset = Number(json, "loopOffset", 0);
            int cap = json.TryGetProperty("maxInstances", out var c) ? c.GetInt32() : 4;
            float low = 1, high = 1;
            if (json.TryGetProperty("pitchRange", out var pitch)) { low = pitch[0].GetSingle(); high = pitch[1].GetSingle(); }
            bool loop = json.TryGetProperty("loop", out var l) && l.GetBoolean();
            bool positional = json.TryGetProperty("positional", out var p) ? p.GetBoolean() : bus is "SFX" or "Ambience";
            if (bus is not ("Music" or "SFX" or "UI" or "Ambience") || cap is < 1 or > 16 || files.Length is < 1 or > 16 ||
                !float.IsFinite(volume) || volume is < -80 or > 6 || !float.IsFinite(offset) || offset < 0 ||
                !float.IsFinite(low) || !float.IsFinite(high) || low < .25f || high > 4 || high < low ||
                files.Any(f => !f.StartsWith("audio/", StringComparison.Ordinal) || !f.EndsWith(".ogg", StringComparison.Ordinal) || f.Contains("..", StringComparison.Ordinal)) ||
                bus is "Music" or "UI" && positional)
            { throw new InvalidDataException("Unsupported sound definition: " + id); }
            voices += bus == "Music" ? 0 : cap;
            if (voices > 256) { throw new InvalidDataException("The sound bank exceeds its 256-voice preparation limit."); }
            sounds.Add(id, new(id, Array.AsReadOnly(files), bus, volume, low, high, cap, loop, offset, positional));
        }
        Sounds = new System.Collections.ObjectModel.ReadOnlyDictionary<string, SoundDefinition>(sounds);
    }
    private static float Number(JsonElement json, string key, float fallback) => json.TryGetProperty(key, out var value) ? value.GetSingle() : fallback;
}
