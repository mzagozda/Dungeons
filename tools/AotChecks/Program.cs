using System.Text.Json;
using System.Runtime.CompilerServices;
using Dungeons.Core.Content;
using Dungeons.Core.Level;
using Dungeons.Core.Sim;
using Dungeons.Game.Persistence;
using Dungeons.Game.Rendering;

if (args.Length != 3) { throw new ArgumentException("Expected content directory, real replay and new output directory."); }
string output = Path.GetFullPath(args[2]);
if (Directory.Exists(output)) { throw new IOException("Output must be new."); }
Directory.CreateDirectory(output);
var checks = new List<(string Name, bool Passed, string? Error)>();
int verifiedTicks = 0;
string? verifiedHash = null;
ContentRegistry LoadContent()
{
    using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(args[0], "index.json")));
    return ContentRegistry.Load(manifest.RootElement.EnumerateArray().Select(row => (row.GetString()!, File.ReadAllText(Path.Combine(args[0], row.GetString()!)))));
}
void Check(string name, Action action)
{
    try { action(); checks.Add((name, true, null)); }
    catch (Exception error) { checks.Add((name, false, error.GetType().Name + ": " + error.Message)); }
}
Check("settings-roundtrip", () =>
{
    var settings = GameSettings.Defaults(QualityPreset.High, false) with { Controls = new() { AutoAim = AimAssist.Strong,
        Bindings = new() { ["attack"] = [new InputBinding(BindingKind.Key, 74)] }, TouchPositions = new() { ["Attack"] = new(.8f, .7f) } } };
    var store = new SettingsStore(Path.Combine(output, "settings"), settings);
    var again = new SettingsStore(Path.Combine(output, "settings"), GameSettings.Defaults(QualityPreset.Low, true));
    if (again.Current.Controls.AutoAim != AimAssist.Strong || again.Current.Controls.Bindings["attack"][0].Kind != BindingKind.Key || again.Current.Video.Preset != QualityPreset.High) { throw new InvalidDataException("Settings fields did not round-trip."); }
});
Check("profile-roundtrip", () =>
{
    var store = new RunStore(Path.Combine(output, "profile"));
    RunTicket ticket = store.Begin("core.mossy_hollow", overrideSeed: 123UL);
    var again = new RunStore(Path.Combine(output, "profile"));
    if (again.Profile.RunCounter != 1 || again.Profile.Levels[ticket.LevelId].LastSeed != 123 || again.Profile.Levels[ticket.LevelId].Attempts != 1) { throw new InvalidDataException("Profile fields did not round-trip."); }
});
Check("real-replay-roundtrip", () =>
{
    ReplayDocument replay = ReplayJson.Read(File.ReadAllText(args[1])).Document ?? throw new InvalidDataException("Invalid supplied replay.");
    string json = ReplayJson.Write(replay);
    ReplayDocument again = ReplayJson.Read(json).Document ?? throw new InvalidDataException("Rewritten replay is invalid.");
    if (again.Snapshot.FinalHash != replay.Snapshot.FinalHash || again.Snapshot.TickCount != replay.Snapshot.TickCount) { throw new InvalidDataException("Replay identity changed."); }
    ContentRegistry content = LoadContent();
    LevelDocument level = LevelLoader.Load(File.ReadAllText(Path.Combine(Path.GetDirectoryName(args[0])!, "levels", "mossy_hollow", "level.json")), content).Document ?? throw new InvalidDataException("Built-in mission failed validation.");
    ReplayVerification verification = ReplayVerifier.Verify(new GameSession(level, replay.Snapshot.Seed, difficulty: replay.Snapshot.Difficulty, content: content, startOverride: replay.Snapshot.StartOverride), replay);
    if (!verification.Success) { throw new InvalidDataException("Actual mission replay failed: " + verification); }
    verifiedTicks = replay.Snapshot.TickCount; verifiedHash = replay.Snapshot.FinalHash.ToString("x16");
    File.WriteAllText(Path.Combine(output, "replay.dreplay"), json);
});
Check("editor-catalogue", () =>
{
    ContentRegistry content = LoadContent();
    string json = ContentEditorCatalog.ToJson(content).ToJsonString();
    File.WriteAllText(Path.Combine(output, "catalogue.json"), json);
});
using (Stream file = new FileStream(Path.Combine(output, "result.json"), FileMode.CreateNew))
using (var writer = new Utf8JsonWriter(file, new() { Indented = true }))
{
    writer.WriteStartObject(); writer.WriteBoolean("passed", checks.All(check => check.Passed));
    writer.WriteBoolean("reflectionEnabled", JsonSerializer.IsReflectionEnabledByDefault);
    writer.WriteBoolean("nativeAotBinary", !RuntimeFeature.IsDynamicCodeSupported); writer.WriteBoolean("physicalAcceptance", false);
    writer.WriteNumber("verifiedReplayTicks", verifiedTicks); writer.WriteString("verifiedReplayHash", verifiedHash);
    writer.WriteStartArray("checks");
    foreach (var check in checks) { writer.WriteStartObject(); writer.WriteString("name", check.Name); writer.WriteBoolean("passed", check.Passed); writer.WriteString("error", check.Error); writer.WriteEndObject(); }
    writer.WriteEndArray(); writer.WriteEndObject();
}
Console.WriteLine(File.ReadAllText(Path.Combine(output, "result.json")));
return checks.All(check => check.Passed) ? 0 : 1;
