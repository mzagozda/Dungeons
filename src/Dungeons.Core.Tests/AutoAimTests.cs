using Dungeons.Core.Content;
using Dungeons.Core.Math;
using Dungeons.Core.Sim;
using Xunit;

namespace Dungeons.Core.Tests;

public sealed class AutoAimTests
{
    private static readonly EntityDef Hero = new("test:hero", EntityKind.Player, new(100, 4.5f));
    private static readonly EntityDef Enemy = new("test:enemy", EntityKind.Enemy, new(100, 0));

    [Fact]
    public void AutoAimUsesRangeScoreStableIdTiesAndBackwardFallback()
    {
        GameSession session = new ScenarioBuilder(30, 30).Entity(Hero, new(10, 10)).Entity(Enemy, new(10, 12)).Entity(Enemy, new(11, 8)).Entity(Enemy, new(9, 8)).Build().Session;
        Entity hero = session.World.Players[0];
        Assert.Equal(new Vec2(1, -2).Normalized, AutoAim.Resolve(session.World, hero, false));
        session.World.Entities[2].Alive = session.World.Entities[3].Alive = false;
        Assert.Equal(new Vec2(0, 1), AutoAim.Resolve(session.World, hero, false));
        session.World.Entities[1].Pos = new(10, 14);
        Assert.Equal(Vec2.North, AutoAim.Resolve(session.World, hero, false));
        Assert.Equal(new Vec2(0, 1), AutoAim.Resolve(session.World, hero, true));
        session.World.Entities[1].Alive = false; Assert.Equal(Vec2.North, AutoAim.Resolve(session.World, hero, true));
    }

    [Fact]
    public void AutoAimRejectsOccludedAndFriendlyTargetsAndLeadsMovingHostile()
    {
        var friendly = new EntityDef("test:friend", EntityKind.Enemy, new(100, 0), faction: Faction.Hero);
        GameSession session = new ScenarioBuilder(30, 30).Entity(Hero, new(10.5f, 10.5f)).Entity(Enemy, new(10.5f, 7.5f)).Entity(friendly, new(10.5f, 9.5f))
            .Cell(10, 8, Cell.Wall).Build().Session;
        Entity hero = session.World.Players[0]; hero.Facing = new(1, 0);
        Assert.Equal(hero.Facing, AutoAim.Resolve(session.World, hero, true));
        session.World.Entities[1].Pos = new(14.5f, 10.5f); session.World.Entities[1].Vel = new(0, 2);
        Vec2 expected = new Vec2(4, 2 * 4f / 14).Normalized;
        Assert.InRange((expected - AutoAim.Resolve(session.World, hero, true)).Length, 0, .000001f);
        session.Step(new PlayerInput(default, pressed: InputButtons.Ranged)); Assert.InRange((expected - hero.Facing).Length, 0, .000001f);
    }

    [Fact]
    public void SightSupercoversCornersInterpolatesHeightAndRejectsInvalidSegments()
    {
        var builder = new ScenarioBuilder(10, 10).Cell(2, 1, Cell.Wall); NavGrid grid = builder.Document.BakeGrid();
        Assert.False(TerrainSight.Visible(grid, new(1.5f, 1.5f), .9f, new(3.5f, 3.5f), .9f));
        Assert.True(TerrainSight.Visible(grid, new(1.5f, 3.5f), .9f, new(3.5f, 3.5f), .9f));
        Assert.True(TerrainSight.Visible(grid, new(1.5f, 1.5f), .9f, new(1.6f, 1.6f), .9f));
        Assert.False(TerrainSight.Visible(grid, new(float.NaN, 1), .9f, new(2, 2), .9f));
        Assert.False(TerrainSight.Visible(grid, new(-1, 1), .9f, new(2, 2), .9f));
        grid = new ScenarioBuilder(10, 10).Cell(2, 1, new Cell(height: 1)).Document.BakeGrid();
        Assert.False(TerrainSight.Visible(grid, new(1.5f, 1.5f), .9f, new(3.5f, 1.5f), .9f));
        Assert.True(TerrainSight.Visible(grid, new(1.5f, 1.5f), 2, new(3.5f, 1.5f), 2));
    }

    [Fact]
    public void MouseSnapRequiresCursorProximityAndVisibility()
    {
        GameSession session = new ScenarioBuilder(30, 30).Entity(Hero, new(10, 10)).Entity(Enemy, new(14, 10)).Build().Session;
        Entity hero = session.World.Players[0];
        Assert.Equal(new Vec2(1, 0), AutoAim.ResolveCursor(session.World, hero, new(14, 11)));
        Assert.Equal(new Vec2(4, 2).Normalized, AutoAim.ResolveCursor(session.World, hero, new(14, 12)));
        session.World.Entities[1].Alive = false;
        Assert.Equal(new Vec2(4, 1).Normalized, AutoAim.ResolveCursor(session.World, hero, new(14, 11)));
        Assert.Equal(hero.Facing, AutoAim.ResolveCursor(session.World, hero, new(float.NaN, 0)));
    }
}
