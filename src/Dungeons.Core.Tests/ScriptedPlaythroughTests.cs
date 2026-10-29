using Dungeons.Core.Level;
using Dungeons.Core.Math;
using Dungeons.Core.Sim;
using Xunit;
using Xunit.Abstractions;

namespace Dungeons.Core.Tests;

[Collection("Performance")]
public sealed class ScriptedPlaythroughTests(ITestOutputHelper output)
{
    [Fact]
    public void ScriptedPlaythroughCompletesTheUnmodifiedMissionUsingOnlyRecordedPlayerCommands()
    {
        LevelDocument level = MissionAssets.Load("game/levels/mossy_hollow/level.json");
        var session = new GameSession(level, 1, record: true); var driver = new MossyDriver(session, level);
        while (session.State == SessionState.Running && session.Tick < 18000)
        {
            session.Step(driver.Command()); session.DrainEvents();
            if (session.Tick % 600 == 0) { Entity hero = session.World.Players[0]; output.WriteLine($"tick={session.Tick} stop={driver.Stop} pos={hero.Pos} hp={hero.Health.Current} kills={session.World.Kills} lives={session.World.Lives} arrows={hero.Inventory.Arrows}"); }
        }
        Entity player = session.World.Players[0]; output.WriteLine($"end={session.State} tick={session.Tick} stop={driver.Stop} pos={player.Pos} hp={player.Health.Current} kills={session.World.Kills}");
        Assert.Equal(SessionState.Won, session.State); Assert.Equal(15, session.World.Kills);
        Assert.All(session.World.Mission.Objectives, objective => Assert.Equal(ObjectiveState.Complete, objective.State));
        Assert.All(session.World.Entities.Where(e => e.Kind == EntityKind.Chest), chest => Assert.True(chest.WorldObject.Opened));
        Assert.Equal(3, session.World.CheckpointOrder); Assert.NotNull(session.World.Mission.Results);
        ReplaySnapshot replay = session.Recorder!.Capture(); var playback = new GameSession(level, 1); ReplayVerification result = ReplayVerifier.Verify(playback, replay);
        Assert.True(result.Success, result.Message); Assert.Equal(session.Hash(), playback.Hash()); Assert.Equal(SessionState.Won, playback.State);
        string path = Path.Combine(MissionAssets.Root, "artifacts/m6/mossy-hollow-scripted.dreplay"); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, ReplayJson.Write(new(replay, level.SourceHash, MissionAssets.Content.Hash))); output.WriteLine("Recorded candidate replay: " + path);
        SaveCandidate("mossy_hollow_full_clear", level, replay);
    }

    [Fact]
    public void AmbushCandidateStopsAtTheAuthoredGateOpeningAndReplaysExactly()
    {
        LevelDocument level = MissionAssets.Load("game/levels/mossy_hollow/level.json"); var session = new GameSession(level, 1, record: true); var driver = new MossyDriver(session, level);
        while (session.State == SessionState.Running && session.Tick < 18000 && session.World.Mission.Objectives.Single(objective => objective.Id == "obj_ambush").State != ObjectiveState.Complete)
        { session.Step(driver.Command()); session.DrainEvents(); }
        Assert.Equal(SessionState.Running, session.State); Assert.Equal(ObjectiveState.Complete, session.World.Mission.Objectives.Single(objective => objective.Id == "obj_ambush").State);
        Entity gate = session.World.Entities.Single(entity => entity.LevelId == "gate_main"); Assert.Equal(GatePhase.Opening, gate.WorldObject.Gate); Assert.Equal(12, session.World.Kills);
        ReplaySnapshot replay = session.Recorder!.Capture(); Assert.True(ReplayVerifier.Verify(new(level, 1), replay).Success); SaveCandidate("mossy_hollow_ambush_only", level, replay);
        output.WriteLine($"Ambush candidate: ticks={session.Tick}, kills={session.World.Kills}, gate={gate.WorldObject.Gate}, hash={session.Hash():x16}");
    }

    [Fact]
    public void DeathRespawnCandidateUsesOnlyMovementAndIdleCommands()
    {
        LevelDocument level = MissionAssets.Load("game/levels/mossy_hollow/level.json"); var session = new GameSession(level, 1, record: true); bool respawned = false;
        while (session.State == SessionState.Running && session.Tick < 3000 && !respawned)
        {
            session.Step(session.Tick < 80 ? new PlayerInput(new(0, -1)) : default);
            foreach (SimEvent ev in session.DrainEvents()) { if (ev.Kind == SimEventKind.PlayerRespawned) { respawned = true; } }
        }
        Assert.True(respawned); Assert.Equal(1, session.World.Deaths); Assert.Equal(2, session.World.Lives); Assert.True(session.World.Players[0].Alive); Assert.Equal(60, session.World.Players[0].Health.InvulnTicks);
        ReplaySnapshot replay = session.Recorder!.Capture(); Assert.True(ReplayVerifier.Verify(new(level, 1), replay).Success); SaveCandidate("mossy_hollow_death_respawn", level, replay);
        output.WriteLine($"Death/respawn candidate: ticks={session.Tick}, deaths={session.World.Deaths}, hash={session.Hash():x16}");
    }

    [Fact, Trait("Category", "Performance")]
    public void FullMissionReplayIncludingHashVerificationAllocatesNothingAndMeetsTickBudget()
    {
        LevelDocument level = MissionAssets.Load("game/levels/mossy_hollow/level.json"); var recorded = new GameSession(level, 1, record: true); var driver = new MossyDriver(recorded, level);
        while (recorded.State == SessionState.Running && recorded.Tick < 18000) { recorded.Step(driver.Command()); recorded.DrainEvents(); }
        Assert.Equal(SessionState.Won, recorded.State); ReplaySnapshot replay = recorded.Recorder!.Capture(); Assert.True(ReplayVerifier.Verify(new(level, 1), replay).Success);
        var measured = new GameSession(level, 1); measured.Hash(); long before = GC.GetAllocatedBytesForCurrentThread(), start = System.Diagnostics.Stopwatch.GetTimestamp();
        ReplayVerification result = ReplayVerifier.Verify(measured, replay); double ms = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds / replay.TickCount;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"Full Mossy Hollow replay: {replay.TickCount} ticks, {ms:F4} ms/tick including hashes, {allocated} allocated bytes.");
        Assert.True(result.Success, result.Message); Assert.Equal(0, allocated); Assert.True(ms <= 1.5, $"Replay averaged {ms:F4} ms/tick.");
    }

    private static void SaveCandidate(string name, LevelDocument level, ReplaySnapshot replay)
    {
        string directory = Path.Combine(MissionAssets.Root, "artifacts/m6/candidates"); Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name + ".dreplay"), ReplayJson.Write(new(replay, level.SourceHash, MissionAssets.Content.Hash)));
    }
}

