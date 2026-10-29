using System.Text;
using System.Text.Json;
using Dungeons.Core.Content;
using Dungeons.Core.Level;
using Dungeons.Game.Persistence;
using Dungeons.Game.UI;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace Dungeons.Game.Tests;

[TestSuite, RequireGodotRuntime]
public sealed class LevelBrowserTests
{
    [TestCase(0, 1, 0), TestCase(0, 16, 1), TestCase(2, 8, 0), TestCase(2, 16, 1), TestCase(3, 2, 0), TestCase(3, 8, 1), TestCase(4, 8, 0), TestCase(6, 16, 1), GodotExceptionMonitor]
    public void BoundedPreviewsMatchNativePngPixelsAcrossColourAndInterlaceModes(int colour, int depth, int interlace)
    {
        byte[] source = PreviewPngFixtures.Build(colour, depth, interlace, metadata: true);
        AssertThat(PreviewPng.TrySanitize(source, out byte[] clean)).IsTrue();
        using var original = new Image(); using var sanitized = new Image();
        AssertThat(original.LoadPngFromBuffer(source)).IsEqual(Error.Ok); AssertThat(sanitized.LoadPngFromBuffer(clean)).IsEqual(Error.Ok);
        AssertThat(original.GetData().SequenceEqual(sanitized.GetData())).IsTrue();
        AssertThat(sanitized.GetWidth()).IsEqual(320); AssertThat(sanitized.GetHeight()).IsEqual(180);
    }

