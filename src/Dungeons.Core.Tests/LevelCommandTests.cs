using System.Text.Json;
using System.Text.Json.Nodes;
using Dungeons.Core.Level;
using Dungeons.Core.Level.Commands;
using Dungeons.Core.Math;
using Dungeons.Core.Sim;
using Xunit;

namespace Dungeons.Core.Tests;

public sealed class LevelCommandTests
{
    [Theory]
    [InlineData("tile")]
    [InlineData("reference")]
    [InlineData("height")]
    [InlineData("overlay")]
    [InlineData("cell_props")]
    [InlineData("resize")]
    [InlineData("add_entity")]
    [InlineData("remove_entity")]
    [InlineData("remove_index")]
    [InlineData("move_entity")]
    [InlineData("move_index")]
    [InlineData("entity_props")]
    [InlineData("props_index")]
    [InlineData("add_trigger")]
    [InlineData("edit_trigger")]
    [InlineData("trigger_index")]
    [InlineData("remove_trigger")]
    [InlineData("remove_trigger_index")]
    [InlineData("objective")]
    [InlineData("objective_index")]
    [InlineData("remove_objective")]
    [InlineData("environment")]
    [InlineData("remove_environment")]
    [InlineData("metadata")]
    [InlineData("remove_metadata")]
    [InlineData("completion")]
    public void EveryBuiltinAppliesUndoesRedoesAndRestoresHistoryAfterCanonicalReload(string kind)
    {
        LevelDocument document = Create(); string before = LevelSerializer.Write(document), sourceHash = document.SourceHash; var placements = document.Entities;
        ulong initial = new GameSession(document, 73).Hash(); LevelCommand command = Command(kind);
        string macro = LevelCommandSerializer.Write(command); LevelCommand restoredNew = LevelCommandSerializer.Read(macro);
        Assert.Equal(macro, LevelCommandSerializer.Write(restoredNew)); Assert.False(restoredNew.HasHistory);
        restoredNew.Apply(document); Assert.True(restoredNew.IsApplied); Assert.False(document.HasErrors, string.Join('\n', document.Diagnostics));
        string after = LevelSerializer.Write(document); Assert.NotEqual(before, after); string afterSource = document.SourceHash;
        Assert.Same(placements, document.Entities); Assert.Equal(1, document.Revision);
        LevelLoadResult loaded = LevelLoader.Load(after, MissionAssets.Content); Assert.True(loaded.Success);
        Assert.Equal(new GameSession(document, 73).Hash(), new GameSession(loaded.Document!, 73).Hash());
        restoredNew.Undo(document); Assert.Equal(before, LevelSerializer.Write(document)); Assert.Equal(sourceHash, document.SourceHash); Assert.Equal(initial, new GameSession(document, 73).Hash());
        restoredNew.Apply(document); Assert.Equal(after, LevelSerializer.Write(document)); Assert.Equal(afterSource, document.SourceHash);
        string history = LevelCommandSerializer.Write(restoredNew); LevelCommand restoredApplied = LevelCommandSerializer.Read(history);
        Assert.Equal(history, LevelCommandSerializer.Write(restoredApplied)); Assert.True(restoredApplied.IsApplied);
        LevelDocument saved = LevelLoader.Load(after, MissionAssets.Content).Document!;
        restoredApplied.Undo(saved); Assert.Equal(before, LevelSerializer.Write(saved)); Assert.Equal(sourceHash, saved.SourceHash);
        LevelCommand undone = LevelCommandSerializer.Read(LevelCommandSerializer.Write(restoredApplied)); Assert.False(undone.IsApplied); Assert.True(undone.HasHistory);
        undone.Apply(saved); Assert.Equal(after, LevelSerializer.Write(saved));
    }

