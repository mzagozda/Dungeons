using System.Text.Json;
using System.Text.Json.Nodes;
using Dungeons.Core.Content;
using Dungeons.Core.Level;
using Dungeons.Core.Math;
using Dungeons.Core.Util;

namespace Dungeons.Core.Sim;

public sealed partial class MissionState
{
    private sealed class RuntimeCondition
    {
        internal string Kind = "";
        internal int Index = -1, Minimum = 1;
        internal bool Value = true;
        internal ObjectiveState State;
        internal RuntimeCondition? Nested;
    }
    private sealed class RuntimeAction
    {
        internal string Kind = "", Target = "", Text = "";
        internal int Index = -1, Amount, Duration, Count = 1;
        internal float Value;
        internal bool Boolean = true;
        internal ObjectiveState State;
        internal EntityDef? Definition;
        internal SpawnSettings? Settings;
    }
    private sealed class RuntimeTrigger
    {
        internal string Event = "", Target = "", Who = "player";
        internal int Index = -1, Minimum = 1, Duration, Cursor, DueTick, EnabledTick, SeenVersion = -1, LastFireTick = -1;
        internal float X, Z, Width, Height;
        internal bool Value = true, Once = true, Enabled = true, Running, Done, WasSatisfied, TimerUsed;
        internal ObjectiveState State;
        internal RuntimeCondition[] Conditions = [];
        internal RuntimeAction[] Actions = [];
        internal void Hash(ref Fnv1a hash)
        {
            hash.Add(Cursor); hash.Add(DueTick); hash.Add(EnabledTick); hash.Add(SeenVersion); hash.Add(LastFireTick);
            hash.Add(Enabled); hash.Add(Running); hash.Add(Done); hash.Add(WasSatisfied); hash.Add(TimerUsed);
        }
    }
    private RuntimeTrigger[] CompileTriggers(JsonObject? source)
    {
        var nodes = new List<JsonObject>();
        if (source?["triggers"] is JsonArray triggers) { foreach (JsonNode? trigger in triggers) { nodes.Add(trigger!.AsObject()); } }
        for (int i = 0; i < nodes.Count; i++) { _triggerIds.Add(JsonData.Text(nodes[i]["id"], "__trigger_" + JsonData.Index(i)), i); }
        var result = new RuntimeTrigger[nodes.Count];
        for (int i = 0; i < nodes.Count; i++)
        {
            JsonObject node = nodes[i], ev = node["event"]!.AsObject();
            var trigger = new RuntimeTrigger
            {
                Event = JsonData.Text(ev["type"]), Target = JsonData.Text(ev["id"]), Who = JsonData.Text(ev["who"], "player"),
                Value = JsonData.Boolean(ev["value"], true), Minimum = JsonData.Integer(ev["min"], 1), Duration = Ticks(ev["seconds"]),
                Once = JsonData.Boolean(node["once"], true), Enabled = JsonData.Boolean(node["enabled"], true),
            };
            switch (trigger.Event)
            {
                case "objective": trigger.Index = _objectiveIds[trigger.Target]; trigger.State = StateOf(JsonData.Text(ev["state"])); trigger.SeenVersion = 0; break;
                case "flag": trigger.Index = _flagIds[JsonData.Text(ev["flag"])]; trigger.SeenVersion = 0; break;
                case "entities_dead": trigger.Index = TagIndex(JsonData.Text(ev["tag"])); break;
                case "enter": case "exit":
                    trigger.X = (float)JsonData.Number(ev["area"]?["x"]); trigger.Z = (float)JsonData.Number(ev["area"]?["z"]);
                    trigger.Width = (float)JsonData.Number(ev["area"]?["w"]); trigger.Height = (float)JsonData.Number(ev["area"]?["h"]); break;
                case "level_start": case "timer": case "interact": case "checkpoint": break;
                default: throw new ArgumentException("Unknown mission event.");
            }
            trigger.Conditions = node["conditions"] is JsonArray conditions ? conditions.Select(condition => CompileCondition(condition!.AsObject())).ToArray() : [];
            trigger.Actions = node["actions"]!.AsArray().Select(action => CompileAction(action!.AsObject())).ToArray(); result[i] = trigger;
        }
        return result;
    }
    private RuntimeCondition CompileCondition(JsonObject node)
    {
        var condition = new RuntimeCondition { Kind = JsonData.Text(node["type"]), Value = JsonData.Boolean(node["value"], true), Minimum = JsonData.Integer(node["min"], 1) };
        switch (condition.Kind)
        {
            case "objective": condition.Index = _objectiveIds[JsonData.Text(node["id"])]; condition.State = StateOf(JsonData.Text(node["state"])); break;
            case "flag": condition.Index = _flagIds[JsonData.Text(node["flag"])]; break;
            case "entities_dead": condition.Index = TagIndex(JsonData.Text(node["tag"])); break;
            case "lives_at_least": condition.Minimum = JsonData.Integer(node["lives"]); break;
            case "not": condition.Nested = CompileCondition(node["condition"]!.AsObject()); break;
            default: throw new ArgumentException("Unknown mission condition.");
        }
        return condition;
    }
    private RuntimeAction CompileAction(JsonObject node)
    {
        var action = new RuntimeAction { Kind = JsonData.Text(node["type"]), Target = JsonData.Text(node["id"]), Boolean = JsonData.Boolean(node["value"], true) };
        switch (action.Kind)
        {
            case "objective": action.Index = _objectiveIds[action.Target]; action.State = StateOf(JsonData.Text(node["state"])); break;
            case "message": action.Text = JsonData.Localized(node["text"]); action.Duration = Ticks(node["seconds"], 2.5); break;
            case "delay": action.Duration = Ticks(node["seconds"]); break;
            case "sound": case "music": break;
            case "camera_shake": action.Value = (float)JsonData.Number(node["strength"], .3); action.Duration = Ticks(node["seconds"], .3); break;
            case "set_flag": action.Index = _flagIds[JsonData.Text(node["flag"])]; break;
            case "set_state": action.Target = JsonData.Text(node["entity"]); action.Text = JsonData.Text(node["state"]); break;
            case "enable_trigger": case "disable_trigger": action.Index = _triggerIds[action.Target]; break;
            case "checkpoint": case "remove_entity": case "complete_level": case "fail_level": break;
            case "heal_player": action.Value = (float)JsonData.Number(node["percent"], 100); break;
            case "give": action.Text = JsonData.Text(node["item"]); action.Amount = JsonData.Integer(node["amount"]); break;
            case "spawn":
                action.Target = JsonData.Text(node["at"]); string id = JsonData.Text(node["entity"]?["type"]);
                if (_content is null || !_content.Entities.TryGetValue(id, out EntityDef? definition)) { throw new ContentMissingException(id); }
                action.Definition = definition; action.Count = _difficulty.ScaleSpawnCount(JsonData.Integer(node["count"], 1));
                var diagnostics = new List<Diagnostic>(); JsonObject properties = definition.Data.Properties.Normalize(node["entity"]?["props"] as JsonObject, diagnostics, "/spawn/props");
                if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)) { throw new ArgumentException("Unvalidated spawn properties."); }
                using (JsonDocument document = JsonDocument.Parse(JsonData.Stringify(properties)))
                {
                    string[] tags = node["entity"]?["tags"] is JsonArray authoredTags ? authoredTags.Select(tag => JsonData.Text(tag)).ToArray() : [];
                    foreach (string tag in tags) { TagIndex(tag); }
                    action.Settings = new(new EntityPlacement(definition, default, null, Vec2.North, Array.AsReadOnly(tags), document.RootElement.Clone()));
                    if (definition.Kind == EntityKind.Gate && action.Settings.Properties.TryGetProperty("openWhen", out JsonElement gateObjective) && gateObjective.GetString() is string goal && goal.Length > 0)
                    { action.Settings.GateObjective = _objectiveIds[goal]; }
                    if (definition.Kind == EntityKind.SpawnMarker && action.Settings.Properties.TryGetProperty("entity", out _))
                    {
                        action.Settings.Spawner = new(action.Settings.Properties, _content, _difficulty);
                        if (action.Settings.Spawner.Tag.Length > 0) { TagIndex(action.Settings.Spawner.Tag); }
                    }
                }
                break;
            default: throw new ArgumentException("Unknown mission action.");
        }
        return action;
    }
}
