using System.Text.Json.Nodes;
using Dungeons.Core.Content;
using Dungeons.Game.Audio;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace Dungeons.Game.Tests;

[TestSuite, RequireGodotRuntime]
public sealed class AudioTests
{
    [TestCase, GodotExceptionMonitor]
    public void ShutdownStopsAndDetachesMusicUiAmbienceAndSpatialStreams()
    {
        var manager = new AudioManager(); using ISceneRunner runner = ISceneRunner.Load(manager, autoFree: true);
        manager.Initialize(new(ContentBridge.LoadRegistry())); manager.ChangeMusic("core:music_forest");
        manager.Play("core:sfx_ui_hover"); manager.Play("core:amb_forest_night"); manager.Play("core:sfx_torch_loop", new(1, 0, 0));
        AssertThat(manager.ActiveVoiceCount).IsEqual(3); manager.StopForShutdown();
        AssertThat(manager.ActiveVoiceCount).IsEqual(0); AssertThat(manager.Play("core:sfx_ui_click")).IsFalse();
        AssertThat(manager.GetChildren().OfType<AudioStreamPlayer>().All(player => !player.Playing && player.Stream is null)).IsTrue();
        AssertThat(manager.GetChildren().OfType<AudioStreamPlayer3D>().All(player => !player.Playing && player.Stream is null)).IsTrue();
        manager.StopForShutdown(); manager._Process(1); // ExitTree can call the same cleanup again safely.
    }

    [TestCase, GodotExceptionMonitor]
    public void PreloadedPositionalVoicesRespectTheirCapAndStealTheOldest()
    {
        var manager = new AudioManager(); using ISceneRunner runner = ISceneRunner.Load(manager, autoFree: true);
        manager.Initialize(new(ContentBridge.LoadRegistry())); manager.SetProcess(false);
        AssertThat(manager.PreparedVoiceCount).IsEqual(152);
        for (int i = 1; i <= 5; i++) { AssertThat(manager.Play("core:sfx_roll", new(i, 0, 0))).IsTrue(); }
        AssertThat(manager.ActiveVoices("core:sfx_roll")).IsEqual(4);
        var voices = manager.GetChildren().OfType<AudioStreamPlayer3D>().Where(p => p.Playing).ToArray();
        AssertThat(voices.Length).IsEqual(4);
        AssertThat(voices.All(p => p.UnitSize == 8 && p.MaxDistance == 30 && p.Bus == "SFX")).IsTrue();
        AssertThat(voices.Any(p => p.Position.X == 1)).IsFalse();
        AssertThat(voices.Select(p => p.Position.X).Order().SequenceEqual(new float[] { 2, 3, 4, 5 })).IsTrue();
        AssertThat(manager.Play("core:sfx_ui_click")).IsTrue();
        AssertThat(manager.GetChildren().OfType<AudioStreamPlayer>().Any(p => p.Bus == "UI" && p.Playing)).IsTrue();
        manager.StopWorld(); AssertThat(manager.ActiveVoices("core:sfx_roll")).IsEqual(0); AssertThat(manager.ActiveVoices("core:sfx_ui_click")).IsEqual(1);
        manager.Stop("core:sfx_ui_click"); AssertThat(manager.ActiveVoices("core:sfx_ui_click")).IsEqual(0);
    }

