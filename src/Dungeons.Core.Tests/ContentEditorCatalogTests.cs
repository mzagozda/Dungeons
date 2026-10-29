using System.Globalization;
using System.Text.Json.Nodes;
using Dungeons.Core.Content;
using Xunit;

namespace Dungeons.Core.Tests;

public sealed class ContentEditorCatalogTests
{
    [Fact]
    public void ArbitraryInheritedDefinitionsSupplyPaletteHintsAndPropertyInspectorsWithoutKnownIds()
    {
        ContentRegistry content = ContentRegistry.Load([
            ("entities/base.json", """{"id":"future:base","kind":"Marker","displayName":"Beacon","category":"logic","editor":{"icon":"icons/beacon.png","char":"B"},"props":{"tag":{"type":"ref","refKind":"tag","default":"group"}}}"""),
            ("entities/child.json", """{"id":"future:child","extends":"future:base","displayName":"Child beacon","props":{"count":{"type":"int","default":2,"min":1,"max":8}}}"""),
            ("tiles/path.json", """{"id":"future:path","displayName":"Moss path","category":"ground","editor":{"icon":"icons/path.png","char":";"}}""")]);
        Assert.True(content.IsValid); JsonObject dump = ContentEditorCatalog.ToJson(content);
        Assert.Equal(content.Hash, dump["contentHash"]!.GetValue<string>()); Assert.Equal(1, dump["formatVersion"]!.GetValue<int>());
        Assert.Equal(["future:base", "future:child"], dump["entities"]!.AsArray().Select(entry => entry!["id"]!.GetValue<string>()));
        JsonObject child = dump["entities"]![1]!.AsObject(); Assert.Equal("Child beacon", child["displayName"]!.GetValue<string>()); Assert.Equal("logic", child["category"]!.GetValue<string>());
        Assert.Equal("icons/beacon.png", child["icon"]!.GetValue<string>()); Assert.Equal("B", child["char"]!.GetValue<string>()); Assert.Equal("Marker", child["kind"]!.GetValue<string>());
        Assert.Equal("tag", child["properties"]!["tag"]!["refKind"]!.GetValue<string>()); Assert.Equal(8, child["properties"]!["count"]!["max"]!.GetValue<int>());
        Assert.Equal(';', content.Tiles["future:path"].Data.EditorChar); Assert.Equal("icons/path.png", content.Tiles["future:path"].Data.EditorIcon);
        child["properties"]!["count"]!["max"] = 100;
        Assert.Equal(8, ContentEditorCatalog.ToJson(content)["entities"]![1]!["properties"]!["count"]!["max"]!.GetValue<int>());
    }

    [Fact]
    public void EveryShippedPrototypeIsExposedWithAnIndependentSchemaAndStableCultureOrdering()
    {
        var content = MissionAssets.Content; JsonObject dump = ContentEditorCatalog.ToJson(content); string before = dump.ToJsonString();
        Assert.Equal(content.Tiles.Count, dump["tiles"]!.AsArray().Count); Assert.Equal(content.Entities.Count, dump["entities"]!.AsArray().Count);
        foreach (JsonNode? entry in dump["tiles"]!.AsArray().Concat(dump["entities"]!.AsArray()))
        {
            Assert.NotEmpty(entry!["displayName"]!.GetValue<string>()); Assert.NotEmpty(entry["category"]!.GetValue<string>());
            Assert.StartsWith("editor/icons/", entry["icon"]!.GetValue<string>()); Assert.Equal(1, entry["char"]!.GetValue<string>().Length);
            Assert.IsType<JsonObject>(entry["properties"]);
        }
        CultureInfo previous = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR"); Assert.Equal(before, ContentEditorCatalog.ToJson(content).ToJsonString()); }
        finally { CultureInfo.CurrentCulture = previous; }
        Assert.Null(DefinitionData.Empty.EditorChar); Assert.Empty(DefinitionData.Empty.EditorIcon);
        Assert.Throws<ArgumentNullException>(() => ContentEditorCatalog.ToJson(null!));
        Assert.Throws<ArgumentException>(() => ContentEditorCatalog.ToJson(ContentRegistry.Load([("entities/bad.json", "bad")])));
    }
}
