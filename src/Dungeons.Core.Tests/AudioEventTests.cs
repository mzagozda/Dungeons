using Dungeons.Core.Content;
using Dungeons.Core.Level;
using Dungeons.Core.Math;
using Dungeons.Core.Sim;
using Xunit;

namespace Dungeons.Core.Tests;

[Collection("Performance")]
public sealed class AudioEventTests
{
    private static readonly EntityDef Walker = new("test:walker", EntityKind.Player, new(100, 4.5f));
    private static GameSession WalkArena() => new ScenarioBuilder(40, 20).Entity(Walker, new(5, 5)).Build().Session;
    private static readonly PlayerInput East = new(new(1, 0));
    private static void Walk(GameSession session, int ticks)
    { for (int i = 0; i < ticks; i++) { session.Step(East); } }

    [Fact]
    public void FootstepsFollowFifteenMovingTicksAndPausePreservesTheCadence()
    {
        GameSession session = WalkArena(); session.DrainEvents(); Walk(session, 14);
        Assert.DoesNotContain(session.DrainEvents().ToArray(), e => e.Kind == SimEventKind.Footstep);
        Assert.Equal(14, session.World.Players[0].FootstepTicks);
        session.Pause(); Walk(session, 30); Assert.Equal(14, session.Tick); session.Resume(); Walk(session, 1);
        SimEvent first = Assert.Single(session.DrainEvents().ToArray(), e => e.Kind == SimEventKind.Footstep);
        Assert.Equal(15, first.Tick); Assert.Equal(Walker.Id, first.Text); Assert.Equal(session.World.Players[0].Pos, first.Position);
        Assert.Equal(IntVec2.FromPosition(first.Position), first.Cell); Assert.Equal("", first.SurfaceKind);
        Walk(session, 15); Assert.Equal(30, Assert.Single(session.DrainEvents().ToArray(), e => e.Kind == SimEventKind.Footstep).Tick);
        Walk(session, 14); session.Step(default(PlayerInput)); Assert.Equal(0, session.World.Players[0].FootstepTicks);
        Walk(session, 14); Assert.DoesNotContain(session.DrainEvents().ToArray(), e => e.Kind == SimEventKind.Footstep);
        Walk(session, 1); Assert.Single(session.DrainEvents().ToArray(), e => e.Kind == SimEventKind.Footstep);
    }

    [Fact]
    public void WallsRollsAndKnockbackAloneDoNotGenerateFootsteps()
    {
        GameSession blocked = new ScenarioBuilder(20, 20).Cell(6, 5, Cell.Wall).Entity(Walker, new(5.65f, 5.5f)).Build().Session;
        Walk(blocked, 45); Assert.DoesNotContain(blocked.DrainEvents().ToArray(), e => e.Kind == SimEventKind.Footstep);
        GameSession rolling = WalkArena(); Walk(rolling, 14); rolling.DrainEvents();
        rolling.Step(new PlayerInput(new(1, 0), pressed: InputButtons.Roll)); Walk(rolling, 23);
        Assert.DoesNotContain(rolling.DrainEvents().ToArray(), e => e.Kind == SimEventKind.Footstep);
        Assert.Equal(0, rolling.World.Players[0].FootstepTicks);
        GameSession pushed = WalkArena(); pushed.World.Players[0].Knockback = new(2, 0);
        for (int i = 0; i < 30; i++) { pushed.Step(default(PlayerInput)); }
        Assert.True(pushed.World.Players[0].Pos.X > 5); Assert.DoesNotContain(pushed.DrainEvents().ToArray(), e => e.Kind == SimEventKind.Footstep);
    }

    [Fact]
    public void FootstepMetadataRetainsTheBakedSurfaceAndResolvedHeightAfterAuthoringChanges()
    {
        LevelDocument level = MissionAssets.Load("game/levels/mossy_hollow/level.json");
        for (int z = 35; z <= 37; z++) { for (int x = 3; x <= 10; x++) { level.PaintCell(x, z, "core:stone_floor", 2); } }
        var session = new GameSession(level); session.DrainEvents(); Walk(session, 15);
        int hero = session.World.Players[0].Id;
        SimEvent step = Assert.Single(session.DrainEvents().ToArray(), e => e.Kind == SimEventKind.Footstep && e.EntityId == hero);
        Assert.Equal("core:stone_floor", step.SurfaceKind); Assert.Equal(2, step.Y); Assert.Equal("core:hero", step.Text);
        level.PaintCell(step.Cell.X, step.Cell.Z, "core:bridge_planks", 2);
        Assert.Equal("core:stone_floor", step.SurfaceKind); Assert.Equal("core:stone_floor", session.World.SurfaceAt(step.Cell));
        Assert.Equal("core:grass", session.World.SurfaceAt(new(0, 0))); // Composite tree resolves to its visible base floor.
    }

    [Fact]
    public void MissionOnlyCodeSourcesKeepTheirBakedGridAndEmptySurfaceFallback()
    {
        var builder = new ScenarioBuilder(20, 20).Entity(Walker, new(5, 5));
        builder.Document.Source = new System.Text.Json.Nodes.JsonObject(); builder.Document.Content = MissionAssets.Content;
        GameSession session = builder.Build().Session; session.DrainEvents(); Walk(session, 15);
        SimEvent step = Assert.Single(session.DrainEvents().ToArray(), e => e.Kind == SimEventKind.Footstep);
        Assert.Equal("", step.SurfaceKind); Assert.Equal(Walker.Id, step.Text);
    }