    [TestCase, GodotExceptionMonitor]
    public void MusicCrossfadesForOneSecondDucksWithoutChangingUserVolumeAndSuspendsInBackground()
    {
        JsonObject audio = JsonNode.Parse(ContentBridge.ReadText("res://content/audio.json"))!.AsObject();
        audio["test:second_music"] = audio["core:music_forest"]!.DeepClone();
        ContentRegistry registry = ContentRegistry.Load([("audio.json", audio.ToJsonString())]);
        var manager = new AudioManager(); using ISceneRunner runner = ISceneRunner.Load(manager, autoFree: true);
        manager.Initialize(new(registry)); manager.SetProcess(false);
        int bus = AudioServer.GetBusIndex("Music"); float volume = AudioServer.GetBusVolumeDb(bus);
        manager.ChangeMusic("core:music_forest"); manager._Process(.5); AssertThat(manager.MusicGain(0)).IsEqual(.5f);
        manager._Process(.5); manager.ChangeMusic("core:music_forest"); AssertThat(manager.MusicGain(0)).IsEqual(1f);
        manager.ChangeMusic("test:second_music"); manager._Process(.5);
        AssertThat(manager.MusicGain(0)).IsEqual(.5f); AssertThat(manager.MusicGain(1)).IsEqual(.5f);
        manager.SetDucked(true); AssertThat(manager.MusicDuckDb).IsEqual(-6f); AssertThat(AudioServer.GetBusVolumeDb(bus)).IsEqual(volume);
        manager._Notification((int)Node.NotificationApplicationPaused); manager._Process(2);
        AssertThat(manager.BackgroundPaused).IsTrue(); AssertThat(manager.MusicGain(0)).IsEqual(.5f);
        AssertThat(manager.GetChildren().OfType<AudioStreamPlayer>().Where(p => p.Playing).All(p => p.StreamPaused)).IsTrue();
        AssertThat(manager.Play("core:sfx_ui_click")).IsFalse();
        manager._Notification((int)Node.NotificationApplicationResumed); manager._Process(.5);
        AssertThat(manager.BackgroundPaused).IsFalse(); AssertThat(manager.MusicGain(0)).IsEqual(0f); AssertThat(manager.MusicGain(1)).IsEqual(1f);
        manager.SetDucked(false); AssertThat(manager.MusicDuckDb).IsEqual(0f);
    }

    [TestCase, GodotExceptionMonitor]
    public async Task RealGameplayEventsRouteToVoicesWithoutChangingSimulationAndLoopsStayBounded()
    {
        var root = new Node3D(); using ISceneRunner runner = ISceneRunner.Load(root, autoFree: true);
        var content = ContentBridge.LoadRegistry(); JsonObject source = ContentBridge.LoadBuiltins(content)[0].ToJsonObject();
        JsonNode enemy = source["entities"]!.AsArray().Single(entity => entity!["id"]!.GetValue<string>() == "z_bank_1")!; enemy["x"] = 5.5; enemy["z"] = 36.5;
        var loaded = Dungeons.Core.Level.LevelLoader.Load(source.ToJsonString(), content); AssertThat(loaded.Success).IsTrue(); var level = loaded.Document!;
        var audio = new AudioManager(); root.AddChild(audio); audio.Initialize(new(content));
        var game = new GameplayScreen(); root.AddChild(game); game.Build(level, content, 42); game.SetProcess(false);
        for (int i = 0; i < 120 && !game.View.ReadyForPlay; i++) { await runner.AwaitIdleFrame(); }
        AssertThat(game.View.ReadyForPlay).IsTrue(); game._Process(0);
        var hero = game.Session.World.Players[0]; var director = game.GetChildren().OfType<GameplayAudio>().Single(); director.SetProcess(false);
        game.Session.Step(new Dungeons.Core.Sim.PlayerInput(default, pressed: Dungeons.Core.Sim.InputButtons.Attack));
        ulong hash = game.Session.Hash(); game._Process(0);
        AssertThat(audio.ActiveVoices("core:sfx_sword_1")).IsEqual(0); AssertThat(game.Session.Hash()).IsEqual(hash);
        for (int i = 0; i < 6; i++) { game.Session.Step(default(Dungeons.Core.Sim.PlayerInput)); game._Process(0); }
        hash = game.Session.Hash(); game._Process(0);
        AssertThat(audio.ActiveVoices("core:sfx_sword_1")).IsEqual(1); AssertThat(game.Session.Hash()).IsEqual(hash);
        for (int i = 0; i < 2400 && hero.Health.Current == hero.Health.Max; i++)
        { game.Session.Step(default(Dungeons.Core.Sim.PlayerInput)); hash = game.Session.Hash(); game._Process(0); AssertThat(game.Session.Hash()).IsEqual(hash); }
        director._Process(.5);
        AssertThat(audio.ActiveVoices("core:sfx_hero_hurt")).IsEqual(1); AssertThat(game.Session.Hash()).IsEqual(hash);
        AssertThat(audio.MusicId).IsEqual("core:music_forest"); AssertThat(audio.ActiveVoices("core:amb_forest_night")).IsEqual(1);
        AssertThat(audio.ActiveVoices("core:sfx_torch_loop") <= 4).IsTrue();
        for (int i = 0; i < 2400 && hero.Alive; i++) { game.Session.Step(default(Dungeons.Core.Sim.PlayerInput)); game._Process(0); }
        game._Process(0); director._Process(0); AssertThat(hero.Alive).IsFalse(); AssertThat(audio.MusicDuckDb).IsEqual(-6f);
        var nextOwner = new object(); audio.BeginWorld(nextOwner); audio.Play("core:amb_forest_night");
        director._ExitTree(); AssertThat(audio.ActiveVoices("core:amb_forest_night")).IsEqual(1); // Retiring scene cannot stop its replacement.
        audio.EndWorld(nextOwner); AssertThat(audio.ActiveVoices("core:amb_forest_night")).IsEqual(0);
    }

