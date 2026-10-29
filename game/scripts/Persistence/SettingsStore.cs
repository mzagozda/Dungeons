using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Dungeons.Game.Rendering;

namespace Dungeons.Game.Persistence;

/// <summary>Bounded, versioned, atomic host settings with retained corrupt-input evidence.</summary>
public sealed class SettingsStore
{
    public const int MaxBytes = 64 * 1024;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true, MaxDepth = 16,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter<QualityPreset>(allowIntegerValues: false), new JsonStringEnumConverter<ShadowQuality>(allowIntegerValues: false),
            new JsonStringEnumConverter<AimAssist>(allowIntegerValues: false), new JsonStringEnumConverter<BindingKind>(allowIntegerValues: false), new JsonStringEnumConverter<HealthPalette>(allowIntegerValues: false) },
    };
    private static readonly JsonTypeInfo<GameSettings> Metadata = new SettingsJsonContext(Options).GameSettings;
    private readonly string _path;
    private readonly Action<string>? _warning;
    public GameSettings Current { get; private set; }
    public SettingsStore(string directory, GameSettings defaults, Action<string>? warning = null)
    {
        SettingsValidation.Validate(defaults); _path = Path.Combine(Path.GetFullPath(directory), "settings.json"); _warning = warning;
        Current = defaults;
        if (File.Exists(_path))
        {
            try { if (new FileInfo(_path).Length > MaxBytes) { throw new JsonException("Settings exceed 64 KiB."); } Current = Read(File.ReadAllText(_path)); return; }
            catch (Exception error) when (error is JsonException or ArgumentException or IOException or UnauthorizedAccessException)
            {
                try { File.Move(_path, _path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff", CultureInfo.InvariantCulture)); }
                catch (Exception backup) when (backup is IOException or UnauthorizedAccessException) { _warning?.Invoke("Settings backup failed: " + backup.Message); }
                _warning?.Invoke("Settings reset after a read failure: " + error.Message);
            }
        }
        Save(defaults);
    }
    public static GameSettings Read(string text)
    {
        if (Encoding.UTF8.GetByteCount(text) > MaxBytes) { throw new JsonException("Settings exceed 64 KiB."); }
        using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 16 });
        CheckDuplicates(document.RootElement);
        if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("formatVersion", out _)) { throw new JsonException("Settings require formatVersion."); }
        GameSettings settings = JsonSerializer.Deserialize(text, Metadata) ?? throw new JsonException("Settings cannot be null.");
        SettingsValidation.Validate(settings); return settings;
    }
    public bool Save(GameSettings settings)
    {
        SettingsValidation.Validate(settings); string text = JsonSerializer.Serialize(settings, Metadata);
        GameSettings snapshot = Read(text); // Own mutable dictionaries independently of an editor's draft.
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!); string temporary = _path + ".tmp";
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            { using (var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true)) { writer.Write(text); writer.Flush(); } stream.Flush(true); }
            File.Move(temporary, _path, true); Current = snapshot; return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { _warning?.Invoke("Settings could not be saved: " + error.Message); return false; }
    }
    private static void CheckDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        { var names = new HashSet<string>(StringComparer.Ordinal); foreach (JsonProperty property in element.EnumerateObject()) { if (!names.Add(property.Name)) { throw new JsonException("Duplicate settings key: " + property.Name); } CheckDuplicates(property.Value); } }
        else if (element.ValueKind == JsonValueKind.Array) { foreach (JsonElement item in element.EnumerateArray()) { CheckDuplicates(item); } }
    }
}