    [Fact]
    public void MeleeActiveEventsRetainComboMetadataAndCancelledWindupHasNoWhooshEvent()
    {
        var target = new EntityDef("test:target", EntityKind.Enemy, new(1000, 0));
        GameSession session = new ScenarioBuilder(20, 20).Entity(Walker, new(5, 5)).Entity(target, new(6.5f, 5)).Build().Session;
        var active = new List<SimEvent>(); session.DrainEvents();
        for (int i = 0; i < 120; i++)
        {
            session.Step(new PlayerInput(default, new(1, 0), true, InputButtons.Attack));
            foreach (SimEvent ev in session.DrainEvents()) { if (ev.Kind == SimEventKind.AttackActive) { active.Add(ev); } }
        }
        Assert.Equal(new[] { 1, 2, 3 }, active.Take(3).Select(e => e.Amount));
        Assert.All(active, e => { Assert.Equal(Walker.Id, e.Text); Assert.Equal(new Vec2(1, 0), e.Direction); Assert.True(float.IsFinite(e.Y)); });
        GameSession cancelled = WalkArena(); cancelled.Step(new PlayerInput(default, pressed: InputButtons.Attack)); cancelled.DrainEvents();
        cancelled.Step(new PlayerInput(new(1, 0), pressed: InputButtons.Roll)); Walk(cancelled, 15);
        Assert.DoesNotContain(cancelled.DrainEvents().ToArray(), e => e.Kind == SimEventKind.AttackActive);
    }

    [Fact]
    public void FootstepPhaseParticipatesInHashAndResetsWhenPooledStorageIsReused()
    {
        GameSession a = WalkArena(), b = WalkArena(); Assert.Equal(a.Hash(), b.Hash());
        a.World.Players[0].FootstepTicks = 14; Assert.NotEqual(a.Hash(), b.Hash());
        a.World.Players[0].FootstepTicks = 0; Assert.Equal(a.Hash(), b.Hash());
        var enemy = new EntityDef("test:pooled", EntityKind.Enemy, new(10, 0));
        Entity first = a.World.SpawnEntity(enemy, new(10, 10), Vec2.North)!; first.FootstepTicks = 14;
        a.World.Despawn(first); a.World.RemoveExpired();
        Entity reused = a.World.SpawnEntity(enemy, new(10, 10), Vec2.North)!;
        Assert.Same(first, reused); Assert.Equal(0, reused.FootstepTicks);
    }

    [Fact, Trait("Category", "Performance")]
    public void WalkingEventsAndHashingAllocateZeroBytesAfterWarmup()
    {
        GameSession session = WalkArena();
        for (int i = 0; i < 180; i++) { session.Step(new PlayerInput(new(i % 60 < 30 ? 1 : -1, 0))); session.DrainEvents(); session.Hash(); }
        int steps = 0; long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 600; i++)
        {
            session.Step(new PlayerInput(new(i % 60 < 30 ? 1 : -1, 0))); session.Hash();
            foreach (SimEvent ev in session.DrainEvents()) { if (ev.Kind == SimEventKind.Footstep) { steps++; } }
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(steps > 0); Assert.Equal(0, allocated);
    }

    [Fact]
    public void CollectedPickupRetainsDefinitionAndPositionAfterRemoval()
    {
        var hero = new EntityDef("test:hero", EntityKind.Player, new(100, 4.5f));
        var pickup = new EntityDef("test:gem", EntityKind.Pickup, new(1, 0), .15f, pickup: new(PickupKind.Emerald, 3));
        GameSession session = new ScenarioBuilder(10, 10).Entity(hero, new(5, 5)).Entity(pickup, new(5.1f, 5)).Build().Session;
        session.DrainEvents(); session.Step(default(PlayerInput));
        SimEvent collected = Assert.Single(session.DrainEvents().ToArray(), e => e.Kind == SimEventKind.PickupCollected);
        Assert.Equal(pickup.Id, collected.Text); Assert.Null(session.World.FindEntity(collected.EntityId)); Assert.Equal(3, collected.Amount);
        Assert.True(collected.Position.IsFinite); Assert.True(float.IsFinite(collected.Y));
    }

    [Fact]
    public void RangedWindupAndLaunchRetainTheirActorDefinitionWithoutStateQueries()
    {
        var hero = new EntityDef("test:hero", EntityKind.Player, new(100, 4.5f));
        GameSession session = new ScenarioBuilder(20, 20).Entity(hero, new(5, 5)).Build().Session;
        session.DrainEvents(); session.Step(new PlayerInput(default, new(1, 0), true, InputButtons.Ranged));
        SimEvent started = Assert.Single(session.DrainEvents().ToArray(), e => e.Kind == SimEventKind.AttackStarted);
        Assert.Equal(hero.Id, started.Text); Assert.Equal(1, started.Value);
        for (int i = 0; i < 10; i++) { session.Step(default(PlayerInput)); }
        SimEvent launched = Assert.Single(session.DrainEvents().ToArray(), e => e.Kind == SimEventKind.ProjectileSpawned);
        Assert.Equal(hero.Id, launched.Text); Assert.Equal(session.World.Players[0].Id, launched.SourceId);
    }
}
