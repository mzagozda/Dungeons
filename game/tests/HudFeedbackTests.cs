using System.Text.Json.Nodes;
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
public sealed class HudFeedbackTests
{
    [TestCase, GodotExceptionMonitor]
    public async Task ActualDamagePotionAndBowSnapshotsDriveWarningsSlotsAndPauseWithoutChangingCore()
    {
        var content = ContentBridge.LoadRegistry(); LevelDocument level = Level(content); var session = new GameSession(level, 1);
        var root = new Node3D(); using ISceneRunner runner = ISceneRunner.Load(root, autoFree: true);
        var view = new WorldView(); root.AddChild(view); view.Build(level, content, session);
        var hud = new Hud(); root.AddChild(hud); hud.Build(session, view);
        for (int i = 0; i < 120 && !view.ReadyForPlay; i++) { await runner.AwaitIdleFrame(); }
        AssertThat(view.ReadyForPlay).IsTrue(); view.SetProcess(false); hud.SetProcess(false); await runner.SimulateFrames(2);
        Entity hero = session.World.Players[0];
        for (int i = 0; i < 3000 && hero.Health.Current >= hero.Health.Max / 4; i++) { session.Step(default(PlayerInput)); session.DrainEvents(); }
        AssertThat(hero.Alive && hero.Health.Current > 0 && hero.Health.Current < hero.Health.Max / 4).IsTrue();
        hud._Process(0); ulong hash = session.Hash(); var vignette = hud.GetNode<LowHealthVignette>("LowHealthVignette");
        var health = hud.GetNode<Control>("HudRoot").GetChildren().OfType<ProgressBar>().Single(); var border = (StyleBoxFlat)health.GetThemeStylebox("background");
        AssertThat(vignette.Visible).IsTrue(); Color initial = border.BorderColor; hud._Process(.3); AssertThat(border.BorderColor != initial).IsTrue();
        AssertThat(session.Hash()).IsEqual(hash);
        session.Pause(); hash = session.Hash(); Color frozen = border.BorderColor; hud._Process(.3); AssertThat(border.BorderColor).IsEqual(frozen); AssertThat(session.Hash()).IsEqual(hash);
        session.Resume(); hud.ApplyAccessibility(new AccessibilitySettings { ReduceMotion = true }); hud._Process(0); Color reduced = border.BorderColor;
        hud._Process(.8); AssertThat(border.BorderColor).IsEqual(reduced); AssertThat(vignette.Visible).IsTrue();
        view.Interpolate(1); await runner.SimulateFrames(2); Save(root, "low-health");
        session.Step(new PlayerInput(default, pressed: InputButtons.Potion)); hud._Process(0); AssertThat(vignette.Visible).IsFalse(); AssertThat(border.BorderColor).IsEqual(Colors.White);
        var potion = hud.GetNode<HudActionSlot>("HudRoot/PotionSlot"); var bow = hud.GetNode<HudActionSlot>("HudRoot/BowSlot");
        AssertThat(potion.CounterText).IsEqual("30"); AssertThat(potion.CooldownFraction).IsEqual(1f);
        session.Step(new PlayerInput(default, new(1, 0), true, InputButtons.Ranged)); for (int i = 0; i < 6; i++) { session.Step(default(PlayerInput)); }
        hud._Process(0); AssertThat(bow.CounterText).IsEqual("29");
        hud._Process(0); hash = session.Hash(); view.Interpolate(1); await runner.SimulateFrames(2); Save(root, "potion-bow"); AssertThat(session.Hash()).IsEqual(hash);
    }

    [TestCase, GodotExceptionMonitor]
    public async Task NativeTouchPotionShowsHalfCooldownAtMaximumScaleWithoutDuplicateSlots()
    {
        var content = ContentBridge.LoadRegistry(); var game = new CaptureGameplayScreen(); using ISceneRunner runner = ISceneRunner.Load(game, autoFree: true);
        game.Build(ContentBridge.LoadBuiltins(content)[0], content, 1, touchPreview: true); game.SetProcess(false);
        for (int i = 0; i < 120 && !game.View.ReadyForPlay; i++) { await runner.AwaitIdleFrame(); }
        AssertThat(game.View.ReadyForPlay).IsTrue(); game._Process(0); game.Hud.SetProcess(false);
        Window window = game.GetWindow(); Vector2I canvas = window.ContentScaleSize; var aspect = window.ContentScaleAspect; float scale = window.ContentScaleFactor;
        try
        {
            window.ContentScaleAspect = Window.ContentScaleAspectEnum.Ignore; window.ContentScaleSize = new(1760, 800); window.ContentScaleFactor = 3;
            game.Hud.ApplyAccessibility(new AccessibilitySettings { TextScale = 1.5f });
            game.Touch!.ConfigurePreview(new(880, 400), new(28, 4, 12, 16));
            game.Session.Step(new PlayerInput(default, pressed: InputButtons.Potion));
            for (int i = 0; i < 900; i++) { game.Session.Step(default(PlayerInput)); game.Session.DrainEvents(); }
            await runner.SimulateFrames(4); game.Touch.RefreshMetrics(); game.Hud._Process(0);
            Entity hero = game.Session.World.Players[0]; AssertThat(hero.Alive).IsTrue(); AssertThat(hero.Action.PotionCooldown).IsEqual(900);
            AssertThat(HudCooldown.Fraction(hero.Action.PotionCooldown, hero.Def.Controls.PotionCooldown, hero.Stats.CooldownMultiplier)).IsEqual(.5f);
            AssertThat(game.Hud.GetNode<HudActionSlot>("HudRoot/PotionSlot").Visible).IsFalse();
            ulong hash = game.Session.Hash(); await runner.SimulateFrames(3); Save(game, "phone-half-cooldown"); AssertThat(game.Session.Hash()).IsEqual(hash);
        }
        finally { window.ContentScaleSize = canvas; window.ContentScaleAspect = aspect; window.ContentScaleFactor = scale; }
    }

