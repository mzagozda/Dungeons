using System.Text.Json;
using Dungeons.Core.Content;
using Dungeons.Core.Level;
using Dungeons.Core.Sim;
using Dungeons.Game.Persistence;
using Dungeons.Game.UI;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace Dungeons.Game.Tests;

[TestSuite, RequireGodotRuntime]
public sealed class LaunchCommandTests
{
    [TestCase(false), TestCase(true), GodotExceptionMonitor]
    public async Task ExternalJsonAndPackageLaunchTheExactDocumentWithAnExplicitSeed(bool package)
    {
        ContentRegistry content = ContentBridge.LoadRegistry(); LevelDocument level = ContentBridge.LoadBuiltins(content)[0];
        string folder = Temp(); string source = Path.Combine(folder, "Résumé Ω level"); Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "level.json"), level.ToJsonObject().ToJsonString());
        string path = source;
        if (package)
        {
            path = Path.Combine(folder, "Résumé Ω.dlvl"); using var stream = File.Create(path);
            LevelPackage.Create([("level.json", (ReadOnlyMemory<byte>)File.ReadAllBytes(Path.Combine(source, "level.json")))]).Package!.Write(stream);
        }
        LevelLoadResult direct = RuntimeLevelFiles.Load(path, content); AssertThat(direct.Success).IsTrue();
        var app = new AppRoot(Path.Combine(folder, "profile"), ["--level", path, "--seed=42"]);
        using ISceneRunner runner = ISceneRunner.Load(app, autoFree: true); await AppTestReady.Wait(runner, app);
        GameplayScreen game = app.GetNode<GameplayScreen>("Gameplay"); game.SetProcess(false); await Ready(runner, game);
        AssertThat(game.Session.World.SourceHash).IsEqual(direct.Document!.SourceHash); AssertThat(game.Session.World.Seed).IsEqual(42UL);
        AssertThat(game.Session.Recorder is not null).IsTrue(); AssertThat(game.IsReplay).IsFalse();
        var profile = new RunStore(Path.Combine(folder, "profile")).Profile;
        AssertThat(profile.Levels[level.Id].Attempts).IsEqual(1); AssertThat(profile.Levels[level.Id].LastSeed).IsEqual(42UL);
    }

    [TestCase(false), TestCase(true), GodotExceptionMonitor]
    public async Task ReplayUsesCommandsIgnoresPlayerActionsAndLeavesEvenUnreadableProfilesUntouched(bool corruptProfile)
    {
        ContentRegistry content = ContentBridge.LoadRegistry(); LevelDocument level = ContentBridge.LoadBuiltins(content)[0]; string folder = Temp();
        ReplayDocument replay = Record(level, content); string path = Path.Combine(folder, "Résumé Ω.dreplay"); File.WriteAllText(path, ReplayJson.Write(replay));
        var profile = new PlayerProfile { RunCounter = 9, TotalEmeralds = 71 }; profile.Levels[level.Id] = new() { Attempts = 3 };
        string profilePath = Path.Combine(folder, "profile.json");
        File.WriteAllText(profilePath, corruptProfile ? "{broken profile" : JsonSerializer.Serialize(profile, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        byte[] before = File.ReadAllBytes(profilePath);
        var app = new AppRoot(folder, ["--replay", path]); using ISceneRunner runner = ISceneRunner.Load(app, autoFree: true); await AppTestReady.Wait(runner, app);
        GameplayScreen game = app.GetNode<GameplayScreen>("Gameplay"); game.SetProcess(false); await Ready(runner, game);
        AssertThat(game.IsReplay).IsTrue(); AssertThat(game.Session.Recorder is null).IsTrue();
        if (game.Session.State == SessionState.Paused) { game._UnhandledInput(new InputEventAction { Action = "pause", Pressed = true }); }
        game._UnhandledInput(new InputEventAction { Action = "pause", Pressed = true }); int tick = game.Session.Tick;
        game._Process(.25); AssertThat(game.Session.Tick).IsEqual(tick);
        game._UnhandledInput(new InputEventAction { Action = "pause", Pressed = true });
        game._UnhandledInput(new InputEventAction { Action = "potion", Pressed = true });
        for (int frame = 0; frame < 30 && !game.ReplayComplete; frame++) { game._Process(.25); }
        AssertThat(game.ReplayComplete).IsTrue(); AssertThat(game.Session.Tick).IsEqual(120); AssertThat(game.Session.Hash()).IsEqual(replay.Snapshot.FinalHash);
        game._Process(1); AssertThat(game.Session.Tick).IsEqual(120);
        AssertThat(File.ReadAllBytes(profilePath).SequenceEqual(before)).IsTrue(); AssertThat(Directory.GetFiles(folder, "profile.json*").Length).IsEqual(1);
        AssertThat(Directory.Exists(Path.Combine(folder, "replays"))).IsFalse();
        if (!corruptProfile)
        {
            await runner.SimulateFrames(2);
            if (DisplayServer.GetName() != "headless") { using Image image = app.GetViewport().GetTexture().GetImage(); image.SavePng(Path.Combine(folder, "replay-complete.png")); }
            game.GetNode("ReplayStatus").FindChildren("*", "Button", true, false).OfType<Button>().Single().EmitSignal(BaseButton.SignalName.Pressed);
            await runner.SimulateFrames(4);
            var browser = app.FindChildren("LevelBrowser", "ScrollContainer", true, false).OfType<LevelBrowserView>().Single();
            browser.Cards.Single(card => card.Entry.Id == level.Id).SelectButton.EmitSignal(BaseButton.SignalName.Pressed); await runner.SimulateFrames(3);
            app.FindChildren("*", "Button", true, false).OfType<Button>().First(button => button.IsVisibleInTree() && button.Text == app.Tr("PLAY")).EmitSignal(BaseButton.SignalName.Pressed);
            await runner.SimulateFrames(3); GameplayScreen normal = app.GetNode<GameplayScreen>("Gameplay");
            AssertThat(normal.IsReplay).IsFalse(); AssertThat(normal.Session.Recorder is not null).IsTrue();
            AssertThat(new RunStore(folder).Profile.Levels[level.Id].Attempts).IsEqual(4);
        }
    }

    [TestCase, GodotExceptionMonitor]
    public async Task RecordingThatEndsWhileDeadKeepsItsBackButtonAboveTheDeathOverlay()
    {
        ContentRegistry content = ContentBridge.LoadRegistry(); var source = ContentBridge.LoadBuiltins(content)[0].ToJsonObject();
        var enemy = source["entities"]!.AsArray().Single(entity => entity!["id"]!.GetValue<string>() == "z_bank_1")!; enemy["x"] = 5.5; enemy["z"] = 36.5;
        LevelLoadResult loaded = LevelLoader.Load(source.ToJsonString(), content); AssertThat(loaded.Success).IsTrue(); LevelDocument level = loaded.Document!;
        var session = GameSession.Create(level, content, 7, record: true);
        for (int tick = 0; tick < 2400 && session.World.Players[0].Alive; tick++) { session.Step(default(PlayerInput)); }
        AssertThat(session.World.Players[0].Alive).IsFalse(); AssertThat(session.State).IsEqual(SessionState.Running);
        var replay = new ReplayDocument(session.Recorder!.Capture(), level.SourceHash, content.Hash);
        var game = new Rendering.CaptureGameplayScreen(); using ISceneRunner runner = ISceneRunner.Load(game, autoFree: true);
        game.BuildReplay(level, content, replay); game.SetProcess(false); await Ready(runner, game);
        for (int frame = 0; frame < 500 && !game.ReplayComplete; frame++) { game._Process(.25); }
        AssertThat(game.ReplayComplete).IsTrue(); AssertThat(game.Session.Hash()).IsEqual(replay.Snapshot.FinalHash);
        AssertThat(game.DeathVisible).IsFalse();
        await runner.SimulateFrames(3); var status = game.GetNode<CanvasLayer>("ReplayStatus");
        if (DisplayServer.GetName() != "headless") { using Image image = game.GetViewport().GetTexture().GetImage(); image.SavePng(Path.Combine(Temp(), "dead-boundary.png")); }
        AssertThat(status.Layer).IsGreater(game.GetNode<CanvasLayer>("DeathScreen").Layer);
        Button back = status.FindChildren("*", "Button", true, false).OfType<Button>().Single(); int quit = 0; game.QuitRequested += () => quit++;
        if (DisplayServer.GetName() == "headless") { back.EmitSignal(BaseButton.SignalName.Pressed); }
        else { runner.SimulateMouseMove(back.GetGlobalRect().GetCenter()).SimulateMouseButtonPressed(MouseButton.Left); await runner.SimulateFrames(2); }
        AssertThat(quit).IsEqual(1);
    }

    [TestCase, GodotExceptionMonitor]
    public async Task ViewerHonoursRecordedDifficultyAndEditorOriginAndRejectsFingerprintMismatchBeforeBuilding()
    {
        ContentRegistry content = ContentBridge.LoadRegistry(); LevelDocument level = ContentBridge.LoadBuiltins(content)[0];
        ReplayDocument replay = Record(level, content, custom: true); var game = new Rendering.CaptureGameplayScreen();
        using ISceneRunner runner = ISceneRunner.Load(game, autoFree: true); game.BuildReplay(level, content, replay); game.SetProcess(false); await Ready(runner, game);
        AssertThat(game.Session.StartOverride).IsEqual(replay.Snapshot.StartOverride); AssertThat(game.Session.World.Difficulty.HpMultiplier).IsEqual(1.3f);
        for (int frame = 0; frame < 30 && !game.ReplayComplete; frame++) { game._Process(.25); }
        AssertThat(game.ReplayComplete).IsTrue(); AssertThat(game.Session.Hash()).IsEqual(replay.Snapshot.FinalHash);
        var rejected = new GameplayScreen(); bool failed = false;
        try { rejected.BuildReplay(level, content, new(replay.Snapshot, new string('0', 64), content.Hash)); }
        catch (InvalidDataException) { failed = true; }
        AssertThat(failed).IsTrue(); AssertThat(rejected.GetChildCount()).IsEqual(0); rejected.Free();
    }

    private static ReplayDocument Record(LevelDocument level, ContentRegistry content, bool custom = false)
    {
        var origin = custom ? new StartOverride(GameSession.Create(level, content, 7).World.Players[0].Pos) : null;
        var session = GameSession.Create(level, content, 7, custom ? new DifficultyProfile(1.3f, 1.2f, 1.2f, 32) : null, record: true, startOverride: origin);
        for (int tick = 0; tick < 120; tick++) { session.Step(tick == 0 ? new PlayerInput(default, pressed: InputButtons.Potion) : default); }
        return new(session.Recorder!.Capture(), level.SourceHash, content.Hash);
    }
    private static string Temp()
    { string path = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "../artifacts/m8/launch-native", Guid.NewGuid().ToString("N"))); Directory.CreateDirectory(path); return path; }
    private static async Task Ready(ISceneRunner runner, GameplayScreen game)
    {
        for (int frame = 0; frame < 180 && !game.View.ReadyForPlay; frame++)
        {
            // These cases intentionally use the production focus handler. Resume
            // unrelated desktop focus loss so it cannot suspend mesh upload.
            if (game.Session.State == SessionState.Paused) { game._UnhandledInput(new InputEventAction { Action = "pause", Pressed = true }); }
            await runner.AwaitIdleFrame();
        }
        AssertThat(game.View.ReadyForPlay).IsTrue(); await runner.SimulateFrames(2);
    }
}
