using Dungeons.Core.Content;
using Dungeons.Core.Math;
using Dungeons.Core.Sim;
using Dungeons.Game.Input;
using Dungeons.Game.Persistence;
using Xunit;

namespace Dungeons.Core.Tests;

public sealed class SettingsInputPolicyTests
{
    private static GameSession Session() => new ScenarioBuilder(30, 30).Entity(new EntityDef("test:hero", EntityKind.Player, new(100, 4.5f)), new(10, 10))
        .Entity(new EntityDef("test:enemy", EntityKind.Enemy, new(100, 0)), new(10, 7.5f)).Entity(new EntityDef("test:enemy", EntityKind.Enemy, new(100, 0)), new(10.3f, 9.37f)).Build().Session;
    [Fact]
    public void StrongAssistPrefersCloserTargetsWithoutChangingNormalAimOrManualCommands()
    {
        GameSession session = Session(); Entity hero = session.World.Players[0]; ulong hash = session.Hash();
        Assert.Equal(Vec2.North, AutoAim.Resolve(session.World, hero, false));
        Vec2 strong = AutoAim.Resolve(session.World, hero, false, strong: true); Assert.Equal((session.World.Entities[2].Pos - hero.Pos).Normalized, strong);
        PlayerInput attack = new(default, pressed: InputButtons.Attack), manual = new(default, Vec2.North, true, InputButtons.Ranged);
        Assert.Equal(attack, AimAssistPolicy.Apply(session.World, attack, AimAssist.Normal, false)); Assert.Equal(manual, AimAssistPolicy.Apply(session.World, manual, AimAssist.Strong, true));
        PlayerInput assisted = AimAssistPolicy.Apply(session.World, attack, AimAssist.Strong, true); Assert.True(assisted.AimValid); Assert.InRange((strong - assisted.Aim).Length, 0, .000001f);
        Assert.Equal(hash, session.Hash()); Assert.Equal(default, AimAssistPolicy.Apply(session.World, default, AimAssist.Strong, false));
    }
    [Fact]
    public void OffDisablesOptionalAssistButTouchKeepsNormalAssistAndReplayRecordsTheChoice()
    {
        GameSession session = Session(); session.World.Players[0].Facing = new(1, 0); PlayerInput attack = new(default, pressed: InputButtons.Attack);
        Assert.False(AimAssistPolicy.Apply(session.World, attack, AimAssist.Off, true).AimValid);
        PlayerInput disabled = AimAssistPolicy.Apply(session.World, attack, AimAssist.Off, false); Assert.True(disabled.AimValid); Assert.Equal(new Vec2(1, 0), disabled.Aim);
        var recorder = new ReplayRecorder(session.World.Seed, session.World.LevelId, session.Hash(), 32);
        session.Step(disabled); recorder.Record([disabled], session.Hash());
        GameSession replay = Session(); replay.World.Players[0].Facing = new(1, 0);
        Assert.True(ReplayVerifier.Verify(replay, recorder.Capture()).Success);
    }
    [Fact]
    public void StrongCursorSnapHasWiderRadiusAndStillRespectsLineOfSight()
    {
        GameSession session = Session(); Entity hero = session.World.Players[0]; session.World.Entities[2].Alive = false;
        Vec2 cursor = new(11.5f, 7.5f); Assert.Equal((cursor - hero.Pos).Normalized, AutoAim.ResolveCursor(session.World, hero, cursor));
        Assert.Equal(Vec2.North, AutoAim.ResolveCursor(session.World, hero, cursor, strong: true));
    }
    [Fact]
    public void LayoutFitAndTransactionalPositionsProtectBoundsGapsAndMirroring()
    {
        Assert.Equal(1.5f, TouchLayout.FitScale(new(1000, 700), 1.5f)); Assert.InRange(TouchLayout.FitScale(new(700, 300), 1.5f), 1.36f, 1.364f); Assert.Equal(0, TouchLayout.FitScale(new(250, 150), 1));
        var right = new TouchLayout(); right.Configure(new(880, 400), new(20, 12), new(20, 12));
        TouchTarget[] before = right.Targets.ToArray();
        Assert.False(right.TrySetPositions([new(InputButtons.Attack, new(.5f, .5f)), new(InputButtons.Roll, new(.5f, .5f))])); Assert.Equal(before, right.Targets.ToArray());
        TouchPlacement[] positions = [new(InputButtons.Potion, new(.5f, .2f))]; Assert.True(right.TrySetPositions(positions));
        var left = new TouchLayout(); left.Configure(new(880, 400), new(20, 12), new(20, 12), leftHanded: true); Assert.True(left.TrySetPositions(positions));
        Assert.Equal(880 - right.Targets[3].Centre.X, left.Targets[3].Centre.X); Assert.Equal(right.Targets[3].Centre.Z, left.Targets[3].Centre.Z);
    }
}
