using Dungeons.Core.Level;
using Dungeons.Core.Sim;
using Dungeons.Game.Input;
using Dungeons.Game.Persistence;
using Dungeons.Game.UI;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace Dungeons.Game.Tests;

[TestSuite, RequireGodotRuntime]
public sealed class MobileServicesTests
{
    private sealed record TestPaths(string UserLevelsDir) : IPlatformPaths;
    private sealed class TestHaptics : IHaptics
    {
        internal int Stops, Plays;
        public void Play(HapticPattern pattern) => Plays++;
        public void Stop() => Stops++;
    }
    [TestCase, GodotExceptionMonitor]
    public async Task InjectedPlatformDirectoryFeedsTheActualCatalogueWithoutMovingTheProfile()
    {
        string prefix = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dungeons-platform-"));
        string root = prefix + Guid.NewGuid().ToString("N"), profile = System.IO.Path.Combine(root, "profile"), shared = System.IO.Path.Combine(root, "documents", "levels");
        string levelFolder = System.IO.Path.Combine(shared, "custom"); System.IO.Directory.CreateDirectory(levelFolder);
        var level = ContentBridge.LoadBuiltins(ContentBridge.LoadRegistry())[0].ToJsonObject(); level["id"] = "test.platform_paths";
        System.IO.File.WriteAllText(System.IO.Path.Combine(levelFolder, "level.json"), level.ToJsonString());
        try
        {
            var app = new AppRoot(profile, new TestPaths(shared));
            using ISceneRunner runner = ISceneRunner.Load(app, autoFree: true); await AppTestReady.Wait(runner, app);
            app.FindChildren("*", "Button", true, false).OfType<Button>().Single(button => button.IsVisibleInTree() && button.Text == app.Tr("PLAY")).EmitSignal(BaseButton.SignalName.Pressed);
            await runner.SimulateFrames(4);
            LevelBrowserView browser = app.FindChildren("LevelBrowser", "ScrollContainer", true, false).OfType<LevelBrowserView>().Single();
            AssertThat(browser.Cards.Single(card => card.Entry.Id == "test.platform_paths").Entry.IsPlayable).IsTrue();
            AssertThat(System.IO.Directory.Exists(profile)).IsTrue();
            AssertThat(System.IO.Directory.Exists(System.IO.Path.Combine(profile, "levels"))).IsFalse();
        }
        finally
        {
            if (!System.IO.Path.GetFullPath(root).StartsWith(prefix, StringComparison.Ordinal)) { throw new InvalidOperationException("Unsafe test cleanup directory."); }
            System.IO.Directory.Delete(root, recursive: true);
        }
    }
    [TestCase, GodotExceptionMonitor]
    public async Task InjectedHapticsStopOnFocusLossSettingsDisableAndActualSceneExit()
    {
        var haptics = new TestHaptics(); var root = new Node();
        using ISceneRunner runner = ISceneRunner.Load(root, autoFree: true);
        var game = new GameplayScreen(haptics); root.AddChild(game);
        var content = ContentBridge.LoadRegistry(); game.Build(ContentBridge.LoadBuiltins(content)[0], content, 42); game.SetProcess(false);
        for (int frame = 0; frame < 120 && !game.View.ReadyForPlay; frame++) { await runner.AwaitIdleFrame(); }
        AssertThat(game.View.ReadyForPlay).IsTrue();
        ulong hash = game.Session.Hash(); int stops = haptics.Stops;
        game._Notification((int)Node.NotificationApplicationFocusOut);
        AssertThat(game.Session.State).IsEqual(SessionState.Paused); AssertThat(haptics.Stops).IsEqual(stops + 1);
        game._Notification((int)Node.NotificationWMGoBackRequest);
        AssertThat(game.Session.Hash()).IsEqual(hash);
        game.ApplySettings(new GameSettings { Controls = new() { Vibration = false } });
        AssertThat(haptics.Stops).IsEqual(stops + 3); AssertThat(haptics.Plays).IsEqual(0);
        game.QueueFree(); await runner.SimulateFrames(2);
        AssertThat(haptics.Stops).IsEqual(stops + 4);
    }
    [TestCase, GodotExceptionMonitor]
    public void GodotSelectedPackageIsImportedAndListedFromThePlatformFolder()
    {
        var content = ContentBridge.LoadRegistry(); var level = ContentBridge.LoadBuiltins(content)[0].ToJsonObject();
        level["id"] = "test.picker_import"; level["name"] = "Picker import";
        string root = ProjectSettings.GlobalizePath("user://import-test-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(root); string source = root.PathJoin("selected.dlvl"), destination = root.PathJoin("levels"); string? imported = null;
        try
        {
            var package = LevelPackage.Create([("level.json", (ReadOnlyMemory<byte>)System.Text.Encoding.UTF8.GetBytes(level.ToJsonString())), ("assets/reserved.bin", new byte[] { 3, 8 })]);
            using (var file = System.IO.File.Create(source)) { package.Package!.Write(file); }
            var result = LevelImportBridge.Import(source, content, destination); imported = result.Path;
            AssertThat(result.Success).IsTrue(); AssertThat(System.IO.File.Exists(imported)).IsTrue();
            CatalogEntry entry = CatalogBridge.Scan(content, destination).Single(entry => entry.Id == "test.picker_import");
            AssertThat(entry.IsPlayable && entry.Source == LevelSource.User).IsTrue(); AssertThat(entry.Diagnostics.Count(d => d.JsonPath == "/assets")).IsEqual(1);
            AssertThat(entry.Document!.Assets.TryRead("assets/reserved.bin", out var asset)).IsTrue(); AssertThat(asset.ToArray().SequenceEqual(new byte[] { 3, 8 })).IsTrue();
            AssertThat(LevelImportBridge.Import(source, content, destination).Path).IsEqual(imported!);
        }
        finally
        {
            if (imported is not null) { System.IO.File.Delete(imported); }
            System.IO.File.Delete(source); if (System.IO.Directory.Exists(destination)) { System.IO.Directory.Delete(destination); } System.IO.Directory.Delete(root);
        }
    }
    [TestCase, GodotExceptionMonitor]
    public void DirectoryAssetsSurviveGodotDiscoveryAndRespectTheSharedBudget()
    {
        var content = ContentBridge.LoadRegistry(); var level = ContentBridge.LoadBuiltins(content)[0].ToJsonObject(); level["id"] = "test.directory_assets";
        string root = ProjectSettings.GlobalizePath("user://asset-test-" + Guid.NewGuid().ToString("N")), directory = root.PathJoin("level"), assets = directory.PathJoin("assets"), nested = assets.PathJoin("nested");
        System.IO.Directory.CreateDirectory(nested);
        try
        {
            System.IO.File.WriteAllText(directory.PathJoin("level.json"), level.ToJsonString());
            System.IO.File.WriteAllBytes(nested.PathJoin("data.bin"), [1, 2, 9]); System.IO.File.WriteAllBytes(assets.PathJoin(".hidden"), []);
            IAssetSource source = LevelAssetBridge.Read(directory); AssertThat(source.Paths.Count).IsEqual(2);
            AssertThat(source.TryRead("assets/nested/data.bin", out var bytes)).IsTrue(); AssertThat(bytes.ToArray().SequenceEqual(new byte[] { 1, 2, 9 })).IsTrue();
            AssertThat(source.TryRead("assets/.hidden", out var empty) && empty.IsEmpty).IsTrue();
            CatalogEntry entry = CatalogBridge.Scan(content, root).Single(entry => entry.Id == "test.directory_assets");
            AssertThat(entry.IsPlayable).IsTrue(); AssertThat(entry.Document!.Assets.Paths.Count).IsEqual(2); AssertThat(entry.Diagnostics.Count(d => d.JsonPath == "/assets")).IsEqual(1);
            bool rejected = false; try { LevelAssetBridge.Read(directory, 2); } catch (InvalidDataException) { rejected = true; } AssertThat(rejected).IsTrue();
            AssertThat(LevelAssetBridge.Read(root).Paths.Count).IsEqual(0);
        }
        finally
        {
            System.IO.File.Delete(directory.PathJoin("level.json")); System.IO.File.Delete(nested.PathJoin("data.bin")); System.IO.File.Delete(assets.PathJoin(".hidden"));
            System.IO.Directory.Delete(nested); System.IO.Directory.Delete(assets); System.IO.Directory.Delete(directory); System.IO.Directory.Delete(root);
        }
    }
}
