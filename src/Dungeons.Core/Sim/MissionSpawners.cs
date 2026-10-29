using Dungeons.Core.Content;
using Dungeons.Core.Math;

namespace Dungeons.Core.Sim;

public sealed partial class MissionState
{
    private struct RuntimeSpawner
    {
        internal int EntityId, Remaining, NextTick;
        internal bool Active, Finished;
        internal SpawnerTemplate Template;
    }
    private readonly Dictionary<EntityDef, SpawnerTemplate> _defaultSpawners = [];
    private readonly Dictionary<EntityDef, int> _defaultGateObjectives = [];
    private void CompileSpawners(World world)
    {
        if (_content is not null)
        {
            foreach (EntityDef definition in _content.Entities.Values)
            {
                if (definition.Kind == EntityKind.Gate && definition.DefaultProperties.TryGetProperty("openWhen", out var goal) && _objectiveIds.TryGetValue(goal.GetString()!, out int objective))
                { _defaultGateObjectives.Add(definition, objective); }
                if (definition.Kind != EntityKind.SpawnMarker || !definition.DefaultProperties.TryGetProperty("entity", out _)) { continue; }
                var template = new SpawnerTemplate(definition.DefaultProperties, _content, _difficulty); _defaultSpawners.Add(definition, template);
                if (template.Tag.Length > 0) { TagIndex(template.Tag); }
            }
        }
        foreach (Entity entity in world.Entities)
        {
            if (entity.Kind != EntityKind.SpawnMarker || !entity.Properties.TryGetProperty("entity", out _)) { continue; }
            if (_content is null) { throw new ArgumentException("Spawner requires a content registry."); }
            var template = new SpawnerTemplate(entity.Properties, _content, _difficulty);
            if (template.Tag.Length > 0) { TagIndex(template.Tag); }
            RegisterSpawner(world, entity, template);
        }
    }
    internal void RegisterSpawnedObject(World world, Entity entity, SpawnSettings? settings)
    {
        if (entity.Kind == EntityKind.Gate)
        {
            int objective = settings?.GateObjective ?? (_defaultGateObjectives.TryGetValue(entity.Def, out int bound) ? bound : -1);
            if (objective >= 0) { RegisterGateTrigger(world, entity.Id, objective); } return;
        }
        if (entity.Kind != EntityKind.SpawnMarker) { return; }
        SpawnerTemplate? template = settings?.Spawner;
        if (template is null) { _defaultSpawners.TryGetValue(entity.Def, out template); }
        if (template is not null) { RegisterSpawner(world, entity, template); }
    }
    private void RegisterSpawner(World world, Entity entity, SpawnerTemplate template)
    {
        int index = _spawnerCount;
        for (int i = 0; i < _spawnerCount; i++) { if (world.FindEntity(_spawners[i].EntityId) is null) { index = i; break; } }
        if (index == _spawners.Length) { throw new InvalidOperationException("Spawner capacity exceeded."); }
        if (index == _spawnerCount) { _spawnerCount++; }
        _spawners[index] = new() { EntityId = entity.Id, Template = template, Remaining = template.Count };
        if (template.Tag.Length > 0) { TagSlot slot = _tags[_tagIds[template.Tag]]; slot.Expected = Saturate(slot.Expected, template.Count); }
    }
    private void ActivateSpawner(World world, int entityId)
    {
        for (int i = 0; i < _spawnerCount; i++)
        {
            ref RuntimeSpawner spawner = ref _spawners[i];
            if (spawner.EntityId != entityId || spawner.Active || spawner.Finished) { continue; }
            spawner.Active = true; spawner.NextTick = checked(world.Tick + spawner.Template.Delay);
            world.Emit(new(world.Tick, SimEventKind.SpawnerActivated, entityId, world.FindEntity(entityId)?.Pos ?? default));
        }
    }
    private void StepSpawners(World world)
    {
        for (int i = 0; i < _spawnerCount; i++)
        {
            ref RuntimeSpawner spawner = ref _spawners[i]; SpawnerTemplate template = spawner.Template;
            Entity? marker = world.FindEntity(spawner.EntityId); if (marker is not { Alive: true } || marker.LifetimeTicks == 0 || spawner.Finished) { continue; }
            if (!spawner.Active)
            {
                bool activate = template.Activation == "immediate";
                if (!activate && template.Activation is "proximity" or "sight")
                {
                    for (int p = 0; p < world.Players.Count; p++)
                    {
                        Entity hero = world.Players[p];
                        if (!hero.Alive || Vec2.DistanceSquared(hero.Pos, marker.Pos) > template.ActivationRadius * template.ActivationRadius) { continue; }
                        if (template.Activation == "sight" && !TerrainSight.Visible(world.Grid, marker.Pos, marker.Y + .9f, hero.Pos, hero.Y + .9f)) { continue; }
                        activate = true; break;
                    }
                }
                if (activate) { ActivateSpawner(world, marker.Id); }
            }
            if (!spawner.Active || world.Tick < spawner.NextTick) { continue; }
            while (spawner.Remaining > 0 && world.CountOwnedMonsters(marker.Id) < template.MaxAlive)
            {
                if (!SpawnAt(world, template.Definition, template.Settings, marker.Pos, marker.Facing, template.Radius, marker.Id)) { break; }
                spawner.Remaining--; spawner.NextTick = checked(world.Tick + template.Interval);
                if (template.Interval > 0) { break; }
            }
            if (spawner.Remaining == 0)
            { spawner.Finished = true; world.Emit(new(world.Tick, SimEventKind.SpawnerFinished, marker.Id, marker.Pos)); }
        }
    }
}
