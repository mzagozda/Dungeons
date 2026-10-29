using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Nodes;
using Dungeons.Core.Level;
using Dungeons.Core.Sim;
using Xunit;

namespace Dungeons.Core.Tests;

public sealed class TriggerSchemaTests
{
    [Fact]
    public void EveryAppendixTypeHasOnlyItsOwnFormFieldsAndTheCatalogueIsImmutable()
    {
        string[][] expected = [
            ["event", "level_start", ""], ["event", "enter", "area,who"], ["event", "exit", "area,who"], ["event", "entities_dead", "min,tag"],
            ["event", "objective", "id,state"], ["event", "interact", "id"], ["event", "checkpoint", "id"], ["event", "flag", "flag,value"], ["event", "timer", "seconds"],
            ["action", "message", "seconds,text"], ["action", "objective", "id,state"], ["action", "spawn", "at,count,entity"], ["action", "delay", "seconds"],
            ["action", "sound", "id"], ["action", "music", "id"], ["action", "camera_shake", "seconds,strength"], ["action", "set_state", "entity,state"],
            ["action", "set_flag", "flag,value"], ["action", "checkpoint", "id"], ["action", "enable_trigger", "id"], ["action", "disable_trigger", "id"],
            ["action", "complete_level", ""], ["action", "fail_level", ""], ["action", "remove_entity", "id"], ["action", "heal_player", "percent"], ["action", "give", "amount,item"],
            ["condition", "objective", "id,state"], ["condition", "flag", "flag,value"], ["condition", "entities_dead", "min,tag"], ["condition", "lives_at_least", "lives"], ["condition", "not", "condition"]
        ];
        Assert.Equal(31, TriggerSchema.Events.Count + TriggerSchema.Actions.Count + TriggerSchema.Conditions.Count);
        foreach (string[] row in expected)
        {
            var group = Group(row[0]); TriggerTypeSchema schema = group[row[1]]; Assert.Equal(row[1], schema.Type);
            Assert.Equal(row[2], string.Join(',', schema.Parameters.Keys.Order(StringComparer.Ordinal)));
            Assert.Throws<NotSupportedException>(() => ((IDictionary<string, TriggerTypeSchema>)group).Add("bad", schema));
            Assert.Throws<NotSupportedException>(() => ((IDictionary<string, TriggerParameter>)schema.Parameters).Clear());
        }
        string before = TriggerSchema.ToJson().ToJsonString(); JsonObject copy = TriggerSchema.Actions["spawn"].ToJson(); copy["properties"]!["count"]!["maximum"] = 500;
        JsonObject dump = TriggerSchema.ToJson(); dump["$defs"]!["event"] = false;
        Assert.Equal(before, TriggerSchema.ToJson().ToJsonString());
        CultureInfo previous = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pl-PL"); Assert.Equal(before, TriggerSchema.ToJson().ToJsonString()); }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void ReferencePickersRangesDefaultsAndNestedFormsExposeProductionMetadata()
    {
        TriggerParameter area = TriggerSchema.Events["enter"].Parameters["area"]; Assert.Equal("object", area.Type); Assert.True(area.Required);
        Assert.Equal(["h", "w", "x", "z"], area.Properties.Keys.Order(StringComparer.Ordinal)); Assert.All(area.Properties.Values, field => Assert.True(field.Required));
        Assert.Equal("player", TriggerSchema.Events["enter"].Parameters["who"].Schema.GetProperty("default").GetString());
        Assert.Equal("objective", TriggerSchema.Events["objective"].Parameters["id"].RefKind);
        Assert.Equal("entity", TriggerSchema.Events["checkpoint"].Parameters["id"].RefKind); Assert.Equal(["Checkpoint"], TriggerSchema.Events["checkpoint"].Parameters["id"].TargetKinds);
        Assert.Equal(["Chest", "Gate"], TriggerSchema.Events["interact"].Parameters["id"].TargetKinds);
        Assert.Equal("trigger", TriggerSchema.Actions["enable_trigger"].Parameters["id"].RefKind); Assert.Equal("trigger", TriggerSchema.Actions["disable_trigger"].Parameters["id"].RefKind);
        Assert.Equal("entity", TriggerSchema.Actions["remove_entity"].Parameters["id"].RefKind); Assert.Empty(TriggerSchema.Actions["remove_entity"].Parameters["id"].TargetKinds);
        Assert.Equal("sound", TriggerSchema.Actions["music"].Parameters["id"].RefKind); Assert.Equal("flag", TriggerSchema.Conditions["flag"].Parameters["flag"].RefKind);
        TriggerParameter prototype = TriggerSchema.Actions["spawn"].Parameters["entity"];
        Assert.True(prototype.Properties["type"].Required); Assert.Equal("entityDefinition", prototype.Properties["type"].RefKind);
        Assert.Equal("tag", prototype.Properties["tags"].Items!.RefKind); Assert.Equal("type", prototype.Properties["props"].PropertySchemaFrom);
        TriggerParameter count = TriggerSchema.Actions["spawn"].Parameters["count"]; Assert.Equal("integer", count.Type); Assert.False(count.Required);
        Assert.Equal(16, count.Schema.GetProperty("maximum").GetInt32()); Assert.Equal(1, count.Schema.GetProperty("default").GetInt32());
        Assert.Equal("localizedString", TriggerSchema.Actions["message"].Parameters["text"].Type);
        TriggerParameter nested = TriggerSchema.Conditions["not"].Parameters["condition"]; Assert.Equal("condition", nested.Type); Assert.True(nested.Required); Assert.Empty(nested.Properties);
        Assert.Equal("#/$defs/condition", nested.SchemaReference); Assert.Null(nested.Items);
        Assert.Equal("hidden", TriggerSchema.Conditions["objective"].Parameters["state"].Schema.GetProperty("default").GetString());
        Assert.Equal(["emerald", "arrows"], TriggerSchema.Actions["give"].Parameters["item"].Choices);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)count.TargetKinds).Add("bad"));
        foreach (TriggerTypeSchema schema in TriggerSchema.Events.Values.Concat(TriggerSchema.Actions.Values).Concat(TriggerSchema.Conditions.Values))
        { foreach (TriggerParameter parameter in schema.Parameters.Values) { Assert.All(parameter.TargetKinds, kind => Assert.True(Enum.TryParse<EntityKind>(kind, false, out _))); } }
    }

    [Fact]
    public void EveryGeneratedFormLoadsCompilesAndRejectsMissingRequiredOrMistypedParameters()
    {
        foreach (string category in new[] { "event", "action", "condition" })
        {
            foreach (TriggerTypeSchema schema in Group(category).Values)
            {
                JsonObject source = Source(category, Sample(category, schema.Type));
                LevelLoadResult loaded = LevelLoader.Load(source.ToJsonString(), MissionAssets.Content);
                Assert.True(loaded.Success, category + "/" + schema.Type + ": " + string.Join(';', loaded.Diagnostics));
                var session = GameSession.Create(loaded.Document!, MissionAssets.Content, 1); session.Step(default(PlayerInput));
                foreach (TriggerParameter parameter in schema.Parameters.Values)
                {
                    if (parameter.Required)
                    {
                        JsonObject missing = source.DeepClone().AsObject(); Selected(missing, category).Remove(parameter.Name);
                        Assert.False(LevelLoader.Load(missing.ToJsonString(), MissionAssets.Content).Success, $"Missing {category}/{schema.Type}/{parameter.Name}");
                    }
                    JsonObject wrong = source.DeepClone().AsObject(); Selected(wrong, category)[parameter.Name] = parameter.Type == "boolean" ? JsonValue.Create("wrong") : JsonValue.Create(false);
                    Assert.False(LevelLoader.Load(wrong.ToJsonString(), MissionAssets.Content).Success, $"Wrong type {category}/{schema.Type}/{parameter.Name}");
                }
            }
        }
    }

    [Theory]
    [InlineData("event", "unknown")]
    [InlineData("action", "unknown")]
    [InlineData("condition", "unknown")]
    [InlineData("action", "camera_focus")]
    [InlineData("action", "dialogue")]
    public void UnknownAndReservedTypesStayOutOfTheCatalogueAndFailValidation(string category, string type)
    {
        Assert.False(Group(category).ContainsKey(type));
        LevelLoadResult result = LevelLoader.Load(Source(category, new() { ["type"] = type }).ToJsonString(), MissionAssets.Content);
        Assert.False(result.Success); Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.JsonPath.EndsWith("/type", StringComparison.Ordinal));
        if (type is "camera_focus" or "dialogue") { Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.LVL1027); }
    }
    private static IReadOnlyDictionary<string, TriggerTypeSchema> Group(string category) => category switch { "event" => TriggerSchema.Events, "action" => TriggerSchema.Actions, _ => TriggerSchema.Conditions };
    private static JsonObject Selected(JsonObject root, string category)
    {
        JsonObject trigger = root["triggers"]![0]!.AsObject(); return category switch { "event" => trigger["event"]!.AsObject(), "action" => trigger["actions"]![0]!.AsObject(), _ => trigger["conditions"]![0]!.AsObject() };
    }
    private static JsonObject Source(string category, JsonObject value)
    {
        JsonObject root = LevelCommandTests.Create().ToJsonObject(); root["entities"]![3]!["tags"] = new JsonArray("enemies");
        root["entities"]!.AsArray().Add(JsonNode.Parse("""{"id":"marker","type":"core:spawn_marker","x":7.5,"z":7.5}""")); root["flags"] = new JsonObject { ["ready"] = false };
        root["triggers"] = new JsonArray(new JsonObject { ["id"] = "check", ["event"] = new JsonObject { ["type"] = "level_start" }, ["actions"] = new JsonArray(new JsonObject { ["type"] = "message", ["text"] = "OK" }), ["conditions"] = new JsonArray() });
        JsonObject trigger = root["triggers"]![0]!.AsObject();
        if (category == "event") { trigger["event"] = value; } else { trigger[category == "action" ? "actions" : "conditions"] = new JsonArray(value); }
        return root;
    }
    private static JsonObject Sample(string category, string type)
    {
        string properties = (category, type) switch
        {
            (_, "objective") => "\"id\":\"goal\",\"state\":\"complete\"", (_, "flag" or "set_flag") => "\"flag\":\"ready\"", (_, "entities_dead") => "\"tag\":\"enemies\"",
            ("event", "enter" or "exit") => "\"area\":{\"x\":1,\"z\":1,\"w\":2,\"h\":2}", ("event", "interact") => "\"id\":\"gate\"", (_, "checkpoint") => "\"id\":\"cp\"", ("event", "timer") => "\"seconds\":1",
            ("action", "message") => "\"text\":{\"en\":\"Hello\",\"pl\":\"Witaj\"}", ("action", "spawn") => "\"at\":\"marker\",\"entity\":{\"type\":\"core:zombie\"}",
            ("action", "delay") => "\"seconds\":0", ("action", "sound" or "music") => "\"id\":\"" + MissionAssets.Content.Sounds.Keys.First() + "\"",
            ("action", "set_state") => "\"entity\":\"gate\",\"state\":\"open\"", ("action", "enable_trigger" or "disable_trigger") => "\"id\":\"check\"",
            ("action", "remove_entity") => "\"id\":\"z\"", ("action", "give") => "\"item\":\"emerald\",\"amount\":1", ("condition", "not") => "\"condition\":{\"type\":\"flag\",\"flag\":\"ready\"}", _ => ""
        };
        return JsonNode.Parse("{\"type\":\"" + type + "\"" + (properties.Length > 0 ? "," + properties : "") + "}")!.AsObject();
    }
}