    [TestCase, GodotExceptionMonitor]
    public async Task CoreFootstepsSelectSoftStoneAndWoodAtTheEmittedCadence()
    {
        foreach ((string tile, string sound) in new[] { ("core:grass", "core:sfx_step_soft"), ("core:stone_floor", "core:sfx_step_stone"), ("core:bridge_planks", "core:sfx_step_wood") })
        {
            var root = new Node3D(); using ISceneRunner runner = ISceneRunner.Load(root, autoFree: true);
            var content = ContentBridge.LoadRegistry(); var level = ContentBridge.LoadBuiltins(content)[0];
            for (int z = 35; z <= 37; z++) { for (int x = 3; x <= 10; x++) { level.PaintCell(x, z, tile, 0); } }
            var audio = new AudioManager(); root.AddChild(audio); audio.Initialize(new(content));
            var game = new GameplayScreen(); root.AddChild(game); game.Build(level, content, 42); game.SetProcess(false);
            for (int i = 0; i < 120 && !game.View.ReadyForPlay; i++) { await runner.AwaitIdleFrame(); }
            AssertThat(game.View.ReadyForPlay).IsTrue(); game._Process(0);
            var director = game.GetChildren().OfType<GameplayAudio>().Single(); director.SetProcess(false);
            for (int i = 0; i < 14; i++) { game.Session.Step(new Dungeons.Core.Sim.PlayerInput(new(1, 0))); game._Process(0); }
            AssertThat(audio.ActiveVoices(sound)).IsEqual(0);
            game.Session.Step(new Dungeons.Core.Sim.PlayerInput(new(1, 0)));
            var events = game.Session.DrainEvents().ToArray();
            var step = events.Single(e => e.Kind == Dungeons.Core.Sim.SimEventKind.Footstep && e.EntityId == game.Session.World.Players[0].Id);
            AssertThat(step.SurfaceKind).IsEqual(tile); ulong hash = game.Session.Hash(); director.Consume(events);
            AssertThat(audio.ActiveVoices(sound)).IsEqual(1); AssertThat(game.Session.Hash()).IsEqual(hash);
        }
    }

    [TestCase, GodotExceptionMonitor]
    public async Task VorbisPlaybackProducesFiniteNonSilentMixedPcm()
    {
        var manager = new AudioManager(); using ISceneRunner runner = ISceneRunner.Load(manager, autoFree: true);
        manager.Initialize(new(ContentBridge.LoadRegistry())); manager.SetProcess(false);
        int bus = AudioServer.GetBusIndex("UI"), effectIndex = AudioServer.GetBusEffectCount(bus);
        bool muted = AudioServer.IsBusMute(bus); float volume = AudioServer.GetBusVolumeDb(bus);
        var capture = new AudioEffectCapture { BufferLength = .5f }; AudioServer.AddBusEffect(bus, capture);
        try
        {
            AudioServer.SetBusMute(bus, false); AudioServer.SetBusVolumeDb(bus, 0);
            manager.Play("core:sfx_ui_back");
            for (int i = 0; i < 60 && capture.GetFramesAvailable() < 8192; i++) { await runner.AwaitIdleFrame(); }
            int count = capture.GetFramesAvailable(); AssertThat(count > 0).IsTrue();
            Vector2[] frames = capture.GetBuffer(count);
            AssertThat(frames.All(frame => float.IsFinite(frame.X) && float.IsFinite(frame.Y))).IsTrue();
            AssertThat(frames.Any(frame => MathF.Abs(frame.X) > .001f || MathF.Abs(frame.Y) > .001f)).IsTrue();
        }
        finally { AudioServer.RemoveBusEffect(bus, effectIndex); AudioServer.SetBusMute(bus, muted); AudioServer.SetBusVolumeDb(bus, volume); }
    }
}
