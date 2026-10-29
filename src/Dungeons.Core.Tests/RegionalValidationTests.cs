using System.Diagnostics;
using System.Text.Json.Nodes;
using Dungeons.Core.Level;
using Dungeons.Core.Level.Commands;
using Dungeons.Core.Math;
using Dungeons.Core.Sim;
using Xunit;
using Xunit.Abstractions;

namespace Dungeons.Core.Tests;

[Collection("Performance")]
public sealed class RegionalValidationTests(ITestOutputHelper output)
{
    [Fact]
    public void DirectValidationMatchesLoadingAcrossDirtySourceFailuresWithoutChangingTheDocument()
    {
        Action<JsonObject>[] mutations = [
            _ => { }, root => root["formatVersion"] = 7, root => root["size"]!["width"] = 14,
            root => SetChar(root, "floor", 7, 2, '?'), root => SetChar(root, "heights", 7, 2, 'g'),
            root => root["palette"]!["."] = "core:missing", root => root["palette"]!["."] = "core:tall_grass",
            root => root["palette"]!["."] = new JsonObject { ["tile"] = "core:grass", ["overlay"] = "core:stone_floor" },
            root => root["cells"] = new JsonArray(new JsonObject { ["x"] = 4, ["z"] = 4, ["props"] = new JsonObject { ["dir"] = "bad" } }),
            root => root["cells"] = new JsonArray(new JsonObject { ["x"] = 99, ["z"] = 1, ["props"] = new JsonObject() }),
            root => root["cells"] = new JsonArray(new JsonObject { ["x"] = 1e20, ["z"] = 1, ["props"] = new JsonObject() }),
            root => root["entities"]![0]!["type"] = "core:missing", root => root["entities"]![0]!["x"] = -4,
            root => root["entities"]![3]!["id"] = "exit", root => root["entities"]![4]!["props"] = new JsonObject { ["width"] = 0 },
            root => root["objectives"]![0] = new JsonObject { ["id"] = "goal", ["text"] = "Wait", ["type"] = "survive", ["seconds"] = 0 },
            root => root["triggers"]![0]!["actions"] = new JsonArray(new JsonObject { ["type"] = "camera_focus" }),
            root => root["completion"]!["exit"] = "missing", root => root["name"] = null,
            root => root["future"] = new JsonObject { ["unknown"] = "preserve" }
        ];
        foreach (Action<JsonObject> mutate in mutations)
        {
            LevelDocument document = LevelCommandTests.Create(); var session = new GameSession(document, 41); ulong hash = session.Hash();
            JsonObject source = document.ToJsonObject(); mutate(source); document.Source = source;
            string before = document.ToJson(), identity = document.SourceHash; long revision = document.Revision; var placements = document.Entities;
            var actual = LevelValidator.Validate(document);
            var loaded = LevelLoader.Load(before, MissionAssets.Content).Diagnostics;
            Assert.Equal(Describe(loaded), Describe(actual)); Assert.Equal(before, document.ToJson()); Assert.Equal(identity, document.SourceHash); Assert.Equal(revision, document.Revision);
            Assert.Same(placements, document.Entities); Assert.Equal(StairsDirection.North, document.GetCell(4, 4).StairsDir); Assert.Equal(hash, session.Hash());
            Assert.DoesNotContain(actual, d => d.Code == DiagnosticCode.LVL9999); Assert.All(actual, d => Assert.True(d.JsonPath.Length == 0 || d.JsonPath.StartsWith('/')));
        }
    }