    [Fact]
    public void EditingRetainsSemanticErrorsWhileNormalLoadsAndPlayStayStrict()
    {
        LevelDocument document = Create(); string before = LevelSerializer.Write(document); var remove = new RemoveEntity("exit"); remove.Apply(document);
        Assert.True(document.HasErrors); Assert.Contains(document.Diagnostics, d => d.Code == DiagnosticCode.LVL1021);
        Assert.DoesNotContain(document.Entities, entity => entity.Id == "exit"); Assert.Contains(document.Entities, entity => entity.Id == "z");
        Assert.Throws<ArgumentException>(() => new GameSession(document)); Assert.Throws<ArgumentException>(() => GameSession.Create(document, MissionAssets.Content, 1));
        string invalid = LevelSerializer.Write(document); Assert.Null(LevelLoader.Load(invalid, MissionAssets.Content).Document);
        LevelLoadResult editing = LevelLoader.LoadForEditing(invalid, MissionAssets.Content); Assert.NotNull(editing.Document); Assert.False(editing.Success); Assert.True(editing.Document!.HasErrors);
        remove.Undo(document); Assert.False(document.HasErrors); Assert.Equal(before, LevelSerializer.Write(document)); _ = new GameSession(document);
        var move = new MoveEntity("z", new(-4, 90)); move.Apply(document); Assert.True(document.HasErrors);
        Assert.Equal(-4, document.ToJsonObject()["entities"]![3]!["x"]!.GetValue<float>()); Assert.DoesNotContain(document.Entities, entity => entity.Id == "z");
        new MoveEntity("z", new(6.5f, 6.5f)).Apply(document); Assert.False(document.HasErrors); Assert.Contains(document.Entities, entity => entity.Id == "z");
    }

    [Fact]
    public void TerrainEditsRebindCellFlagsHeightSurfaceAndPropertiesWithoutTouchingExistingSessions()
    {
        LevelDocument document = Create(); var session = new GameSession(document, 4); ulong hash = session.Hash();
        new SetHeight(5, 5, 1).Apply(document); Assert.Equal(1, document.GetCell(5, 5).Height); Assert.Equal(0, session.World.Grid.HeightAt(new(5.5f, 5.5f)));
        new SetTile(5, 5, "core:stone_floor").Apply(document); Assert.Equal(1, document.GetCell(5, 5).Height);
        new SetOverlay(5, 5, "core:tall_grass").Apply(document); new SetCellProps(5, 5, new() { ["future/value~"] = "preserve" }).Apply(document);
        Assert.Equal("preserve", document.ToJsonObject()["cells"]![0]!["props"]!["future/value~"]!.GetValue<string>());
        Assert.True(document.HasErrors); Assert.Throws<ArgumentException>(() => new GameSession(document));
        new SetCellProps(5, 5, new()).Apply(document); Assert.False(document.HasErrors);
        var next = new GameSession(document, 4); Assert.Equal("core:stone_floor", next.World.SurfaceAt(new(5, 5))); Assert.Equal(hash, session.Hash());
        Assert.Empty(document.ToJsonObject()["cells"]!.AsArray());
        new LevelCommandBatch([new SetHeight(5, 4, 1), new SetCellProps(4, 4, new() { ["dir"] = "east" })]).Apply(document);
        Assert.False(document.HasErrors); Assert.Equal(StairsDirection.East, document.GetCell(4, 4).StairsDir);
    }

    [Fact]
    public void CompositeOverlayClearingAndTileReplacementDoNotLeaveHiddenDecorationsOrOverrides()
    {
        LevelDocument document = Create(); new SetTile(4, 4, new JsonObject { ["tile"] = "core:grass", ["overlay"] = "core:tall_grass" }).Apply(document);
        string composite = LevelSerializer.Write(document); var clear = new SetOverlay(4, 4, null); clear.Apply(document);
        JsonObject source = document.ToJsonObject(); char symbol = source["layers"]!["floor"]![4]!.GetValue<string>()[4];
        Assert.Equal("core:grass", source["palette"]![symbol.ToString()]!.GetValue<string>()); clear.Undo(document); Assert.Equal(composite, LevelSerializer.Write(document));
        new SetCellProps(4, 4, new() { ["future"] = 1 }).Apply(document); new SetOverlay(4, 4, "core:flowers").Apply(document);
        new SetTile(4, 4, "core:stone_floor").Apply(document); source = document.ToJsonObject(); Assert.Equal(' ', source["layers"]!["overlay"]![4]!.GetValue<string>()[4]); Assert.Empty(source["cells"]!.AsArray());
    }

