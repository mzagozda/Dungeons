using System.Text.Json.Nodes;
using Dungeons.Core.Content;
using Dungeons.Core.Level;
using Dungeons.Core.Sim;
using Dungeons.Game.Persistence;
using Dungeons.Game.Rendering;
using Dungeons.Game.UI;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace Dungeons.Game.Tests;

[TestSuite, RequireGodotRuntime]
public sealed class MissionHudTests
{
    [TestCase, GodotExceptionMonitor]
    public async Task AuthoredObjectivesMessagesCheckpointAndAiReadActualStateWithoutChangingCore()
    {
        var content = ContentBridge.LoadRegistry(); LevelDocument level = Fixture(content); var session = new GameSession(level, 1);
        var root = new Node3D(); using ISceneRunner runner = ISceneRunner.Load(root, autoFree: true);
        var view = new WorldView(); root.AddChild(view); view.Build(level, content, session);
        var hud = new Hud(); root.AddChild(hud); hud.Build(session, view); hud.SetProcess(false);
        await Ready(runner, view); hud._Process(0);
        var objectives = hud.GetNode<HudObjectives>("HudRoot/Objectives"); var message = hud.GetNode<HudBanner>("HudRoot/MissionMessage"); var notice = hud.GetNode<HudBanner>("HudRoot/CentreNotice");
        AssertThat(hud.ObjectiveText).IsEqual("Defeat the ambush"); AssertThat(objectives.SecondaryCount).IsEqual(1); AssertThat(hud.ExitLockVisible).IsTrue();
        ulong hash = session.Hash(); hud._Process(.1); AssertThat(message.Modulate.A).IsEqual(.5f); AssertThat(session.Hash()).IsEqual(hash);
        hud._Process(.1); AssertThat(message.Modulate.A).IsEqual(1f);
        session.Step(default(PlayerInput)); SimEvent[] events = session.DrainEvents().ToArray(); AssertThat(events.Any(ev => ev.Kind == SimEventKind.CheckpointActivated)).IsTrue();
        hud.ConsumeEvents(events); hud._Process(.1); AssertThat(notice.TextLabel.Text).IsEqual(hud.Tr("HUD_CHECKPOINT"));
        session.Pause(); hash = session.Hash(); float alpha = notice.Modulate.A; hud._Process(1); AssertThat(notice.Modulate.A).IsEqual(alpha); AssertThat(session.Hash()).IsEqual(hash); session.Resume();
        await runner.SimulateFrames(3); Save(hud, "checkpoint"); hud._Process(3.1);
        AssertThat(notice.TextLabel.Text.Contains("Defeat the ambush", StringComparison.Ordinal)).IsTrue();
        for (; session.Tick < 54;) { session.Step(default(PlayerInput)); }
        hud.ConsumeEvents(session.DrainEvents()); hud._Process(0);
        AssertThat(hud.ObjectiveText).IsEqual("Find the hidden chest"); AssertThat(objectives.SecondaryCount).IsEqual(1); AssertThat(message.Modulate.A).IsEqual(.5f);
        for (; session.Tick < 60;) { session.Step(default(PlayerInput)); }
        hud._Process(0); AssertThat(message.Visible).IsFalse(); AssertThat(notice.Visible).IsFalse();
        hud.ToggleDebugOverlay(); hud._Process(0); AssertThat(hud.GetNode<Label>("HudRoot/DebugStats").Text.Contains("AI Idle 1", StringComparison.Ordinal)).IsTrue();
        hash = session.Hash(); await runner.SimulateFrames(3); Save(hud, "secondary-and-debug"); AssertThat(session.Hash()).IsEqual(hash);
    }