// Test input policy: no entity writes, artificial damage, terrain edits or tuning changes.
internal sealed class MossyDriver
{
    private readonly GameSession _session;
    private readonly AStar _pathfinder;
    private readonly IntVec2[] _path = new IntVec2[256];
    private readonly (Vec2 point, string id)[] _stops;
    private int _pathCount, _waypoint, _repath, _dwell;
    private Vec2 _lastGoal;
    internal int Stop { get; private set; }
    internal MossyDriver(GameSession session, LevelDocument level)
    {
        _session = session; _pathfinder = new(session.World.Grid);
        string[] ids = ["chest_pocket", "em_start", "cp_bridge", "em_corridor", "arrows_nook_e", "em_nook_w", "cp_clearing", "cp_gate", "chest_ruin", "food_ruin", "em_ruin", "exit_main"];
        _stops = ids.Select(id => (level.Entities.Single(e => e.Id == id).Position, id)).ToArray();
    }
    internal PlayerInput Command()
    {
        Entity hero = _session.World.Players[0]; if (!hero.Alive) { return default; }
        Entity? threat = null; float nearest = float.MaxValue;
        for (int i = 0; i < _session.World.Entities.Count; i++)
        {
            Entity enemy = _session.World.Entities[i]; if (enemy.Kind != EntityKind.Enemy || !enemy.Alive) { continue; }
            float distance = (enemy.Pos - hero.Pos).Length;
            bool visible = TerrainSight.Visible(_session.World.Grid, hero.Pos, hero.Y + .9f, enemy.Pos, enemy.Y + .9f);
            if (distance < nearest && (distance < 10 && visible || Stop == _stops.Length - 1)) { threat = enemy; nearest = distance; }
        }
        Vec2 goal = _stops[System.Math.Min(Stop, _stops.Length - 1)].point; float speed = .22f;
        if (threat is not null) { goal = threat.Pos; speed = 1; }
        else if ((hero.Pos - goal).Length < .8f)
        {
            if (++_dwell >= 60 && (Stop != 7 || _session.World.Mission.Objectives.Single(o => o.Id == "obj_ambush").State == ObjectiveState.Complete))
            { Stop = System.Math.Min(Stop + 1, _stops.Length - 1); _dwell = 0; goal = _stops[Stop].point; }
        }
        else { _dwell = 0; }
        Vec2 move = AvoidProps(hero, Follow(hero, goal), goal) * speed;
        if ((hero.Pos - goal).Length < (threat is null ? .7f : 1.15f)) { move = default; }
        InputButtons buttons = InputButtons.None; Vec2 aim = threat is null ? hero.Facing : (threat.Pos - hero.Pos).Normalized;
        if (hero.Health.Current <= 45 && hero.Action.PotionCooldown == 0) { buttons |= InputButtons.Potion; }
        if (threat is not null)
        {
            if (nearest <= 1.95f) { buttons |= InputButtons.Attack; }
            else if (threat.Def.Brain?.Kind == Dungeons.Core.Content.BrainKind.Archer && nearest <= 10 && hero.Inventory.Arrows > 0)
            { aim = (threat.Pos + threat.Vel * (nearest / 14) - hero.Pos).Normalized; buttons |= InputButtons.Ranged; }
            if (hero.Health.Current < 45 && nearest < 2.3f && threat.Action.Phase == AttackPhase.Windup && _session.Tick - threat.Action.StartedTick >= 20 && hero.Action.RollCooldown == 0)
            { buttons |= InputButtons.Roll; move = new(-aim.Z, aim.X); }
        }
        return new(move, aim, true, buttons);
    }
    private Vec2 Follow(Entity hero, Vec2 goal)
    {
        if (_repath-- <= 0 || (goal - _lastGoal).LengthSquared > 1)
        { PathResult route = _pathfinder.FindPath(IntVec2.FromPosition(hero.Pos), IntVec2.FromPosition(goal), _path); _pathCount = route.Count; _waypoint = 0; _repath = 15; _lastGoal = goal; }
        while (_waypoint < _pathCount && (hero.Pos - _path[_waypoint].Center).Length < .25f) { _waypoint++; }
        return ((_waypoint < _pathCount ? _path[_waypoint].Center : goal) - hero.Pos).Normalized;
    }
    private Vec2 AvoidProps(Entity hero, Vec2 direction, Vec2 goal)
    {
        for (int i = 0; i < _session.World.Entities.Count; i++)
        {
            Entity obstacle = _session.World.Entities[i];
            if (!obstacle.Alive || !obstacle.Solid || obstacle.Kind == EntityKind.Gate || obstacle.Id == hero.Id) { continue; }
            if ((obstacle.Pos - goal).LengthSquared < .01f) { continue; }
            float clearance = hero.Radius + obstacle.Radius + .06f;
            if ((hero.Pos + direction * .6f - obstacle.Pos).LengthSquared >= clearance * clearance) { continue; }
            Vec2 away = (hero.Pos - obstacle.Pos).Normalized, tangent = new(-away.Z, away.X);
            if (Vec2.Dot(tangent, direction) < 0) { tangent = -tangent; }
            direction = (tangent + away * .15f).Normalized;
        }
        return direction;
    }
}