    [Fact]
    public void InvalidStructureAndFailedBatchesRollBackAllDerivedAndAuthoredState()
    {
        LevelDocument document = Create(); string before = LevelSerializer.Write(document), identity = document.SourceHash; var entities = document.Entities; var environment = document.Environment;
        var bad = new LevelCommandBatch([new SetHeight(5, 5, 2), new MoveEntity("z", new(7, 7)), new SetMetadata("version", JsonValue.Create("broken"))]);
        LevelEditException error = Assert.Throws<LevelEditException>(() => bad.Apply(document)); Assert.Contains(error.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.False(bad.IsApplied); Assert.False(bad.HasHistory); Assert.Equal(before, LevelSerializer.Write(document)); Assert.Equal(identity, document.SourceHash); Assert.Equal(0, document.Revision);
        Assert.Same(entities, document.Entities); Assert.Same(environment, document.Environment); Assert.Equal(0, document.GetCell(5, 5).Height);
        Assert.Throws<LevelEditException>(() => new SetHeight(100, 100, 1).Apply(document)); Assert.Equal(before, LevelSerializer.Write(document));
    }

    [Fact]
    public void BatchHasOneUndoBoundaryAndPaletteAdditionHistoryStaysCompact()
    {
        LevelDocument document = Create(); string before = LevelSerializer.Write(document);
        var batch = new LevelCommandBatch(Enumerable.Range(0, 10).Select(x => (ILevelCommand)new SetTile(x, 5, "core:stone_floor")));
        batch.Apply(document); Assert.Equal(1, document.Revision); Assert.False(document.HasErrors);
        JsonObject record = JsonNode.Parse(LevelCommandSerializer.Write(batch))!.AsObject(); Assert.True(record["changes"]!.AsArray().Count <= 2); // One palette entry and one row.
        batch.Undo(document); Assert.Equal(before, LevelSerializer.Write(document)); batch.Apply(document);
        var add = new AddEntity(Entity("core:torch", "one_more", 8.5, 3.5)); add.Apply(document);
        record = JsonNode.Parse(LevelCommandSerializer.Write(add))!.AsObject(); JsonObject splice = Assert.Single(record["changes"]!.AsArray())!.AsObject();
        Assert.Equal("splice", splice["kind"]!.GetValue<string>()); Assert.Empty(splice["before"]!.AsArray()); Assert.Single(splice["after"]!.AsArray()); Assert.True(LevelCommandSerializer.Write(add).Length < 2000);
    }

    [Fact]
    public void HistoryRejectsOutOfOrderUndoWrongDocumentsAndTamperedDeltasWithoutMutation()
    {
        LevelDocument document = Create(); var first = new SetHeight(4, 4, 1); var second = new SetHeight(5, 5, 1); first.Apply(document); second.Apply(document);
        string both = LevelSerializer.Write(document); Assert.Throws<LevelEditException>(() => first.Undo(document)); Assert.Equal(both, LevelSerializer.Write(document)); Assert.True(first.IsApplied);
        Assert.Throws<LevelEditException>(() => first.Apply(document)); second.Undo(document); first.Undo(document); Assert.Throws<LevelEditException>(() => first.Undo(document));
        first.Apply(document); JsonObject envelope = JsonNode.Parse(LevelCommandSerializer.Write(first))!.AsObject();
        envelope["changes"]![0]!["before"] = "bad row"; LevelCommand forged = LevelCommandSerializer.Read(envelope.ToJsonString()); string current = LevelSerializer.Write(document);
        Assert.Throws<LevelEditException>(() => forged.Undo(document)); Assert.Equal(current, LevelSerializer.Write(document)); Assert.True(forged.IsApplied);
        var other = Create(); new SetMetadata("name", JsonValue.Create("Different")).Apply(other); Assert.Throws<LevelEditException>(() => first.Undo(other));
        envelope = JsonNode.Parse(LevelCommandSerializer.Write(first))!.AsObject(); envelope["contentHash"] = new string('0', 64);
        Assert.Throws<LevelEditException>(() => LevelCommandSerializer.Read(envelope.ToJsonString()).Undo(document)); Assert.Equal(current, LevelSerializer.Write(document));
    }

    [Fact]
    public void NoOpsPreserveRevisionExactSourceIdentityAndSerializedUndoStillWorks()
    {
        LevelDocument document = Create(); string source = document.SourceHash, before = LevelSerializer.Write(document); var noOp = new SetHeight(4, 4, 0);
        noOp.Apply(document); Assert.True(noOp.IsApplied); Assert.Equal(0, document.Revision); Assert.Equal(source, document.SourceHash);
        LevelCommand restored = LevelCommandSerializer.Read(LevelCommandSerializer.Write(noOp)); restored.Undo(document); restored.Apply(document);
        Assert.Equal(before, LevelSerializer.Write(document)); Assert.Equal(source, document.SourceHash); Assert.Equal(0, document.Revision);
    }

    [Theory]
    [InlineData(ResizeAnchor.NorthWest, 0, 0)]
    [InlineData(ResizeAnchor.North, 2, 0)]
    [InlineData(ResizeAnchor.NorthEast, 4, 0)]
    [InlineData(ResizeAnchor.West, 0, 1)]
    [InlineData(ResizeAnchor.Center, 2, 1)]
    [InlineData(ResizeAnchor.East, 4, 1)]
    [InlineData(ResizeAnchor.SouthWest, 0, 2)]
    [InlineData(ResizeAnchor.South, 2, 2)]
    [InlineData(ResizeAnchor.SouthEast, 4, 2)]
    public void ResizeAnchorsShiftLayersEntitiesAreasAndCameraBounds(ResizeAnchor anchor, int dx, int dz)
    {
        LevelDocument document = Create(); new SetHeight(5, 5, 1).Apply(document); new SetCellProps(4, 4, new() { ["dir"] = "north" }).Apply(document);
        string before = LevelSerializer.Write(document); var resize = new Resize(16, 14, anchor); resize.Apply(document);
        Assert.Equal(16, document.Width); Assert.Equal(14, document.Height); Assert.Equal(1, document.GetCell(5 + dx, 5 + dz).Height);
        Assert.Equal(new Vec2(1.5f + dx, 10.5f + dz), document.Entities.Single(e => e.Id == "spawn").Position);
        JsonObject source = document.ToJsonObject(); Assert.Equal(3 + dx, source["triggers"]![0]!["event"]!["area"]!["x"]!.GetValue<double>());
        Assert.Equal(3 + dz, source["triggers"]![0]!["event"]!["area"]!["z"]!.GetValue<double>());
        Assert.Equal(4 + dx, source["cells"]![0]!["x"]!.GetValue<int>()); Assert.Equal(1 + dx, source["environment"]!["camera"]!["bounds"]!["x"]!.GetValue<double>());
        Assert.False(document.HasErrors); resize.Undo(document); Assert.Equal(before, LevelSerializer.Write(document));
    }

    [Fact]
    public void CroppingRetainsOutsideEntitiesForRepairAndUndoRestoresDroppedCellOverrides()
    {
        LevelDocument document = Create(); new SetCellProps(11, 11, new() { ["dir"] = "north" }).Apply(document); string before = LevelSerializer.Write(document);
        var resize = new Resize(8, 8, ResizeAnchor.Center); resize.Apply(document); Assert.True(document.HasErrors);
        Assert.Equal(6, document.ToJsonObject()["entities"]!.AsArray().Count); Assert.Empty(document.ToJsonObject()["cells"]!.AsArray()); Assert.Equal(8, document.Width);
        Assert.Throws<ArgumentException>(() => new GameSession(document)); resize.Undo(document); Assert.Equal(before, LevelSerializer.Write(document)); Assert.False(document.HasErrors);
    }

    [Fact]
    public void UndoRestoresMetadataIdAndMissionBindingsAndLegacyHelpersRefreshEditingDiagnostics()
    {
        LevelDocument document = Create(); var rename = new SetMetadata(new() { ["id"] = "test.renamed", ["name"] = "New" }); rename.Apply(document);
        Assert.Equal("test.renamed", document.Id); Assert.Equal("New", document.Name); rename.Undo(document); Assert.Equal("test.editor", document.Id);
        var remove = new SetObjective("goal", null); remove.Apply(document); Assert.False(document.HasErrors); Assert.Empty(new GameSession(document).World.Mission.Objectives); remove.Undo(document);
        Assert.Single(new GameSession(document).World.Mission.Objectives);
        var wall = MissionAssets.Content.Tiles.Values.First(tile => tile.Cell.Solid && !tile.Overlay); document.PaintCell(6, 6, wall.Id, 0); Assert.True(document.HasErrors);
        document.PaintCell(6, 6, "core:grass", 0); Assert.False(document.HasErrors); document.AddEntity(new(MissionAssets.Content.Entities["core:torch"], new(7, 7), "legacy_torch")); Assert.Contains(document.Entities, e => e.Id == "legacy_torch");
    }

    [Fact]
    public void DuplicateIdsCanBeRepairedByIndexAndMissingOrAmbiguousSelectionsAreRejected()
    {
        LevelDocument document = Create(); new AddEntity(Entity("core:zombie", "z", 8, 8)).Apply(document); Assert.True(document.HasErrors);
        Assert.Throws<LevelEditException>(() => new RemoveEntity("z").Apply(document)); new RemoveEntity(6).Apply(document); Assert.False(document.HasErrors);
        Assert.Throws<LevelEditException>(() => new RemoveEntity("missing").Apply(document)); Assert.Throws<LevelEditException>(() => new EditTrigger(5, Trigger("other")).Apply(document));
        new SetEntityProps(4, new() { ["width"] = 0 }).Apply(document); Assert.True(document.HasErrors);
        new SetEntityProps(4, new() { ["width"] = 2, ["state"] = "open" }).Apply(document); Assert.False(document.HasErrors);
    }

    [Fact]
    public void MalformedLayersAreRepairableByResizeAndMalformedStructureHasNoEditingDocument()
    {
        JsonObject source = Create().ToJsonObject(); source["layers"]!["floor"]![0] = "........";
        LevelLoadResult editing = LevelLoader.LoadForEditing(source.ToJsonString(), MissionAssets.Content); Assert.NotNull(editing.Document); Assert.True(editing.Document!.HasErrors);
        Assert.Throws<LevelEditException>(() => new SetTile(2, 0, "core:grass").Apply(editing.Document)); new Resize(12, 12).Apply(editing.Document); Assert.False(editing.Document.HasErrors);
        source["layers"]!["floor"] = 17; Assert.Null(LevelLoader.LoadForEditing(source.ToJsonString(), MissionAssets.Content).Document);
    }

    [Fact]
    public void InputsAreCopiedAndMalformedOrUnknownHistoryCannotExecute()
    {
        var properties = new JsonObject { ["future"] = "original" }; var command = new SetCellProps(4, 4, properties); properties["future"] = "changed";
        LevelDocument document = Create(); command.Apply(document); Assert.Equal("original", document.ToJsonObject()["cells"]![0]!["props"]!["future"]!.GetValue<string>());
        Assert.Throws<LevelEditException>(() => new SetHeight(0, 0, 1).Apply(new(8, 8))); Assert.Throws<ArgumentNullException>(() => new SetHeight(0, 0, 1).Apply(null!));
        Assert.Throws<JsonException>(() => new SetHeight(0, 0, 16)); Assert.Throws<JsonException>(() => new Resize(7, 8)); Assert.Throws<JsonException>(() => new SetMetadata("size", new JsonObject()));
        Assert.Throws<ArgumentException>(() => new LevelCommandBatch([command])); Assert.Throws<JsonException>(() => new LevelCommandBatch([]));
        Assert.Throws<JsonException>(() => LevelCommandSerializer.Read("{\"version\":1,\"version\":1}")); Assert.Throws<JsonException>(() => LevelCommandSerializer.Read("[]"));
        foreach (string field in new[] { "version", "state", "operation", "extra" })
        {
            JsonObject record = JsonNode.Parse(LevelCommandSerializer.Write(new SetHeight(4, 4, 1)))!.AsObject();
            record[field] = field == "operation" ? new JsonObject { ["type"] = "unknown" } : JsonValue.Create("bad");
            Assert.Throws<JsonException>(() => LevelCommandSerializer.Read(record.ToJsonString()));
        }
    }

    internal static LevelDocument Create()
    {
        JsonObject source = new()
        {
            ["formatVersion"] = 1, ["id"] = "test.editor", ["name"] = "Editor", ["author"] = "Author", ["version"] = "1.0.0", ["size"] = new JsonObject { ["width"] = 12, ["height"] = 12 },
            ["palette"] = new JsonObject { ["."] = "core:grass" }, ["layers"] = new JsonObject { ["floor"] = new JsonArray(Enumerable.Range(0, 12).Select(_ => (JsonNode?)JsonValue.Create(new string('.', 12))).ToArray()), ["heights"] = new JsonArray(Enumerable.Range(0, 12).Select(_ => (JsonNode?)JsonValue.Create(new string('0', 12))).ToArray()) },
            ["entities"] = new JsonArray(Entity("core:player_spawn", "spawn", 1.5, 10.5), Entity("core:checkpoint", "cp", 2.5, 10.5), Entity("core:exit", "exit", 10.5, 1.5), Entity("core:zombie", "z", 6.5, 6.5), Entity("core:gate", "gate", 5.5, 4.5, new() { ["state"] = "open" }), Entity("core:torch", null, 1.5, 1.5)),
            ["triggers"] = new JsonArray(new JsonObject { ["id"] = "enter", ["event"] = new JsonObject { ["type"] = "enter", ["area"] = new JsonObject { ["x"] = 3, ["z"] = 3, ["w"] = 2, ["h"] = 2 } }, ["actions"] = new JsonArray(new JsonObject { ["type"] = "message", ["text"] = "Enter" }) }),
            ["objectives"] = new JsonArray(new JsonObject { ["id"] = "goal", ["text"] = "Reach exit", ["type"] = "reach", ["target"] = "exit", ["initialState"] = "active" }),
            ["completion"] = new JsonObject { ["type"] = "exit", ["exit"] = "exit" }, ["environment"] = new JsonObject { ["camera"] = new JsonObject { ["bounds"] = new JsonObject { ["x"] = 1, ["z"] = 1, ["w"] = 10, ["h"] = 10 } } }
        };
        source["palette"]!["s"] = "core:stairs";
        foreach (var (x, z) in new[] { (4, 4), (11, 11) })
        {
            char[] floor = source["layers"]!["floor"]![z]!.GetValue<string>().ToCharArray(); floor[x] = 's'; source["layers"]!["floor"]![z] = new string(floor);
            char[] upper = source["layers"]!["heights"]![z - 1]!.GetValue<string>().ToCharArray(); upper[x] = '1'; source["layers"]!["heights"]![z - 1] = new string(upper);
        }
        LevelLoadResult loaded = LevelLoader.Load(source.ToJsonString(), MissionAssets.Content); Assert.True(loaded.Success, string.Join('\n', loaded.Diagnostics)); return loaded.Document!;
    }
    private static JsonObject Entity(string type, string? id, double x, double z, JsonObject? properties = null)
    { var entity = new JsonObject { ["type"] = type, ["x"] = x, ["z"] = z }; if (id is not null) { entity["id"] = id; } if (properties is not null) { entity["props"] = properties; } return entity; }
    private static JsonObject Trigger(string id) => new() { ["id"] = id, ["event"] = new JsonObject { ["type"] = "timer", ["seconds"] = 1 }, ["actions"] = new JsonArray(new JsonObject { ["type"] = "message", ["text"] = "Changed" }) };
    private static LevelCommand Command(string kind) => kind switch
    {
        "tile" => new SetTile(4, 4, "core:stone_floor"), "reference" => new SetTile(4, 4, new JsonObject { ["tile"] = "core:stairs", ["props"] = new JsonObject { ["dir"] = "north" }, ["overlay"] = "core:tall_grass" }),
        "height" => new SetHeight(5, 5, 1), "overlay" => new SetOverlay(4, 4, "core:tall_grass"), "cell_props" => new SetCellProps(4, 4, new() { ["dir"] = "north" }), "resize" => new Resize(16, 14, ResizeAnchor.Center),
        "add_entity" => new AddEntity(Entity("core:skeleton", "new", 8.5, 8.5)), "remove_entity" => new RemoveEntity("z"), "remove_index" => new RemoveEntity(5),
        "move_entity" => new MoveEntity("z", new(7.5f, 6.5f), Vec2.North), "move_index" => new MoveEntity(3, new(7.5f, 6.5f)),
        "entity_props" => new SetEntityProps("gate", new() { ["state"] = "open", ["width"] = 3 }), "props_index" => new SetEntityProps(4, new() { ["state"] = "open", ["width"] = 3 }),
        "add_trigger" => new AddTrigger(Trigger("new")), "edit_trigger" => new EditTrigger("enter", Trigger("enter")), "trigger_index" => new EditTrigger(0, Trigger("enter")),
        "remove_trigger" => new EditTrigger("enter", null), "remove_trigger_index" => new EditTrigger(0, null),
        "objective" => new SetObjective(new() { ["id"] = "second", ["text"] = "Second", ["type"] = "flag" }), "objective_index" => new SetObjective(0, new() { ["id"] = "goal", ["text"] = "Changed goal", ["type"] = "reach", ["target"] = "exit", ["initialState"] = "active" }),
        "remove_objective" => new SetObjective("goal", null), "remove_environment" => new SetEnvironment(null), "remove_metadata" => new SetMetadata(new JsonObject(), ["author"]),
        "environment" => new SetEnvironment(new() { ["camera"] = new JsonObject { ["fovDeg"] = 30 } }), "metadata" => new SetMetadata(new() { ["name"] = new JsonObject { ["en"] = "Changed", ["pl"] = "Zmienione" }, ["author"] = "Editor", ["seed"] = 77 }),
        "completion" => new SetMetadata("completion", new JsonObject { ["type"] = "exit", ["exit"] = "exit", ["requires"] = new JsonArray("goal") }), _ => throw new ArgumentException(kind)
    };
}