    [TestCase, GodotExceptionMonitor]
    public async Task RealCatalogueCardsDetailsDiagnosticsAndRefreshKeepInvalidLevelsVisible()
    {
        string folder = Temp("app"); var content = ContentBridge.LoadRegistry(); Prepare(folder, content);
        var profile = new PlayerProfile(); profile.Levels["core.mossy_hollow"] = new() { Completed = true, BestTimeTicks = 11591, Attempts = 3, BestEmeralds = 71 };
        File.WriteAllText(Path.Combine(folder, "profile.json"), JsonSerializer.Serialize(profile, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        var app = new AppRoot(folder); using ISceneRunner runner = ISceneRunner.Load(app, autoFree: true); await AppTestReady.Wait(runner, app);
        Button Find(string key) => app.FindChildren("*", "Button", true, false).OfType<Button>().First(button => button.IsVisibleInTree() && button.Text == app.Tr(key));
        Find("PLAY").EmitSignal(BaseButton.SignalName.Pressed); await runner.SimulateFrames(6);
        LevelBrowserView Browser() => app.FindChildren("LevelBrowser", "ScrollContainer", true, false).OfType<LevelBrowserView>().Single(view => view.IsVisibleInTree());
        LevelBrowserView browser = Browser(); AssertThat(browser.Cards.Count).IsEqual(4);
        LevelCard built = browser.Cards.Single(card => card.Entry.Id == "core.mossy_hollow");
        AssertThat(built.Completed).IsTrue(); AssertThat(built.Entry.PreviewBytes.Length).IsGreater(0); AssertThat(built.Picture.Texture is not null).IsTrue();
        Label caption = built.FindChildren("LevelName", "Label", true, false).OfType<Label>().Single(); AssertThat(caption.Size.Y).IsGreater(0f);
        AssertRenderedInk(caption);
        AssertThat(browser.Cards.Single(card => card.Entry.Id == "test.directory").Picture.Texture is not null).IsTrue();
        Save(app, "cards");
        LevelCard invalid = browser.Cards.Single(card => card.Entry.Id == "test.broken"); AssertThat(invalid.SelectButton.Disabled).IsFalse();
        AssertThat(invalid.SelectButton.TooltipText.Contains("LVL", StringComparison.Ordinal)).IsTrue();
        invalid.SelectButton.EmitSignal(BaseButton.SignalName.Pressed); await runner.SimulateFrames(4);
        AssertThat(Find("PLAY").Disabled).IsTrue();
        Find("LEVEL_SHOW_DIAGNOSTICS").EmitSignal(BaseButton.SignalName.Pressed); await runner.SimulateFrames(3);
        AcceptDialog dialog = app.GetNode<AcceptDialog>("LevelDiagnostics"); AssertThat(dialog.Visible).IsTrue();
        AssertThat(dialog.FindChildren("*", "Label", true, false).OfType<Label>().Any(label => label.Text.Contains("LVL", StringComparison.Ordinal))).IsTrue();
        Save(app, "diagnostics"); app._Notification((int)Node.NotificationWMGoBackRequest); AssertThat(dialog.Visible).IsFalse();
        Find("BACK").EmitSignal(BaseButton.SignalName.Pressed); await runner.SimulateFrames(4);
        AssertThat(Browser().Cards.Single(card => card.Entry.Id == "test.broken").SelectButton.HasFocus()).IsTrue();
        File.Delete(Path.Combine(folder, "levels", "broken", "level.json")); Find("REFRESH").EmitSignal(BaseButton.SignalName.Pressed); await runner.SimulateFrames(4);
        AssertThat(Browser().Cards.Count).IsEqual(3);
        Browser().Cards.Single(card => card.Entry.Id == "core.mossy_hollow").SelectButton.EmitSignal(BaseButton.SignalName.Pressed); await runner.SimulateFrames(4);
        Label record = app.FindChildren("Record", "Label", true, false).OfType<Label>().Single(label => label.IsVisibleInTree()); AssertThat(record.Text.Contains("193", StringComparison.Ordinal)).IsTrue();
        AssertThat(Find("PLAY").Disabled).IsFalse(); Save(app, "details");
        AssertThat(new RunStore(folder).Profile.Levels["core.mossy_hollow"].Attempts).IsEqual(3);
    }

    [TestCase, GodotExceptionMonitor]
    public async Task GridResizesFollowsFocusAndLongPressDoesNotActivateBrokenCards()
    {
        var content = ContentBridge.LoadRegistry(); string folder = Temp("grid"); Prepare(folder, content);
        CatalogEntry[] entries = CatalogBridge.Scan(content, Path.Combine(folder, "levels")).OrderBy(entry => entry.IsPlayable).ToArray();
        var root = new Control(); using ISceneRunner runner = ISceneRunner.Load(root, autoFree: true); root.Size = new(1120, 500);
        var browser = new LevelBrowserView(); root.AddChild(browser); browser.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); browser.Build(entries, new Dictionary<string, LevelRecord>());
        int hints = 0, activations = 0; browser.DiagnosticRequested += _ => hints++; browser.Selected += _ => activations++;
        await runner.SimulateFrames(5); AssertThat(browser.Columns).IsGreater(1);
        browser.Cards[0].SelectButton.GrabFocus(); runner.SimulateKeyPressed(Key.Right); await runner.SimulateFrames(2);
        AssertThat(browser.Cards[1].SelectButton.HasFocus()).IsTrue();
        root.Size = new(560, 300); await runner.SimulateFrames(5); AssertThat(browser.Columns).IsEqual(1);
        LevelCard invalid = browser.Cards[0]; Vector2 point = invalid.SelectButton.GetGlobalRect().GetCenter();
        root.GetViewport().PushInput(new InputEventScreenTouch { Index = 9, Position = point, Pressed = true }, true);
        invalid._Process(.6); root.GetViewport().PushInput(new InputEventScreenTouch { Index = 9, Position = point, Pressed = false }, true);
        AssertThat(hints).IsEqual(1); AssertThat(activations).IsEqual(0);
        root.GetViewport().PushInput(new InputEventScreenTouch { Index = 9, Position = point, Pressed = true }, true);
        root.GetViewport().PushInput(new InputEventScreenDrag { Index = 9, Position = point + new Vector2(0, 40), Relative = new(0, 40) }, true);
        invalid._Process(.6); root.GetViewport().PushInput(new InputEventScreenTouch { Index = 9, Position = point + new Vector2(0, 40), Pressed = false }, true);
        AssertThat(hints).IsEqual(1); AssertThat(activations).IsEqual(0);
        LevelCard last = browser.Cards[^1]; last.SelectButton.GrabFocus(); await runner.SimulateFrames(3); AssertThat(browser.ScrollVertical).IsGreater(0);
        AssertThat(last.GetGlobalRect().Intersects(browser.GetGlobalRect())).IsTrue(); Save(root, "compact-grid");
    }

    [TestCase, GodotExceptionMonitor]
    public void MissingAndCorruptOptionalPreviewsStaySelectableAndCacheIsBounded()
    {
        var content = ContentBridge.LoadRegistry(); byte[] png = PreviewPngFixtures.Build(2, 8, 0);
        var candidates = Enumerable.Range(0, LevelPreviewPool.Capacity + 3).Select(index =>
            new CatalogCandidate("preview-" + index, LevelSource.User, Encoding.UTF8.GetBytes($$"""{"id":"test.{{index}}","name":"Preview {{index}}"}"""), previewBytes: index == 0 ? default : index == 1 ? new byte[] { 1, 2, 3 } : png));
        CatalogEntry[] entries = LevelCatalog.Scan(candidates, content).ToArray();
        using var pool = new LevelPreviewPool();
        AssertThat(pool.Get(entries.Single(entry => entry.Id == "test.0")) is null).IsTrue();
        AssertThat(pool.Get(entries.Single(entry => entry.Id == "test.1")) is null).IsTrue();
        foreach (CatalogEntry entry in entries.Where(entry => entry.Id is not ("test.0" or "test.1")))
        { AssertThat(pool.Get(entry) is not null).IsTrue(); AssertThat(pool.Count <= LevelPreviewPool.Capacity).IsTrue(); }
    }

    [TestCase, GodotExceptionMonitor]
    public async Task PhoneCatalogueAndDetailsFitSafeAreaAtMaximumUiAndTextScale()
    {
        string folder = Temp("phone"); Prepare(folder, ContentBridge.LoadRegistry());
        var app = new AppRoot(folder); using ISceneRunner runner = ISceneRunner.Load(app, autoFree: true); await AppTestReady.Wait(runner, app);
        Window window = app.GetWindow(); float factor = window.ContentScaleFactor; int font = window.GetThemeDefaultFontSize();
        Vector2I canvas = window.ContentScaleSize; Window.ContentScaleAspectEnum aspect = window.ContentScaleAspect;
        Button Find(string key) => app.FindChildren("*", "Button", true, false).OfType<Button>().First(button => button.IsVisibleInTree() && button.Text == app.Tr(key));
        try
        {
            // Headless DisplayServer uses a 64x64 dummy window. Pin the same
            // landscape canvas on every backend instead of inheriting its aspect.
            window.ContentScaleAspect = Window.ContentScaleAspectEnum.Ignore; window.ContentScaleSize = new(1760, 800);
            await runner.SimulateFrames(2);
            float height = window.GetVisibleRect().Size.Y * factor;
            window.ContentScaleFactor = height / 400 * 1.5f; UiTheme.ApplyTextScale(window, 1.5f);
            Find("PLAY").EmitSignal(BaseButton.SignalName.Pressed); await runner.SimulateFrames(6);
            MenuLayout Frame() => app.FindChildren("MenuFrame", "MarginContainer", true, false).OfType<MenuLayout>().Single();
            Frame().SetSafeInsets(new(28 / 1.5f, 4 / 1.5f), new(12 / 1.5f, 16 / 1.5f)); await runner.SimulateFrames(5);
            var browser = app.FindChildren("LevelBrowser", "ScrollContainer", true, false).OfType<LevelBrowserView>().Single();
            Rect2 viewport = window.GetVisibleRect();
            GD.Print($"CATALOGUE_PHONE canvas={viewport.Size} window={window.Size} factor={window.ContentScaleFactor} frame={Frame().Size} browser={browser.Size} font={window.GetThemeDefaultFontSize()}");
            AssertThat(browser.Size.Y).IsGreater(40f); AssertThat(browser.ScrollHorizontal).IsEqual(0);
            AssertThat(browser.ScrollVertical).IsEqual(0);
            Label heading = browser.Cards[0].FindChildren("LevelName", "Label", true, false).OfType<Label>().Single();
            AssertThat(heading.GetGlobalRect().Intersects(browser.GetGlobalRect())).IsTrue(); AssertRenderedInk(heading);
            foreach (Button button in new[] { Find("REFRESH"), Find("IMPORT_LEVEL"), Find("BACK") })
            { Rect2 rect = button.GetGlobalRect(); AssertThat(rect.Position.X >= 28 / 1.5f && rect.End.X <= viewport.End.X - 12 / 1.5f + 1 && rect.End.Y <= viewport.End.Y - 16 / 1.5f + 1).IsTrue(); }
            Save(app, "phone-cards"); browser.Cards[0].SelectButton.EmitSignal(BaseButton.SignalName.Pressed); await runner.SimulateFrames(5);
            Frame().SetSafeInsets(new(28 / 1.5f, 4 / 1.5f), new(12 / 1.5f, 16 / 1.5f)); await runner.SimulateFrames(4);
            var details = app.FindChildren("LevelDetails", "ScrollContainer", true, false).OfType<LevelDetailsView>().Single();
            AssertThat(details.Size.Y).IsGreater(40f); AssertThat(Find("PLAY").GetGlobalRect().End.Y <= viewport.End.Y).IsTrue(); Save(app, "phone-details");
        }
        finally { window.ContentScaleSize = canvas; window.ContentScaleAspect = aspect; window.ContentScaleFactor = factor; UiTheme.ApplyTextScale(window, font / 16f); }
    }

    private static void AssertRenderedInk(Label label)
    {
        if (DisplayServer.GetName() == "headless") { return; }
        using Image image = label.GetViewport().GetTexture().GetImage(); Vector2 scale = image.GetSize() / label.GetViewport().GetVisibleRect().Size;
        Rect2 rect = label.GetGlobalRect(); int ink = 0;
        for (int y = (int)(rect.Position.Y * scale.Y); y < Math.Min(image.GetHeight(), rect.End.Y * scale.Y); y++)
        { for (int x = (int)(rect.Position.X * scale.X); x < Math.Min(image.GetWidth(), rect.End.X * scale.X); x++) { Color c = image.GetPixel(x, y); if (c.R > .7f && c.G > .7f && c.B > .7f) { ink++; } } }
        AssertThat(ink).IsGreater(10);
    }

    private static string Temp(string name)
    { string path = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "../artifacts/m8/level-browser-tests", name + "-" + Guid.NewGuid().ToString("N"))); Directory.CreateDirectory(path); return path; }
    private static void Prepare(string folder, ContentRegistry content)
    {
        string levels = Path.Combine(folder, "levels"); Directory.CreateDirectory(levels); var level = ContentBridge.LoadBuiltins(content)[0];
        var source = level.ToJsonObject(); source["id"] = "test.directory"; source["name"] = "Directory trial"; source["description"] = "A directory-authored level.";
        string directory = Path.Combine(levels, "directory"); Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "level.json"), source.ToJsonString());
        byte[] png = PreviewPngFixtures.Build(2, 8, 0); File.WriteAllBytes(Path.Combine(directory, "preview.png"), png);
        source["id"] = "test.package"; source["name"] = "Packed trial";
        var package = LevelPackage.Create([("level.json", (ReadOnlyMemory<byte>)Encoding.UTF8.GetBytes(source.ToJsonString())), ("preview.png", png)]);
        using (var output = File.Create(Path.Combine(levels, "packed.dlvl"))) { package.Package!.Write(output); }
        string broken = Path.Combine(levels, "broken"); Directory.CreateDirectory(broken);
        File.WriteAllText(Path.Combine(broken, "level.json"), """{"id":"test.broken","name":"Broken trial","author":"Test","description":"Broken entries retain descriptions."}""");
    }
    private static void Save(Node root, string name)
    {
        if (DisplayServer.GetName() == "headless") { return; }
        string folder = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "../artifacts/m8/level-browser-captures")); Directory.CreateDirectory(folder);
        using Image image = root.GetViewport().GetTexture().GetImage(); AssertThat(image.SavePng(Path.Combine(folder, name + "-" + RenderingServer.GetCurrentRenderingMethod() + ".png"))).IsEqual(Error.Ok);
    }
}
