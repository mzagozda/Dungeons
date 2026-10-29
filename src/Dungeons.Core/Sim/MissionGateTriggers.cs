using Dungeons.Core.Util;

namespace Dungeons.Core.Sim;

public sealed partial class MissionState
{
    // Runtime-created gates have no authored instance id. Their compiled objective
    // event uses a stable runtime target with the same set_state action executor.
    private struct GateTrigger { internal int EntityId, Objective; internal bool Fired; }
    private readonly GateTrigger[] _gateTriggers = new GateTrigger[GameConstants.MaxEntities];
    private readonly RuntimeAction _openGateAction = new() { Kind = "set_state", Text = "open" };
    private int _gateTriggerCount;
    private void RegisterGateTrigger(World world, int entityId, int objective)
    {
        int index = _gateTriggerCount;
        for (int i = 0; i < _gateTriggerCount; i++) { if (world.FindEntity(_gateTriggers[i].EntityId) is null) { index = i; break; } }
        if (index == _gateTriggers.Length) { throw new InvalidOperationException("Gate trigger capacity exceeded."); }
        if (index == _gateTriggerCount) { _gateTriggerCount++; }
        _gateTriggers[index] = new() { EntityId = entityId, Objective = objective };
    }
    private void StepGateTriggers(World world)
    {
        for (int i = 0; i < _gateTriggerCount; i++)
        {
            ref GateTrigger trigger = ref _gateTriggers[i];
            if (trigger.Fired || _objectives[trigger.Objective].State != ObjectiveState.Complete || world.FindEntity(trigger.EntityId) is not { Alive: true }) { continue; }
            trigger.Fired = true; Execute(world, _openGateAction, trigger.EntityId);
        }
    }
    private void HashGateTriggers(ref Fnv1a hash)
    {
        hash.Add(_gateTriggerCount);
        for (int i = 0; i < _gateTriggerCount; i++) { GateTrigger trigger = _gateTriggers[i]; hash.Add(trigger.EntityId); hash.Add(trigger.Objective); hash.Add(trigger.Fired); }
    }
}
