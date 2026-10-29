using System.Text.Json.Nodes;
using Dungeons.Core.Content;
using Dungeons.Core.Level;
using Dungeons.Core.Sim;
using Xunit;

namespace Dungeons.Core.Tests;

public sealed class ContentRegistryTests
{
    [Fact]
    public void InheritanceIsDeepImmutableOrderedAndSupportsEliteStatMultipliers()
    {
        (string path, string json)[] files = [
            ("entities/base.json", """{"id":"test:base","kind":"Enemy","radius":0.35,"stats":{"hp":40,"speed":2.6},"render":{"model":"models/base.glb","scale":1},"props":{"range":{"type":"float","default":2,"min":0,"max":10}}}"""),
            ("entities/elite.json", """{"id":"test:elite","extends":"test:base","statMultipliers":{"hp":2},"render":{"scale":1.5}}""")];
        ContentRegistry content = ContentRegistry.Load(files), reversed = ContentRegistry.Load(files.Reverse());
        Assert.True(content.IsValid, string.Join('\n', content.Diagnostics)); Assert.Equal(content.Hash, reversed.Hash);
        Assert.Equal(80, content.Entities["test:elite"].Stats.Hp); Assert.Equal(40, content.Entities["test:base"].Stats.Hp);
        Assert.Equal(2.6f, content.Entities["test:elite"].Stats.Speed);
        Assert.Equal("models/base.glb", content.Entities["test:elite"].Data.Json.GetProperty("render").GetProperty("model").GetString());
        Assert.Equal(1.5f, content.Entities["test:elite"].Data.Json.GetProperty("render").GetProperty("scale").GetSingle());
        Assert.Single(content.Entities["test:elite"].Data.Properties.Definitions);
        Assert.False(content.Entities is Dictionary<string, EntityDef>); Assert.Throws<ContentMissingException>(() => content.Tile("test:none"));
        Assert.Throws<ArgumentException>(() => new EntityDef("x", EntityKind.Player, default));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"id\":\"bad\"}")]
    [InlineData("{\"id\":\"test:a\",\"kind\":\"Typo\"}")]
    [InlineData("{\"id\":\"test:a\",\"kind\":\"Enemy\",\"stats\":{\"hp\":\"bad\"}}")]
    [InlineData("{\"id\":\"test:a\",\"kind\":\"Enemy\",\"stats\":{\"speed\":999}}")]
    [InlineData("{\"id\":\"test:a\",\"extends\":\"test:missing\"}")]
    [InlineData("{\"id\":\"test:a\",\"extends\":\"test:a\"}")]
    [InlineData("{\"id\":\"test:a\",\"kind\":\"Enemy\",\"props\":{\"p\":{\"type\":\"typo\"}}}")]
    [InlineData("{\"id\":\"test:a\",\"kind\":\"Enemy\",\"props\":{\"p\":{\"type\":\"enum\"}}}")]
    [InlineData("{\"id\":\"test:a\",\"kind\":\"Enemy\",\"audio\":123}")]
    [InlineData("{\"id\":\"test:a\",\"kind\":\"Enemy\",\"render\":{\"variants\":0}}")]
    [InlineData("{\"id\":\"test:a\",\"kind\":\"Enemy\",\"render\":{\"variants\":1.5}}")]
    [InlineData("{\"id\":\"test:a\",\"kind\":\"Enemy\",\"render\":{\"topVariants\":-1}}")]
    [InlineData("{\"id\":\"test:a\",\"kind\":\"Enemy\",\"render\":{\"thickness\":2}}")]
    public void InvalidDefinitionsReturnErrors(string json)
    {
        ContentRegistry content = ContentRegistry.Load(new[] { ("entities/a.json", json) });
        Assert.False(content.IsValid); Assert.Contains(content.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void DuplicateIdsAndUnknownBaseFloorLootBrainsAndSoundsAreRejected()
    {
        string entity = """{"id":"test:a","kind":"Enemy","brain":"bad:brain","loot":"bad:loot","audio":{"hit":"bad:sound"}}""";
        ContentRegistry content = ContentRegistry.Load(new[] { ("entities/a.json", entity), ("entities/b.json", entity), ("tiles/tree.json", """{"id":"test:tree","baseFloor":"test:missing"}""") });
        Assert.Contains(content.Diagnostics, d => d.Code == DiagnosticCode.CNT1001);
        Assert.Equal(4, content.Diagnostics.Count(d => d.Code == DiagnosticCode.CNT1003));
    }

    [Fact]
    public void PropertySchemaAcceptsAllInspectorTypesAndRejectsInvalidValues()
    {
        string json = """
            {"id":"test:a","kind":"Marker","props":{
              "count":{"type":"int","default":2,"min":1,"max":4},
              "radius":{"type":"float","default":1.5,"min":0,"max":3},
              "enabled":{"type":"bool","default":true},
              "name":{"type":"string","default":"","maxLength":4},
              "state":{"type":"enum","values":["a","b"],"default":"a"},
              "color":{"type":"color","default":"#aBcDeF"},
              "loot":{"type":"ref","refKind":"loot","default":"test:loot"},
              "point":{"type":"vec2","default":[1,2]}}}
            """;
        ContentRegistry registry = ContentRegistry.Load(new[] { ("entities/a.json", json) });
        Assert.True(registry.IsValid); PropertySchema schema = registry.Entities["test:a"].Data.Properties;
        var diagnostics = new List<Diagnostic>(); JsonObject defaults = schema.Normalize(null, diagnostics, "/props");
        Assert.Equal(8, defaults.Count); Assert.All(diagnostics, d => Assert.Equal(DiagnosticSeverity.Info, d.Severity));
        JsonObject descriptor = schema.Definitions["count"].ToJson(); descriptor["max"] = 999;
        var supplied = new JsonObject { ["count"] = 5, ["radius"] = -1, ["enabled"] = "yes", ["name"] = "longer", ["state"] = "c", ["color"] = "red", ["point"] = new JsonArray(1), ["typo"] = 1 };
        diagnostics.Clear(); schema.Normalize(supplied, diagnostics, "/props");
        Assert.Equal(8, diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Null(supplied["loot"]); Assert.Equal("count", schema.Definitions["count"].Label);
    }
}
