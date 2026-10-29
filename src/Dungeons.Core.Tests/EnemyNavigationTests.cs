using Dungeons.Core.Content;
using Dungeons.Core.Math;
using Dungeons.Core.Sim;
using Xunit;

namespace Dungeons.Core.Tests;

public sealed class EnemyNavigationTests
{
    private static readonly EntityDef Hero = new("test:hero", EntityKind.Player, new(1000000, 4.5f));
    private static readonly EntityDef Enemy = new("test:enemy", EntityKind.Enemy, new(40, 12),
        weapons: new(new WeaponDef("test:claws", new MeleeSwing[] { new(27, 6, 42, 8, 1, 45, 1.4f) })), brain: new(BrainKind.MeleeChaser, 400, 400));
    private static readonly Cell Fence = new(walkable: false, solid: true, blocksProjectiles: true);
    private static void Tick(GameSession session) { session.Step(default(PlayerInput)); session.DrainEvents(); }

    [Fact]
    public void UnreachableGoalRetriesAfterSixtyTicksAndNewOpeningRestoresChase()
    {
        var builder = new ScenarioBuilder(20, 20).Entity(Hero, new(15.5f, 10.5f)).Entity(Enemy, new(5.5f, 10.5f));
        for (int z = 0; z < 20; z++) { builder.Cell(10, z, Fence); }
        GameSession session = builder.Build().Session; Entity enemy = session.World.Entities[1]; bool retried = false; int previousRetry = 0;
        for (int tick = 0; tick < 220; tick++)
        {
            Tick(session); if (enemy.Ai.RetryTicks == 60) { retried = true; }
            if (previousRetry > 1) { Assert.Equal(previousRetry - 1, enemy.Ai.RetryTicks); }
            previousRetry = enemy.Ai.RetryTicks; Assert.True(enemy.Pos.X < 10);
        }
        Assert.True(retried); session.World.Grid.SetRuntimeCell(10, 10, Cell.Floor);
        for (int tick = 0; tick < 180 && enemy.Action.Phase == AttackPhase.Idle; tick++) { Tick(session); }
        Assert.True(enemy.Pos.X > 10); Assert.Equal(AttackPhase.Windup, enemy.Action.Phase);
    }

    [Fact]
    public void TruncatedSerpentineRoutesContinueUntilTheRealTarget()
    {
        var builder = new ScenarioBuilder(256, 10).Entity(Hero, new(250.5f, 6.5f)).Entity(Enemy, new(2.5f, 2.5f));
        for (int z = 0; z < 10; z++) { for (int x = 0; x < 256; x++) { builder.Cell(x, z, Fence); } }
        foreach (int z in new[] { 2, 4, 6 }) { for (int x = 2; x <= 253; x++) { builder.Cell(x, z, Cell.Floor); } }
        builder.Cell(253, 3, Cell.Floor).Cell(2, 5, Cell.Floor);
        GameSession session = builder.Build().Session; Entity enemy = session.World.Entities[1]; bool truncated = false;
        for (int tick = 0; tick < 5000 && enemy.Action.Phase == AttackPhase.Idle; tick++)
        { Tick(session); truncated |= enemy.PathTruncated; Assert.True(enemy.Pos.IsFinite); }
        Assert.True(truncated); Assert.Equal(AttackPhase.Windup, enemy.Action.Phase);
        Assert.InRange((enemy.Pos - session.World.Players[0].Pos).Length, 0, 1.76f);
    }

    [Fact]
    public void MovingTargetRepathsAfterFifteenTicksAndNearestPlayerTiesUseSlotOrder()
    {
        GameSession session = new ScenarioBuilder(30, 20).Entity(Hero, new(20.5f, 5.5f)).Entity(Hero, new(20.5f, 15.5f))
            .Entity(Enemy, new(5.5f, 10.5f)).Build().Session;
        var inputs = new PlayerInput[2]; Entity enemy = session.World.Entities[2];
        for (int i = 0; i < 30; i++) { session.Step(inputs); }
        Assert.Equal(session.World.Players[0].Id, enemy.Ai.TargetId); IntVec2 original = enemy.Ai.RouteGoal;
        session.World.Players[0].Pos += new Vec2(3, 0);
        for (int i = 0; i < 20; i++) { session.Step(inputs); }
        // The other player is now closer, and both target selection and route update follow it.
        Assert.Equal(session.World.Players[1].Id, enemy.Ai.TargetId); Assert.NotEqual(original, enemy.Ai.RouteGoal);
        Assert.Equal(IntVec2.FromPosition(session.World.Players[1].Pos), enemy.Ai.RouteGoal);
    }
}
