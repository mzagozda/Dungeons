using System.Text.Json.Nodes;
using Dungeons.Core.Sim;
using Dungeons.Core.Util;
using Xunit;

namespace Dungeons.Core.Tests;

// Keep the exact allocation probe isolated from parallel stress/fuzz authoring work.
[Collection("Performance")]
public sealed class MissionStateTests
{
    private static GameSession Create(JsonObject? source)
    {
        var builder = new ScenarioBuilder(8, 8).Entity(new Dungeons.Core.Content.EntityDef("test:hero", EntityKind.Player, new(100, 4.5f)), new(2.5f, 2.5f));
        builder.Document.Source = source; return builder.Build().Session;
    }
    [Fact]
    public void StartupActionsActivateRealObjectivesRespectDelayAndExpireMessages()
    {
        JsonObject source = JsonNode.Parse("""
            {"objectives":[{"id":"a","text":{"en":"Cross the river"}},{"id":"b","text":"Second","initialState":"active"}],
             "triggers":[{"event":{"type":"level_start"},"actions":[
               {"type":"objective","id":"a","state":"active"},{"type":"message","text":"Welcome","seconds":0.2},
               {"type":"delay","seconds":0.1},{"type":"objective","id":"a","state":"complete"}]}]}
            """)!.AsObject();
        GameSession session = Create(source); MissionState mission = session.World.Mission;
        var before = new Fnv1a(); mission.Hash(ref before);
        Assert.Equal("Cross the river", mission.PrimaryText); Assert.Equal(0, mission.Objectives[0].ActivatedTick);
        Assert.Equal("Welcome", mission.Message); Assert.Equal(12, mission.MessageTicksRemaining);
        for (int tick = 1; tick <= 5; tick++) { session.Step(default(PlayerInput)); Assert.Equal(ObjectiveState.Active, mission.Objectives[0].State); }
        session.Step(default(PlayerInput)); Assert.Equal(ObjectiveState.Complete, mission.Objectives[0].State); Assert.Equal("Second", mission.PrimaryText);
        var after = new Fnv1a(); mission.Hash(ref after); Assert.NotEqual(before.Value, after.Value);
        for (int tick = 7; tick <= 12; tick++) { session.Step(default(PlayerInput)); }
        Assert.Empty(mission.Message); Assert.Equal(0, mission.MessageTicksRemaining);
        session.Step(default(PlayerInput)); Assert.Equal(ObjectiveState.Complete, mission.Objectives[0].State);
    }
    [Fact]
    public void DisabledStartupDoesNotRunAndSteadyMissionTicksAllocateNothing()
    {
        GameSession session = Create(JsonNode.Parse("""{"triggers":[{"enabled":false,"event":{"type":"level_start"},"actions":[{"type":"message","text":"Never"}]}]}""")!.AsObject());
        MissionState mission = session.World.Mission;
        for (int i = 1; i <= 1000; i++) { session.Step(default(PlayerInput)); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 1001; i <= 2000; i++) { session.Step(default(PlayerInput)); }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before); Assert.Empty(mission.PrimaryText); Assert.Empty(mission.Message);
        GameSession empty = Create(null); empty.Step(default(PlayerInput)); Assert.Empty(empty.World.Mission.Objectives);
    }
}