    [TestCase, GodotExceptionMonitor]
    public async Task AllSecondaryObjectivesRemainAvailableInBoundedScrollableArea()
    {
        var content = ContentBridge.LoadRegistry(); LevelDocument level = Fixture(content, many: true); var session = new GameSession(level, 1);
        var root = new Node3D(); using ISceneRunner runner = ISceneRunner.Load(root, autoFree: true);
        var view = new WorldView(); root.AddChild(view); view.Build(level, content, session);
        var hud = new Hud(); root.AddChild(hud); hud.Build(session, view); hud.SetProcess(false); await Ready(runner, view);
        hud._Process(.2); await runner.SimulateFrames(4); hud._Process(0);
        var objectives = hud.GetNode<HudObjectives>("HudRoot/Objectives"); AssertThat(objectives.SecondaryCount).IsEqual(30);
        var scroll = objectives.GetNode<ScrollContainer>("SecondaryObjectives"); AssertThat(scroll.Visible).IsTrue(); scroll.ScrollVertical = 100000;
        await runner.SimulateFrames(3); AssertThat(scroll.ScrollVertical).IsGreater(0);
        Label last = objectives.FindChildren("Secondary_31", "Label", true, false).OfType<Label>().Single();
        AssertThat(last.GetGlobalRect().Intersects(scroll.GetGlobalRect())).IsTrue(); Save(hud, "many-objectives");
    }

    [TestCase(1280, 720, false), TestCase(1920, 1080, false), TestCase(2560, 1440, false), TestCase(1760, 800, true), GodotExceptionMonitor]
    public async Task HeaderBannersAndCountersFitReferenceSizesAndMaximumScalePhone(int width, int height, bool phone)
    {
        var content = ContentBridge.LoadRegistry(); LevelDocument level = Fixture(content); var session = new GameSession(level, 1);
        var root = new Node(); using ISceneRunner runner = ISceneRunner.Load(root, autoFree: true);
        var viewport = new SubViewport { Size = new(width, height), OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always }; root.AddChild(viewport);
        Window window = root.GetWindow(); float oldScale = window.ContentScaleFactor;
        try
        {
            if (phone) { viewport.Size2DOverride = new(width / 3, height / 3); viewport.Size2DOverrideStretch = true; window.ContentScaleFactor = 3; }
            var view = new WorldView(); viewport.AddChild(view); view.Build(level, content, session);
            var hud = new Hud(); viewport.AddChild(hud); hud.Build(session, view); hud.SetProcess(false); hud.UsesTouch = phone;
            if (phone) { hud.ApplyAccessibility(new AccessibilitySettings { TextScale = 1.5f }); hud.ApplySafeInsets(new(28 / 1.5f, 4 / 1.5f), new(12 / 1.5f, 16 / 1.5f), 24); }
            await Ready(runner, view); hud._Process(.2); await runner.SimulateFrames(5); hud._Process(0); await runner.SimulateFrames(2);
            Control frame = hud.GetNode<Control>("HudRoot"); var objectives = hud.GetNode<HudObjectives>("HudRoot/Objectives");
            GD.Print($"MISSION_HUD {width}x{height} phone={phone} viewport={viewport.GetVisibleRect().Size} frame={frame.GetGlobalRect()} objectives={objectives.GetGlobalRect()}");
            AssertThat(objectives.Primary.MaxLinesVisible).IsEqual(2); AssertThat(objectives.SecondaryCount).IsEqual(1);
            var notice = hud.GetNode<HudBanner>("HudRoot/CentreNotice"); var icon = notice.FindChildren("ExitLock", "Control", true, false).OfType<Control>().Single();
            if (icon.IsVisibleInTree()) { AssertThat(icon.GlobalPosition.X + icon.Size.X * icon.Scale.X <= notice.TextLabel.GetGlobalRect().Position.X + 1).IsTrue(); }
            CheckBounds(frame); Save(hud, $"layout-{width}x{height}");
        }
        finally { window.ContentScaleFactor = oldScale; }
    }

