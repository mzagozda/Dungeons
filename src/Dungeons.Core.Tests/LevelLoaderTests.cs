using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Dungeons.Core.Content;
using Dungeons.Core.Level;
using Dungeons.Core.Sim;
using Xunit;

namespace Dungeons.Core.Tests;

[Collection("Performance")]
public sealed class LevelLoaderTests
{
    private static readonly string Root = FindRoot();
    private static readonly ContentRegistry Content = LoadContent();
    private static readonly string Mossy = File.ReadAllText(Path.Combine(Root, "game/levels/mossy_hollow/level.json"));

    [Theory]
    [InlineData("gate_main", "broken")]
    [InlineData("chest_pocket", "closed")]
    [InlineData("spawn", "active")]
    public void UnsupportedObjectStatesAreRejectedBeforeTheMissionRuns(string entity, string state)
    {
        JsonObject source = JsonNode.Parse(Mossy)!.AsObject(); source["triggers"]!.AsArray().Add(new JsonObject { ["id"] = "bad_state", ["event"] = new JsonObject { ["type"] = "level_start" }, ["actions"] = new JsonArray(new JsonObject { ["type"] = "set_state", ["entity"] = entity, ["state"] = state }) });
        LevelLoadResult result = LevelLoader.Load(source.ToJsonString(), Content); Assert.False(result.Success); Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.LVL1020 && diagnostic.JsonPath.EndsWith("/state", StringComparison.Ordinal));
    }

    [Fact]
    public void GateFootprintsOutsideTheMapAreRejectedBeforeRuntimeConstruction()
    {
        JsonObject source = JsonNode.Parse(Mossy)!.AsObject();
        JsonNode gate = source["entities"]!.AsArray().Single(e => e!["id"]!.GetValue<string>() == "gate_main")!;
        gate["x"] = .5; gate["z"] = 36.5; gate["props"]!["width"] = 8;
        LevelLoadResult loaded = LevelLoader.Load(source.ToJsonString(), Content); Assert.False(loaded.Success);
        Assert.Contains(loaded.Diagnostics, d => d.Code == DiagnosticCode.LVL1012 && d.Message.Contains("footprint", StringComparison.Ordinal));
    }

    [Fact]
    public void ShippedContentAndMossyHollowValidateWithoutErrorsOrWarnings()
    {
        Assert.True(Content.IsValid, string.Join('\n', Content.Diagnostics));
        LevelLoadResult result = LevelLoader.Load(Mossy, Content);
        Assert.True(result.Success, string.Join('\n', result.Diagnostics));
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning);
        Assert.Equal("Mossy Hollow", result.Document!.Name); Assert.Equal(32, result.Document.Width); Assert.Equal(40, result.Document.Height);
        Assert.Equal(1, result.Document.GetCell(16, 11).Height); Assert.Equal(StairsDirection.North, result.Document.GetCell(15, 12).StairsDir);
        Assert.Equal(64, result.Document.SourceHash.Length); Assert.Equal(Content.Hash, result.Document.ContentHash);
        LevelLoadResult roundtrip = LevelLoader.Load(result.Document.ToJson(), Content);
        Assert.True(roundtrip.Success); Assert.Equal(result.Document.Entities.Count, roundtrip.Document!.Entities.Count);
    }

    [Theory]
    [InlineData("{")] [InlineData("[]")] [InlineData("null")]
    [InlineData("{\"formatVersion\":1,\"formatVersion\":1}")]
    [InlineData("{\"formatVersion\":1,}")] [InlineData("/* comment */ {}")] [InlineData("{\"n\":NaN}")]
    public void MalformedInputReturnsDiagnostics(string json)
    {
        LevelLoadResult result = LevelLoader.Load(json, Content);
        Assert.False(result.Success); Assert.Null(result.Document); Assert.NotEmpty(result.Diagnostics);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.LVL9999);
    }

    [Fact]
    public void StrictEncodingUnknownPropertiesVersionAndCulture()
    {
        Assert.Contains(LevelLoader.Load(new byte[] { 0xff, 0xfe }.AsSpan(), Content).Diagnostics, d => d.Code == DiagnosticCode.LVL0001);
        JsonObject source = JsonNode.Parse(Mossy)!.AsObject(); source["futureMetadata"] = new JsonObject { ["value"] = 3 };
        LevelLoadResult unknown = LevelLoader.Load(source.ToJsonString(), Content);
        Assert.True(unknown.Success); Assert.Contains(unknown.Diagnostics, d => d.Code == DiagnosticCode.LVL0100 && d.JsonPath == "/futureMetadata");
        Assert.Equal(3, unknown.Document!.ToJsonObject()["futureMetadata"]!["value"]!.GetValue<int>());
        source["formatVersion"] = 999; Assert.Contains(LevelLoader.Load(source.ToJsonString(), Content).Diagnostics, d => d.Code == DiagnosticCode.LVL0002);
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE"); LevelDocument loaded = LevelLoader.Load(Mossy, Content).Document!;
            Assert.Equal(24.5f, loaded.Entities.Single(e => e.Id == "z_corridor_1").Position.X);
        }
        finally { CultureInfo.CurrentCulture = previous; }
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(Mossy)); Assert.True(LevelLoader.Load(stream, Content).Success); Assert.True(stream.CanRead);
        JsonNode original = JsonNode.Parse(Mossy)!; JsonNode migrated = LevelMigrations.Migrate(original, 1);
        Assert.True(JsonNode.DeepEquals(original, migrated)); migrated["author"] = "changed"; Assert.NotEqual(original["author"]!.ToString(), migrated["author"]!.ToString());
        Assert.Throws<NotSupportedException>(() => LevelMigrations.Migrate(original, 0));
    }

    [Fact]
    public void SemanticFailuresHaveStableLocatedCodes()
    {
        Check(DiagnosticCode.LVL1001, root => root["layers"]!["floor"]![0] = "........");
        Check(DiagnosticCode.LVL1002, root => root["layers"]!["floor"]![0] = "?" + new string('T', 31));
        Check(DiagnosticCode.LVL1003, root => root["palette"]!["T"] = "core:missing");
        Check(DiagnosticCode.LVL1004, root => root["palette"]!["."] = "core:tall_grass");
        Check(DiagnosticCode.LVL1005, root => root["layers"]!["heights"]![0] = "g" + new string('0', 31));
        Check(DiagnosticCode.LVL1006, root => root["layers"]!["heights"]![11] = new string('0', 32));
        Check(DiagnosticCode.LVL1010, root => root["entities"]!.AsArray().RemoveAt(0));
        Check(DiagnosticCode.LVL1011, root => root["entities"]![1]!["type"] = "core:missing");
        Check(DiagnosticCode.LVL1012, root => root["entities"]![0]!["x"] = -1);
        Check(DiagnosticCode.LVL1013, root => root["entities"]![1]!["id"] = "spawn");
        Check(DiagnosticCode.LVL1020, root => root["triggers"]![3]!["actions"]![0]!["id"] = "missing");
        Check(DiagnosticCode.LVL1021, root => root["completion"]!["exit"] = "missing");
        Check(DiagnosticCode.LVL1022, root => root["triggers"]![0]!["actions"]!.AsArray().RemoveAt(0), error: false);
        Check(DiagnosticCode.LVL1023, root => root["triggers"]![2]!["event"]!["tag"] = "never_spawned", error: false);
        Check(DiagnosticCode.LVL1024, root => { root["objectives"]![0]!["type"] = "survive"; root["objectives"]![0]!["seconds"] = null; });
        Check(DiagnosticCode.LVL1025, root => root["entities"]![5]!["props"]!["openWhen"] = "missing");
        Check(DiagnosticCode.LVL1027, root => root["triggers"]![0]!["actions"]![1]!["type"] = "dialogue");
        Check(DiagnosticCode.LVL0003, root => root["triggers"]![0]!["actions"]![1]!["seconds"] = -1);
        Check(DiagnosticCode.LVL0003, root => root["triggers"]![0]!["actions"]![1]!["seconds"] = 1e100);
        Check(DiagnosticCode.LVL1040, root => { JsonArray entities = root["entities"]!.AsArray(); for (int i = entities.Count - 1; i >= 0; i--) { if (entities[i]!["type"]!.GetValue<string>() == "core:checkpoint") { entities.RemoveAt(i); } } root["triggers"]!.AsArray().RemoveAt(3); }, error: false);
    }

    [Fact]
    public void ReachabilitySpawnerCapsAndDefaultsHaveFixtures()
    {
        Check(DiagnosticCode.LVL1014, root =>
        {
            Surround(root, 24, 30);
        }, error: false);
        Check(DiagnosticCode.LVL1030, root => Surround(root, 16, 4), error: true);
        Check(DiagnosticCode.LVL1031, root => Surround(root, 16, 4), error: true);
        Check(DiagnosticCode.LVL1026, root => root["entities"]!.AsArray().Add(JsonNode.Parse("""{"id":"test_spawner","type":"core:spawner","x":4.5,"z":36.5,"props":{"entity":"core:chest"}}""")));
        Check(DiagnosticCode.LVL1050, root =>
        {
            JsonArray entities = root["entities"]!.AsArray();
            for (int i = 0; i < 65; i++) { entities.Add(new JsonObject { ["id"] = "extra_" + i, ["type"] = "core:zombie", ["x"] = 4.5f, ["z"] = 36.5f }); }
        }, error: false);
        Assert.Contains(LevelLoader.Load(Mossy, Content).Diagnostics, d => d.Code == DiagnosticCode.LVL1060 && d.Severity == DiagnosticSeverity.Info);
    }
    private static void Surround(JsonObject root, int x, int z)
    {
        foreach (var (cx, cz) in new[] { (x - 1, z), (x + 1, z), (x, z - 1), (x, z + 1) })
        { char[] row = root["layers"]!["floor"]![cz]!.GetValue<string>().ToCharArray(); row[cx] = '#'; root["layers"]!["floor"]![cz] = new string(row); }
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void FullSizeLevelWithSixtyFourMonstersLoadsUnderThreeHundredMilliseconds()
    {
        JsonObject root = JsonNode.Parse(Mossy)!.AsObject(); root["size"] = new JsonObject { ["width"] = 256, ["height"] = 256 };
        root["palette"] = new JsonObject { ["."] = "core:grass" };
        root["layers"] = new JsonObject { ["floor"] = new JsonArray(Enumerable.Range(0, 256).Select(_ => (JsonNode?)JsonValue.Create(new string('.', 256))).ToArray()),
            ["heights"] = new JsonArray(Enumerable.Range(0, 256).Select(_ => (JsonNode?)JsonValue.Create(new string('0', 256))).ToArray()) };
        var entities = new JsonArray(JsonNode.Parse("""{"id":"spawn","type":"core:player_spawn","x":4.5,"z":4.5}"""),
            JsonNode.Parse("""{"id":"exit","type":"core:exit","x":250.5,"z":250.5}"""), JsonNode.Parse("""{"type":"core:checkpoint","x":6.5,"z":6.5}"""));
        for (int i = 0; i < 64; i++) { entities.Add(new JsonObject { ["type"] = "core:zombie", ["x"] = 8.5f + i % 8 * 8, ["z"] = 8.5f + i / 8 * 8 }); }
        root["entities"] = entities; root["triggers"] = new JsonArray(); root["objectives"] = new JsonArray(); root["completion"] = new JsonObject { ["type"] = "exit", ["exit"] = "exit" };
        string json = root.ToJsonString();
        for (int i = 0; i < 2; i++) { Assert.True(LevelLoader.Load(json, Content).Success); }
        var timer = System.Diagnostics.Stopwatch.StartNew(); LevelLoadResult result = LevelLoader.Load(json, Content); timer.Stop();
        Assert.True(result.Success, string.Join('\n', result.Diagnostics)); Assert.True(timer.ElapsedMilliseconds < 300, $"Load took {timer.Elapsed.TotalMilliseconds:F2} ms.");
    }

    [Fact]
    public void LargeMalformedRowsDeepNestingAndNonFiniteNumbersStayAtDiagnosticBoundary()
    {
        JsonObject root = JsonNode.Parse(Mossy)!.AsObject();
        root["layers"]!["floor"] = new JsonArray(Enumerable.Range(0, 10000).Select(_ => (JsonNode?)JsonValue.Create("........")).ToArray());
        string[] inputs = [root.ToJsonString(), Mossy.Replace("36.5", "1e9999"), new string('[', 100) + "0" + new string(']', 100)];
        foreach (string input in inputs)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew(); LevelLoadResult result = LevelLoader.Load(input, Content);
            Assert.False(result.Success); Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.LVL9999); Assert.True(timer.Elapsed.TotalSeconds < 2);
        }
    }

    [Fact]
    public void AuthoredMetadataAndTerrainEditsSurviveSerializationWithoutMutatingRunningWorlds()
    {
        LevelDocument document = LevelLoader.Load(Mossy, Content).Document!;
        EntityPlacement zombie = document.Entities.Single(e => e.Id == "z_pocket_1");
        Assert.Equal(new Dungeons.Core.Math.Vec2(0, 1), zombie.Facing); Assert.Contains("pocket", zombie.Tags);
        Assert.Equal(2, zombie.Properties.GetProperty("patrolRadius").GetSingle());
        GameSession session = new(document); Entity runtime = session.World.Entities.Single(e => e.LevelId == "z_pocket_1");
        Assert.Equal(zombie.Facing, runtime.Facing); Assert.Contains("pocket", runtime.Tags);
        string before = document.SourceHash; bool original = document.GetCell(3, 3).Solid;
        document.PaintCell(3, 3, "core:grass", 0, "core:flowers");
        Assert.NotEqual(before, document.SourceHash); Assert.False(document.GetCell(3, 3).Solid); Assert.Equal(original, session.World.Grid[3, 3].Solid);
        LevelLoadResult reloaded = LevelLoader.Load(document.ToJson(), Content); Assert.True(reloaded.Success, string.Join('\n', reloaded.Diagnostics));
        Assert.Equal(document.SourceHash, reloaded.Document!.SourceHash); Assert.False(reloaded.Document.GetCell(3, 3).Solid);
        document.AddEntity(new(Content.Entities["core:food"], new(4.5f, 36.5f), "extra_food"));
        Assert.Contains(LevelLoader.Load(document.ToJson(), Content).Document!.Entities, e => e.Id == "extra_food");
        Assert.DoesNotContain(session.World.Entities, e => e.LevelId == "extra_food");
        Assert.Throws<InvalidOperationException>(() => document.SetCell(3, 3, Cell.Wall));
        Assert.Throws<ArgumentException>(() => document.PaintCell(3, 3, "core:flowers", 0));
        JsonObject source = JsonNode.Parse(Mossy)!.AsObject(); source["environment"] = new JsonObject { ["camera"] = new JsonObject { ["fovDeg"] = 30 } };
        LevelEnvironment environment = LevelLoader.Load(source.ToJsonString(), Content).Document!.Environment;
        Assert.Equal(30, environment.CameraFov); Assert.Equal(45, environment.CameraYaw); Assert.Equal(-50, environment.CameraPitch); Assert.Equal(22, environment.CameraDistance); Assert.Equal(1.5f, environment.CameraLookAhead);
        Assert.True(environment.Json.GetProperty("fog").GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public void StoredSpawnerArenaHasValidatedDeclarativeProperties()
    {
        string json = File.ReadAllText(Path.Combine(Root, "src/Dungeons.Core.Tests/TestLevels/test_spawner_arena/level.json"));
        LevelLoadResult result = LevelLoader.Load(json, Content); Assert.True(result.Success, string.Join('\n', result.Diagnostics));
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error);
        EntityPlacement marker = result.Document!.Entities.Single(e => e.Id == "wave");
        Assert.Equal(6, marker.Properties.GetProperty("count").GetInt32()); Assert.Equal(2, marker.Properties.GetProperty("maxAlive").GetInt32());
        Assert.Equal("arena", marker.Properties.GetProperty("tag").GetString());
    }

    private static void Check(DiagnosticCode code, Action<JsonObject> mutate, bool error = true)
    {
        JsonObject root = JsonNode.Parse(Mossy)!.AsObject(); mutate(root); LevelLoadResult result = LevelLoader.Load(root.ToJsonString(), Content);
        Assert.Contains(result.Diagnostics, d => d.Code == code);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.LVL9999);
        if (error) { Assert.False(result.Success); }
    }
    private static ContentRegistry LoadContent()
    {
        string path = Path.Combine(Root, "game/content");
        string[] files = System.Text.Json.JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(path, "index.json")))!;
        return ContentRegistry.Load(files.Select(relative => (relative, File.ReadAllText(Path.Combine(path, relative)))));
    }
    private static string FindRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "specification.md"))) { directory = directory.Parent; }
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
