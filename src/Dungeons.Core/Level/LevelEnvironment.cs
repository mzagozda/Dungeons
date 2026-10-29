using System.Text.Json;
using System.Text.Json.Nodes;
using Dungeons.Core.Util;

namespace Dungeons.Core.Level;

/// <summary>Fully populated immutable environment data shared by host/editor, with §10.5 defaults.</summary>
public sealed class LevelEnvironment
{
    private const string Defaults = """
        {"sky":{"top":"#1b2a44","horizon":"#35506e","ground":"#101820"},
         "sun":{"yawDeg":35,"pitchDeg":-55,"color":"#fff1dc","energy":1.1,"shadows":true},
         "ambient":{"color":"#5a6f99","energy":0.45},"fog":{"color":"#2e4a62","density":0.015,"startDistance":18,"enabled":true},
         "glow":{"enabled":true,"intensity":0.6,"threshold":1},"camera":{"yawDeg":45,"pitchDeg":-50,"distance":22,"fovDeg":25,"lookAhead":1.5},
         "music":"core:music_forest","ambience":"core:amb_forest_night","waterColor":"#2f6f8f"}
        """;
    internal LevelEnvironment(JsonObject? source)
    {
        JsonObject merged = JsonData.Parse(Defaults).AsObject();
        if (source is not null)
        {
            foreach (KeyValuePair<string, JsonNode?> pair in source)
            {
                if (pair.Value is JsonObject values && merged[pair.Key] is JsonObject original)
                { foreach (KeyValuePair<string, JsonNode?> value in values) { original[value.Key] = value.Value?.DeepClone(); } }
                else { merged[pair.Key] = pair.Value?.DeepClone(); }
            }
        }
        using JsonDocument document = JsonDocument.Parse(JsonData.Stringify(merged)); Json = document.RootElement.Clone();
    }
    /// <summary>Default mood/camera for code-authored scenarios.</summary>
    public static LevelEnvironment Default { get; } = new(null);
    /// <summary>All standard nested blocks/fields are present; optional camera.bounds stays optional.</summary>
    public JsonElement Json { get; }
    /// <summary>Camera yaw in degrees.</summary>
    public float CameraYaw => Json.GetProperty("camera").GetProperty("yawDeg").GetSingle();
    /// <summary>Camera downward pitch in degrees.</summary>
    public float CameraPitch => Json.GetProperty("camera").GetProperty("pitchDeg").GetSingle();
    /// <summary>Perspective field of view.</summary>
    public float CameraFov => Json.GetProperty("camera").GetProperty("fovDeg").GetSingle();
    /// <summary>Camera distance in metres.</summary>
    public float CameraDistance => Json.GetProperty("camera").GetProperty("distance").GetSingle();
    /// <summary>Camera facing look-ahead distance.</summary>
    public float CameraLookAhead => Json.GetProperty("camera").GetProperty("lookAhead").GetSingle();
}