    internal static void CheckBounds(Control frame)
    {
        ProgressBar health = frame.GetChildren().OfType<ProgressBar>().Single(); Rect2 area = frame.GetGlobalRect();
        foreach (Label label in frame.FindChildren("*", "Label", true, false).OfType<Label>().Where(label => label.IsVisibleInTree() && label.Text.Length > 0 && !health.IsAncestorOf(label)))
        {
            Rect2 rect = label.GetGlobalRect(); Node? parent = label.GetParent();
            while (parent is Control control && parent != frame) { if (control.ClipContents) { rect = rect.Intersection(control.GetGlobalRect()); } parent = parent.GetParent(); }
            if (!rect.HasArea()) { continue; }
            AssertThat(rect.Position.X >= area.Position.X - 1 && rect.End.X <= area.End.X + 1 && rect.End.Y <= health.GetGlobalRect().Position.Y + 1).IsTrue();
        }
        HudCounters counters = frame.GetChildren().OfType<HudCounters>().Single(); Rect2 bounds = counters.GetGlobalRect();
        AssertThat(bounds.Position.X >= area.Position.X - 1 && bounds.End.X <= area.End.X + 1).IsTrue();
    }
    private static async Task Ready(ISceneRunner runner, WorldView view)
    { for (int i = 0; i < 120 && !view.ReadyForPlay; i++) { await runner.AwaitIdleFrame(); } AssertThat(view.ReadyForPlay).IsTrue(); view.SetProcess(false); await runner.SimulateFrames(2); }
    private static LevelDocument Fixture(ContentRegistry content, bool many = false)
    {
        JsonObject source = new()
        {
            ["formatVersion"] = 1, ["id"] = "test.mission_hud", ["name"] = "Mission HUD", ["author"] = "Test", ["version"] = "1.0.0",
            ["size"] = new JsonObject { ["width"] = 12, ["height"] = 12 }, ["palette"] = new JsonObject { ["."] = "core:grass" },
            ["layers"] = new JsonObject { ["floor"] = new JsonArray(Enumerable.Range(0, 12).Select(_ => (JsonNode?)JsonValue.Create("............")).ToArray()), ["heights"] = new JsonArray(Enumerable.Range(0, 12).Select(_ => (JsonNode?)JsonValue.Create("000000000000")).ToArray()) },
            ["entities"] = JsonNode.Parse("""[{"id":"spawn","type":"core:player_spawn","x":6,"z":6},{"id":"checkpoint","type":"core:checkpoint","x":6.1,"z":6},{"id":"enemy","type":"core:zombie","x":1,"z":1,"props":{"patrolRadius":0}},{"id":"exit","type":"core:exit","x":8.5,"z":6}]"""),
            ["flags"] = JsonNode.Parse("""{"ready":false,"side":false,"third":false}"""),
            ["objectives"] = JsonNode.Parse("""[{"id":"primary","text":"Defeat the ambush","type":"flag","flag":"ready","initialState":"active"},{"id":"side","text":"Find the hidden chest","type":"flag","flag":"side","initialState":"active","optional":true},{"id":"third","text":"Explore the old ruins","type":"flag","flag":"third","initialState":"hidden","optional":true}]"""),
            ["completion"] = JsonNode.Parse("""{"type":"exit","exit":"exit","requires":["primary"]}"""),
            ["triggers"] = JsonNode.Parse("""[{"id":"welcome","event":{"type":"level_start"},"actions":[{"type":"message","text":"Prepare for the next fight","seconds":1}]},{"id":"advance","event":{"type":"timer","seconds":0.5},"actions":[{"type":"set_flag","flag":"ready","value":true},{"type":"objective","id":"third","state":"active"}]}]""")
        };
        if (many) { for (int i = 0; i < 29; i++) { source["objectives"]!.AsArray().Add(new JsonObject { ["id"] = "extra" + i, ["text"] = "Optional goal " + i + ": explore the forest and ruins", ["type"] = "flag", ["flag"] = "side", ["initialState"] = "active", ["optional"] = true }); } }
        LevelLoadResult loaded = LevelLoader.Load(source.ToJsonString(), content); return loaded.Document ?? throw new InvalidDataException(string.Join('\n', loaded.Diagnostics));
    }
    private static void Save(Node root, string name)
    {
        if (DisplayServer.GetName() == "headless") { return; }
        string folder = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "../artifacts/m8/mission-hud")); Directory.CreateDirectory(folder);
        using Image image = root.GetViewport().GetTexture().GetImage(); AssertThat(image.SavePng(Path.Combine(folder, name + "-" + RenderingServer.GetCurrentRenderingMethod() + ".png"))).IsEqual(Error.Ok);
    }
}
