using Dungeons.Core.Content;
using Dungeons.Core.Level;
using System.Text.Json.Nodes;
using Dungeons.Core.Sim;
using Dungeons.Game.Persistence;
using Dungeons.Game.Rendering;
using Dungeons.Game.UI;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace Dungeons.Game.Tests;

[TestSuite, RequireGodotRuntime]
public sealed class PickupPresentationTests
{
    [TestCase, GodotExceptionMonitor]
    public async Task CollectedCoreEventsReachHudAfterRemovalAndRemainPresentationOnly()
    {
        var content = ContentBridge.LoadRegistry();
        JsonObject source = new()
        {
            ["formatVersion"] = 1, ["id"] = "test.pickup", ["name"] = "Pickup", ["author"] = "Test", ["version"] = "1.0.0",
            ["size"] = new JsonObject { ["width"] = 12, ["height"] = 12 }, ["palette"] = new JsonObject { ["."] = "core:grass" },
            ["layers"] = new JsonObject { ["floor"] = new JsonArray(Enumerable.Range(0, 12).Select(_ => (JsonNode?)JsonValue.Create("............")).ToArray()),
                ["heights"] = new JsonArray(Enumerable.Range(0, 12).Select(_ => (JsonNode?)JsonValue.Create("000000000000")).ToArray()) },
            ["entities"] = JsonNode.Parse("""[{"id":"spawn","type":"core:player_spawn","x":6,"z":6},{"id":"emerald","type":"core:emerald_pile","x":6.1,"z":6},{"id":"arrows","type":"core:arrow_bundle","x":6,"z":6.1},{"id":"exit","type":"core:exit","x":10,"z":1}]"""),
            ["completion"] = JsonNode.Parse("""{"type":"exit","exit":"exit","requires":[]}""")
        };
        LevelLoadResult loaded = LevelLoader.Load(source.ToJsonString(), content);
        if (!loaded.Success) { throw new InvalidDataException(string.Join('\n', loaded.Diagnostics)); }
        LevelDocument level = loaded.Document!; var session = new GameSession(level, 1);
        var root = new Node3D(); using ISceneRunner runner = ISceneRunner.Load(root, autoFree: true);
        var view = new WorldView(); root.AddChild(view); view.Build(level, content, session);
        var hud = new Hud(); root.AddChild(hud); hud.Build(session, view);
        for (int frame = 0; frame < 120 && !view.ReadyForPlay; frame++) { await runner.AwaitIdleFrame(); }
        await runner.SimulateFrames(2); view.SetProcess(false); hud.SetProcess(false);
        var flights = hud.GetNode<PickupFlights>("HudRoot/PickupFlights"); flights.SetProcess(false);
        session.Step(default(PlayerInput)); SimEvent[] events = session.DrainEvents().ToArray();
        AssertThat(events.Count(ev => ev.Kind == SimEventKind.PickupCollected)).IsEqual(2);
        AssertThat(session.World.Entities.Any(entity => entity.Kind == EntityKind.Pickup && entity.Alive)).IsFalse();
        ulong hash = session.Hash(); hud.ConsumeEvents(events); hud._Process(0); AssertThat(hud.ActivePickupFlights).IsEqual(2);
        hud.ConsumeEvents(new[] {
            new SimEvent(1, SimEventKind.PickupCollected, 99, new(6, 6), -1, 1, text: "core:emerald_pile"),
            new SimEvent(1, SimEventKind.PickupCollected, 99, new(6, 6), session.World.Players[0].Id, 0, text: "core:emerald_pile"),
            new SimEvent(1, SimEventKind.PickupCollected, 99, new(6, 6), session.World.Players[0].Id, 1, text: "missing") });
        AssertThat(hud.ActivePickupFlights).IsEqual(2);
        flights.Advance(.23); await runner.SimulateFrames(2);
        if (DisplayServer.GetName() != "headless")
        {
            string folder = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "../artifacts/m5/pickup")); Directory.CreateDirectory(folder);
            using Image image = root.GetViewport().GetTexture().GetImage();
            AssertThat(image.SavePng(Path.Combine(folder, "flight-" + RenderingServer.GetCurrentRenderingMethod() + ".png"))).IsEqual(Error.Ok);
        }
        flights.Advance(.43); AssertThat(hud.ActivePickupFlights).IsEqual(0);
        for (int i = 0; i < 100; i++) { hud.ConsumeEvents(events); }
        AssertThat(hud.ActivePickupFlights).IsEqual(PickupFlights.Capacity);
        hud.ApplyAccessibility(new AccessibilitySettings { ReduceMotion = true }); AssertThat(hud.ActivePickupFlights).IsEqual(0);
        hud.ConsumeEvents(events); AssertThat(hud.ActivePickupFlights).IsEqual(0); AssertThat(session.Hash()).IsEqual(hash);
        GD.Print($"PICKUP_FLIGHT=PASS renderer={RenderingServer.GetCurrentRenderingMethod()} coreHash={hash:x16}");
    }

    [TestCase, GodotExceptionMonitor]
    public async Task CanvasPoolRejectsOffscreenPositionsAndTracksLiveMirroredTarget()
    {
        var root = new Control(); using ISceneRunner runner = ISceneRunner.Load(root, autoFree: true);
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var flights = new PickupFlights(); root.AddChild(flights); Vector2 target = new(100, 100); int queries = 0;
        flights.Bind(_ => { queries++; return target; }); flights.SetProcess(false); await runner.SimulateFrames(2);
        int children = flights.GetChildCount();
        flights.Collect(PickupKind.Emerald, new(-50, -50)); flights.Collect(PickupKind.Food, new(float.NaN, 10));
        target = new(float.NaN, 10); flights.Collect(PickupKind.Arrows, new(200, 200)); AssertThat(flights.ActiveCount).IsEqual(0);
        target = new(100, 100); flights.Collect(PickupKind.Arrows, new(200, 200)); AssertThat(flights.ActiveCount).IsEqual(1);
        target = new(500, 100); int before = queries; flights.Advance(.3); await runner.SimulateFrames(2);
        if (DisplayServer.GetName() != "headless") { AssertThat(queries).IsGreater(before); }
        AssertThat(flights.GetChildCount()).IsEqual(children); flights.Advance(1); AssertThat(flights.ActiveCount).IsEqual(0);
    }
}