    [TestCase(1280, 720), TestCase(1920, 1080), TestCase(2560, 1440), GodotExceptionMonitor]
    public async Task HealthGroupScalesFromReferenceAndKeepsFullHealthOutlineAboveFill(int width, int height)
    {
        var content = ContentBridge.LoadRegistry(); LevelDocument level = Level(content); var session = new GameSession(level, 1);
        var root = new Node(); using ISceneRunner runner = ISceneRunner.Load(root, autoFree: true);
        var viewport = new SubViewport { Size = new(width, height), OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always }; root.AddChild(viewport);
        var view = new WorldView(); viewport.AddChild(view); view.Build(level, content, session);
        var hud = new Hud(); viewport.AddChild(hud); hud.Build(session, view);
        for (int i = 0; i < 120 && !view.ReadyForPlay; i++) { await runner.AwaitIdleFrame(); }
        AssertThat(view.ReadyForPlay).IsTrue(); view.SetProcess(false);
        await runner.SimulateFrames(5); hud._Process(0); await runner.SimulateFrames(2);
        var health = hud.GetNode<Control>("HudRoot").GetChildren().OfType<ProgressBar>().Single();
        var potion = hud.GetNode<HudActionSlot>("HudRoot/PotionSlot"); var bow = hud.GetNode<HudActionSlot>("HudRoot/BowSlot");
        float factor = height / 1080f * hud.GetWindow().ContentScaleFactor;
        GD.Print($"HUD_REFERENCE {width}x{height} factor={factor} health={health.Size} slot={potion.Size}");
        Vector2 healthError = (health.Size - new Vector2(400 * factor, 28 * factor)).Abs(), slotError = (potion.Size - Vector2.One * (64 * factor)).Abs();
        AssertThat(healthError.X <= 1 && healthError.Y <= 1).IsTrue();
        AssertThat(slotError.X <= 1 && slotError.Y <= 1).IsTrue();
        Rect2 healthRect = health.GetGlobalRect(), left = potion.GetGlobalRect(), right = bow.GetGlobalRect();
        AssertThat(left.End.X < healthRect.Position.X && right.Position.X > healthRect.End.X).IsTrue();
        AssertThat(left.Position.X >= 0 && right.End.X <= width && left.End.Y <= height && right.End.Y <= height).IsTrue();
        if (DisplayServer.GetName() != "headless")
        {
            using Image image = viewport.GetTexture().GetImage();
            AssertThat(image.GetWidth()).IsEqual(width); AssertThat(image.GetHeight()).IsEqual(height);
            Color pixel = image.GetPixel((int)healthRect.GetCenter().X, (int)MathF.Round(healthRect.Position.Y + 1));
            AssertThat(pixel.R > .95f && pixel.G > .95f && pixel.B > .95f).IsTrue();
        }
        Save(hud, $"health-group-{width}x{height}");
    }

    private static LevelDocument Level(Dungeons.Core.Content.ContentRegistry content)
    {
        JsonObject source = new()
        {
            ["formatVersion"] = 1, ["id"] = "test.hud", ["name"] = "HUD", ["author"] = "Test", ["version"] = "1.0.0",
            ["size"] = new JsonObject { ["width"] = 12, ["height"] = 12 }, ["palette"] = new JsonObject { ["."] = "core:grass" },
            ["layers"] = new JsonObject { ["floor"] = new JsonArray(Enumerable.Range(0, 12).Select(_ => (JsonNode?)JsonValue.Create("............")).ToArray()), ["heights"] = new JsonArray(Enumerable.Range(0, 12).Select(_ => (JsonNode?)JsonValue.Create("000000000000")).ToArray()) },
            ["entities"] = JsonNode.Parse("""[{"id":"spawn","type":"core:player_spawn","x":6,"z":6},{"id":"enemy","type":"core:zombie","x":7.1,"z":6},{"id":"exit","type":"core:exit","x":10,"z":1}]"""),
            ["completion"] = JsonNode.Parse("""{"type":"exit","exit":"exit","requires":[]}""")
        };
        LevelLoadResult loaded = LevelLoader.Load(source.ToJsonString(), content); return loaded.Document ?? throw new InvalidDataException(string.Join('\n', loaded.Diagnostics));
    }
    private static void Save(Node root, string name)
    {
        if (DisplayServer.GetName() == "headless") { return; }
        string folder = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "../artifacts/m8/hud-feedback")); Directory.CreateDirectory(folder);
        using Image image = root.GetViewport().GetTexture().GetImage(); AssertThat(image.SavePng(Path.Combine(folder, name + "-" + RenderingServer.GetCurrentRenderingMethod() + ".png"))).IsEqual(Error.Ok);
    }
}
