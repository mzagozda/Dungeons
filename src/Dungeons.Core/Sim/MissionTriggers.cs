using Dungeons.Core.Math;
using Dungeons.Core.Util;

namespace Dungeons.Core.Sim;

public sealed partial class MissionState
{
    internal void StepTriggers(World world)
    {
        if (world.Tick > 0 && MessageTicksRemaining > 0 && --MessageTicksRemaining == 0) { Message = ""; }
        StepSpawners(world);
        foreach (RuntimeTrigger trigger in _triggers)
        {
            if (!trigger.Enabled || trigger.Done) { continue; }
            if (!trigger.Running)
            {
                if (trigger.LastFireTick == world.Tick || !EventMatches(world, trigger)) { continue; }
                bool allowed = true; foreach (RuntimeCondition condition in trigger.Conditions) { if (!Matches(world, condition)) { allowed = false; break; } }
                if (!allowed) { continue; }
                trigger.Running = true; trigger.Cursor = 0; trigger.LastFireTick = world.Tick;
            }
            if (world.Tick < trigger.DueTick) { continue; }
            while (trigger.Cursor < trigger.Actions.Length && trigger.Enabled)
            {
                RuntimeAction action = trigger.Actions[trigger.Cursor++];
                if (action.Kind == "delay")
                { trigger.DueTick = checked(world.Tick + action.Duration); if (action.Duration > 0) { break; } }
                else { Execute(world, action); }
                if (Outcome != SessionState.Running) { return; }
            }
            // A trailing delay must still elapse before the list is complete.
            if (trigger.Cursor == trigger.Actions.Length && world.Tick >= trigger.DueTick)
            { trigger.Running = false; if (trigger.Once) { trigger.Done = true; trigger.Enabled = false; } }
        }
        StepGateTriggers(world);
    }
    private bool EventMatches(World world, RuntimeTrigger trigger)
    {
        switch (trigger.Event)
        {
            case "level_start": return world.Tick == 0;
            case "timer":
                if (trigger.TimerUsed || world.Tick - trigger.EnabledTick < trigger.Duration) { return false; }
                trigger.TimerUsed = true; return true;
            case "entities_dead":
                bool satisfied = AllDead(world, trigger.Index, trigger.Minimum); bool changed = satisfied && !trigger.WasSatisfied; trigger.WasSatisfied = satisfied; return changed;
            case "objective":
                RuntimeObjective objective = _objectives[trigger.Index]; if (objective.Version == trigger.SeenVersion) { return false; }
                trigger.SeenVersion = objective.Version; return objective.State == trigger.State;
            case "flag":
                FlagSlot flag = _flags[trigger.Index]; if (flag.Version == trigger.SeenVersion) { return false; }
                trigger.SeenVersion = flag.Version; return flag.Value == trigger.Value;
            case "interact": case "checkpoint":
                Entity? target = world.FindAuthoredEntity(trigger.Target); if (target is null || target.WorldObject.ChangedTick == trigger.SeenVersion) { return false; }
                trigger.SeenVersion = target.WorldObject.ChangedTick;
                return trigger.SeenVersion >= 0 && (trigger.Event == "checkpoint" ? target.WorldObject.Activated : target.WorldObject.Opened || target.WorldObject.Interacted);
            case "enter": case "exit":
                for (int i = 0; i < world.Entities.Count; i++)
                {
                    Entity entity = world.Entities[i];
                    if (!entity.Alive || entity.LifetimeTicks == 0 || trigger.Who == "player" && entity.Kind != EntityKind.Player || trigger.Who == "monster" && entity.Kind != EntityKind.Enemy) { continue; }
                    bool before = Inside(trigger, entity.PrevPos), after = Inside(trigger, entity.Pos);
                    if (entity.SpawnTick == world.Tick) { before = false; }
                    if (trigger.Event == "enter" ? !before && after : before && !after) { return true; }
                }
                return false;
            default: return false;
        }
    }
    private static bool Inside(RuntimeTrigger trigger, Vec2 position) => position.X >= trigger.X && position.Z >= trigger.Z && position.X <= trigger.X + trigger.Width && position.Z <= trigger.Z + trigger.Height;
    private bool Matches(World world, RuntimeCondition condition) => condition.Kind switch
    {
        "objective" => _objectives[condition.Index].State == condition.State,
        "flag" => _flags[condition.Index].Value == condition.Value,
        "entities_dead" => AllDead(world, condition.Index, condition.Minimum),
        "lives_at_least" => world.Lives >= condition.Minimum,
        "not" => !Matches(world, condition.Nested!),
        _ => false,
    };
    private void Execute(World world, RuntimeAction action, int runtimeEntityId = 0)
    {
        switch (action.Kind)
        {
            case "objective": SetObjective(world, action.Index, action.State); break;
            case "message":
                Message = action.Duration > 0 ? action.Text : ""; MessageTicksRemaining = action.Duration;
                world.Emit(new(world.Tick, SimEventKind.ShowMessage, 0, default, text: action.Text, durationTicks: action.Duration)); break;
            case "sound": case "music": world.Emit(new(world.Tick, action.Kind == "sound" ? SimEventKind.PlaySound : SimEventKind.MusicChange, 0, default, text: action.Target)); break;
            case "camera_shake": world.Emit(new(world.Tick, SimEventKind.CameraShake, 0, default, value: action.Value, durationTicks: action.Duration)); break;
            case "set_flag":
                FlagSlot flag = _flags[action.Index]; if (flag.Value == action.Boolean) { break; } flag.Value = action.Boolean; flag.Version++;
                world.Emit(new(world.Tick, SimEventKind.FlagChanged, 0, default, sourceId: action.Index, amount: flag.Value ? 1 : 0)); break;
            case "set_state":
                Entity? target = runtimeEntityId == 0 ? world.FindAuthoredEntity(action.Target) : world.FindEntity(runtimeEntityId); if (target is null) { break; }
                if (target.Kind == EntityKind.Gate) { world.SetGate(target, action.Text == "open"); }
                else if (target.Kind == EntityKind.Chest && action.Text == "open") { world.OpenChest(target); }
                else if (target.Kind == EntityKind.Checkpoint && action.Text == "active") { world.ActivateCheckpoint(target); }
                else if (action.Text == "active") { ActivateSpawner(world, target.Id); }
                break;
            case "checkpoint": Entity? checkpoint = world.FindAuthoredEntity(action.Target); if (checkpoint is not null) { world.ActivateCheckpoint(checkpoint); } break;
            case "enable_trigger":
                RuntimeTrigger enabled = _triggers[action.Index]; enabled.Enabled = true; enabled.Done = false; enabled.EnabledTick = world.Tick; enabled.TimerUsed = false; break;
            case "disable_trigger": _triggers[action.Index].Enabled = false; break;
            case "remove_entity": Entity? removed = world.FindAuthoredEntity(action.Target); if (removed is not null) { world.Despawn(removed); } break;
            case "complete_level": End(world, false); break;
            case "fail_level": End(world, true); break;
            case "heal_player":
                for (int p = 0; p < world.Players.Count; p++)
                {
                    Entity hero = world.Players[p];
                    if (!hero.Alive) { continue; } int amount = (int)System.Math.Min(hero.Health.Max - hero.Health.Current, System.Math.Ceiling(hero.Health.Max * (double)action.Value / 100));
                    hero.HealthData.Current += amount; world.Emit(new(world.Tick, SimEventKind.Healed, hero.Id, hero.Pos, amount: amount));
                }
                break;
            case "give":
                for (int p = 0; p < world.Players.Count; p++)
                {
                    Entity hero = world.Players[p];
                    if (!hero.Alive) { continue; }
                    if (action.Text == "emerald") { hero.InventoryData.Emeralds = Saturate(hero.InventoryData.Emeralds, action.Amount); }
                    else { hero.InventoryData.Arrows = System.Math.Min(hero.Def.Controls.ArrowsMax, Saturate(hero.InventoryData.Arrows, action.Amount)); }
                    world.Emit(new(world.Tick, SimEventKind.InventoryChanged, hero.Id, hero.Pos, amount: action.Amount, text: action.Text));
                }
                break;
            case "spawn":
                Entity? marker = world.FindAuthoredEntity(action.Target); if (marker is null) { break; }
                for (int i = 0; i < action.Count; i++) { SpawnAt(world, action.Definition!, action.Settings!, marker.Pos, marker.Facing, .5f); }
                break;
        }
    }
    private static bool SpawnAt(World world, Content.EntityDef definition, SpawnSettings settings, Vec2 origin, Vec2 facing, float radius, int ownerId = 0)
    {
        Vec2 offset = default;
        if (radius > 0)
        {
            for (int attempt = 0; attempt < 32; attempt++)
            { Vec2 candidate = new(world.Randoms.Spawn.Range(-1, 1), world.Randoms.Spawn.Range(-1, 1)); if (candidate.LengthSquared <= 1) { offset = candidate * radius; break; } }
        }
        if (!world.TryNearestSafePosition(origin + offset, definition.Radius, out Vec2 point)) { return false; }
        bool accepted = definition.Kind == EntityKind.Enemy ? world.RequestMonsterSpawn(definition, point, facing, settings: settings, ownerId: ownerId) : world.SpawnEntity(definition, point, facing, settings: settings) is not null;
        if (!accepted) { world.Log.Log(LogLevel.Warning, "Sim", "Mission spawn could not be placed or exceeded entity capacity."); }
        return accepted;
    }
}