    [Fact]
    public void ChangingTheUpperCellRevalidatesTheNeighborStairButNotDistantTerrain()
    {
        LevelDocument document = LevelCommandTests.Create(); var edit = new SetHeight(4, 3, 0); edit.Apply(document); string source = document.ToJson(); ulong revision = (ulong)document.Revision;
        var local = LevelValidator.ValidateCells(document, new(4, 3, 1, 1));
        Assert.Contains(local, d => d.Code == DiagnosticCode.LVL1006 && d.Cell == new IntVec2(4, 4));
        Assert.DoesNotContain(LevelValidator.ValidateCells(document, new(1, 8, 1, 1)), d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal(source, document.ToJson()); Assert.Equal(revision, (ulong)document.Revision);
        edit.Undo(document); Assert.DoesNotContain(LevelValidator.ValidateCells(document, new(4, 3, 1, 1)), d => d.Severity == DiagnosticSeverity.Error);
        edit.Apply(document); Assert.Contains(LevelValidator.ValidateCells(document, new(4, 3, 1, 1)), d => d.Code == DiagnosticCode.LVL1006);
    }

    [Fact]
    public void RegionalChecksUseCurrentSourceAndKeepUnrelatedCellFailuresOutOfTheResult()
    {
        LevelDocument document = LevelCommandTests.Create(); SetChar(document.Source!, "floor", 10, 4, '?'); SetChar(document.Source!, "heights", 7, 2, 'g');
        Assert.DoesNotContain(LevelValidator.ValidateCells(document, new(1, 8, 1, 1)), d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains(LevelValidator.ValidateCells(document, new(10, 4, 1, 1)), d => d.Code == DiagnosticCode.LVL1002 && d.Cell == new IntVec2(10, 4));
        Assert.Contains(LevelValidator.ValidateCells(document, new(7, 2, 1, 1)), d => d.Code == DiagnosticCode.LVL1005 && d.Cell == new IntVec2(7, 2));
        Assert.Equal(0, document.GetCell(7, 2).Height); Assert.True(document.GetCell(10, 4).Walkable);
        Assert.Empty(LevelValidator.ValidateCells(document, new(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue)));
        Assert.Empty(LevelValidator.ValidateCells(document, default));
    }

    [Fact]
    public void PropertyAndOverlayDiagnosticsHaveCellHighlightsAndTheOverrideIndexCannotGoStale()
    {
        LevelDocument document = LevelCommandTests.Create(); var bad = new SetCellProps(4, 4, new() { ["dir"] = "bad" }); bad.Apply(document);
        Assert.Contains(LevelValidator.ValidateCells(document, new(4, 4, 1, 1)), d => d.Code == DiagnosticCode.LVL1003 && d.Cell == new IntVec2(4, 4) && d.JsonPath.StartsWith("/cells/"));
        bad.Undo(document); Assert.DoesNotContain(LevelValidator.ValidateCells(document, new(4, 4, 1, 1)), d => d.Severity == DiagnosticSeverity.Error);
        new SetTile(5, 5, new JsonObject { ["tile"] = "core:grass", ["overlay"] = "core:stone_floor" }).Apply(document);
        Assert.Contains(LevelValidator.ValidateCells(document, new(5, 5, 1, 1)), d => d.Code == DiagnosticCode.LVL1004 && d.Cell == new IntVec2(5, 5));
        JsonObject root = LevelCommandTests.Create().ToJsonObject(); root["cells"] = new JsonArray(new JsonObject { ["x"] = 4, ["z"] = 4, ["props"] = new JsonObject { ["dir"] = "north" } });
        document = LevelLoader.Load(root.ToJsonString(), MissionAssets.Content).Document!;
        _ = LevelValidator.ValidateCells(document, new(4, 4, 1, 1)); document.PaintCell(4, 4, "core:grass", 0);
        Assert.DoesNotContain(LevelValidator.ValidateCells(document, new(4, 4, 1, 1)), d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void SharedIndexAndPaletteErrorsRemainVisibleAndHugeOverrideCoordinatesAreRejected()
    {
        LevelDocument document = LevelCommandTests.Create(); document.Source!["cells"] = new JsonArray(new JsonObject { ["x"] = 1e20, ["z"] = 0, ["props"] = new JsonObject() });
        Assert.Contains(LevelValidator.Validate(document), d => d.Code == DiagnosticCode.LVL0003 && d.JsonPath == "/cells/0");
        Assert.Contains(LevelValidator.ValidateCells(document, new(1, 1, 1, 1)), d => d.Code == DiagnosticCode.LVL0003 && d.JsonPath == "/cells/0");
        document.Source["palette"]!["!"] = "core:missing";
        Assert.Contains(LevelValidator.ValidateCells(document, new(1, 1, 1, 1)), d => d.Code == DiagnosticCode.LVL1003 && d.JsonPath == "/palette/!");
        document.Source["layers"]!["floor"]![0] = "........";
        Assert.Contains(LevelValidator.ValidateCells(document, new(1, 1, 1, 1)), d => d.Code == DiagnosticCode.LVL1001);
    }

    [Fact]
    public void RectanglesClipSafelyAndUnboundOrInvalidArgumentsHaveExplicitErrors()
    {
        var rectangle = new CellRect(-2, -2, 5, 5); Assert.True(rectangle.Contains(0, 0)); Assert.False(rectangle.Contains(3, 0));
        Assert.True(new CellRect(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue).Contains(int.MaxValue, int.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CellRect(0, 0, -1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CellRect(0, 0, 1, -1));
        Assert.DoesNotContain(LevelValidator.ValidateCells(LevelCommandTests.Create(), rectangle), d => d.Severity == DiagnosticSeverity.Error);
        Assert.Throws<ArgumentNullException>(() => LevelValidator.Validate(null!, MissionAssets.Content));
        Assert.Throws<ArgumentNullException>(() => LevelValidator.Validate(null!));
        Assert.Throws<InvalidOperationException>(() => LevelValidator.Validate(new(8, 8), MissionAssets.Content));
        Assert.Throws<ArgumentNullException>(() => LevelValidator.ValidateCells(null!, default, MissionAssets.Content));
        Assert.Throws<ArgumentNullException>(() => LevelValidator.ValidateCells(null!, default));
        Assert.Throws<InvalidOperationException>(() => LevelValidator.ValidateCells(new(8, 8), default, MissionAssets.Content));
    }

    [Fact, Trait("Category", "Performance")]
    public void Full256ValidationWithSixtyFourMonstersStaysBelowFiftyMilliseconds()
    {
        LevelDocument document = Large();
        for (int i = 0; i < 4; i++) { Assert.DoesNotContain(LevelValidator.Validate(document), d => d.Severity == DiagnosticSeverity.Error); }
        for (int i = 0; i < 8; i++)
        { var timer = Stopwatch.StartNew(); var diagnostics = LevelValidator.Validate(document); timer.Stop(); output.WriteLine($"Full validation: {timer.Elapsed.TotalMilliseconds:F3} ms"); Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error); Assert.True(timer.Elapsed.TotalMilliseconds <= 50, $"Full validation took {timer.Elapsed.TotalMilliseconds:F3} ms."); }
    }

    [Fact, Trait("Category", "Performance")]
    public void Disconnected256TargetsShareExhaustiveSearchAndStayBelowFiftyMilliseconds()
    {
        JsonObject root = Large().ToJsonObject(); root["palette"]!["#"] = "core:stone_wall";
        for (int z = 0; z < 256; z++) { SetChar(root, "floor", 128, z, '#'); }
        for (int i = 3; i < root["entities"]!.AsArray().Count; i++) { root["entities"]![i]!["x"] = 140.5 + (i - 3) % 8 * 8; }
        LevelDocument document = LevelLoader.LoadForEditing(root.ToJsonString(), MissionAssets.Content).Document!;
        for (int i = 0; i < 4; i++) { _ = LevelValidator.Validate(document); }
        var timer = Stopwatch.StartNew(); var diagnostics = LevelValidator.Validate(document); timer.Stop();
        output.WriteLine($"Disconnected validation: {timer.Elapsed.TotalMilliseconds:F3} ms");
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.LVL1031); Assert.Equal(64, diagnostics.Count(d => d.Code == DiagnosticCode.LVL1014));
        Assert.True(timer.Elapsed.TotalMilliseconds <= 50, $"Disconnected validation took {timer.Elapsed.TotalMilliseconds:F3} ms.");
    }

    [Fact, Trait("Category", "Performance")]
    public void ARegionalCheckDoesNotRescanSixtyFiveThousandCellOverrides()
    {
        JsonObject root = Large().ToJsonObject(); var cells = new JsonArray();
        for (int z = 0; z < 256; z++) { for (int x = 0; x < 256; x++) { cells.Add(new JsonObject { ["x"] = x, ["z"] = z, ["props"] = new JsonObject() }); } }
        root["cells"] = cells; LevelLoadResult loaded = LevelLoader.Load(root.ToJsonString(), MissionAssets.Content); Assert.True(loaded.Success); LevelDocument document = loaded.Document!;
        // Keep the large fixture's temporary parse/clone garbage outside this
        // warmed incremental-work measurement, as in the full-validation probes.
        for (int i = 0; i < 4; i++) { _ = LevelValidator.ValidateCells(document, new(120, 120, 1, 1)); }
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        for (int i = 0; i < 8; i++)
        { var timer = Stopwatch.StartNew(); var diagnostics = LevelValidator.ValidateCells(document, new(120 + i, 120, 1, 1)); timer.Stop(); output.WriteLine($"Dense-override regional validation: {timer.Elapsed.TotalMilliseconds:F3} ms"); Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error); Assert.True(timer.Elapsed.TotalMilliseconds < 16, $"Regional validation took {timer.Elapsed.TotalMilliseconds:F3} ms."); }
    }
    private static string[] Describe(IReadOnlyList<Diagnostic> diagnostics) => diagnostics.Select(d => $"{d.Severity}|{d.Code}|{d.JsonPath}|{d.Cell}|{d.Message}").ToArray();
    private static void SetChar(JsonObject root, string layer, int x, int z, char value)
    { char[] row = root["layers"]![layer]![z]!.GetValue<string>().ToCharArray(); row[x] = value; root["layers"]![layer]![z] = new string(row); }
    private static LevelDocument Large()
    {
        JsonObject root = LevelCommandTests.Create().ToJsonObject(); root["size"] = new JsonObject { ["width"] = 256, ["height"] = 256 }; root["palette"] = new JsonObject { ["."] = "core:grass" };
        root["layers"] = new JsonObject { ["floor"] = new JsonArray(Enumerable.Range(0, 256).Select(_ => (JsonNode?)JsonValue.Create(new string('.', 256))).ToArray()), ["heights"] = new JsonArray(Enumerable.Range(0, 256).Select(_ => (JsonNode?)JsonValue.Create(new string('0', 256))).ToArray()) };
        var entities = new JsonArray(JsonNode.Parse("""{"id":"spawn","type":"core:player_spawn","x":4.5,"z":4.5}"""), JsonNode.Parse("""{"id":"exit","type":"core:exit","x":250.5,"z":250.5}"""), JsonNode.Parse("""{"type":"core:checkpoint","x":6.5,"z":6.5}"""));
        for (int i = 0; i < 64; i++) { entities.Add(new JsonObject { ["type"] = "core:zombie", ["x"] = 8.5 + i % 8 * 8, ["z"] = 8.5 + i / 8 * 8 }); }
        root["entities"] = entities; root["triggers"] = new JsonArray(); root["objectives"] = new JsonArray(); root["completion"] = new JsonObject { ["type"] = "exit", ["exit"] = "exit" };
        LevelLoadResult loaded = LevelLoader.Load(root.ToJsonString(), MissionAssets.Content); Assert.True(loaded.Success); return loaded.Document!;
    }
}
