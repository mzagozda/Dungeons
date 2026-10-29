using GdUnit4;
using Dungeons.Game.UI;
using Godot;
using static GdUnit4.Assertions;

namespace Dungeons.Game.Tests;

[TestSuite, RequireGodotRuntime]
public sealed class UiThemeTests
{
    [TestCase, GodotExceptionMonitor]
    public async Task SharedThemeReachesCanvasControlsWithPixelFontAndNineSliceStates()
    {
        var root = new CanvasLayer(); using ISceneRunner runner = ISceneRunner.Load(root, autoFree: true);
        var button = new Button { Text = "Play" }; root.AddChild(button);
        var label = new Label { Text = "Mossy Hollow" }; root.AddChild(label);
        var option = new OptionButton(); root.AddChild(option); option.AddItem("High");
        var panel = new PanelContainer(); root.AddChild(panel);
        var check = new CheckButton(); root.AddChild(check);
        var tabs = new TabContainer(); root.AddChild(tabs); tabs.AddChild(new Control { Name = "Video" }); tabs.AddChild(new Control { Name = "Audio" });
        await runner.SimulateFrames(2);
        GD.Print($"UI_THEME window={button.GetWindow().Name} theme={button.GetWindow().Theme?.ResourcePath} font={button.GetWindow().Theme?.DefaultFont?.ResourcePath} controlFont={button.GetThemeDefaultFont().GetFontName()} root={root.GetPath()}");
        foreach (Control control in new Control[] { button, label, option, panel, check })
        { AssertThat(control.GetThemeDefaultFont().ResourcePath).IsEqual("res://assets/fonts/Silkscreen-Regular.ttf"); }
        var font = (FontFile)button.GetThemeFont("font");
        AssertThat(font.Antialiasing).IsEqual(TextServer.FontAntialiasing.None);
        AssertThat(font.SubpixelPositioning).IsEqual(TextServer.SubpixelPositioning.Disabled);
        AssertThat(font.MultichannelSignedDistanceField).IsFalse(); AssertThat(font.GenerateMipmaps).IsFalse();
        foreach (string state in new[] { "normal", "hover", "pressed", "disabled", "focus" })
        {
            var style = (StyleBoxTexture)button.GetThemeStylebox(state);
            AssertThat(style.Texture.GetSize()).IsEqual(new Vector2(16, 16)); AssertThat(style.TextureMarginLeft).IsEqual(4f);
        }
        AssertThat(((StyleBoxTexture)button.GetThemeStylebox("focus")).DrawCenter).IsFalse();
        AssertThat(panel.GetThemeStylebox("panel") is StyleBoxTexture).IsTrue();
        AssertThat(option.GetThemeIcon("arrow").GetSize()).IsEqual(new Vector2(16, 16));
        AssertThat(check.GetThemeIcon("on").GetSize()).IsEqual(new Vector2(16, 16));
        AssertThat(tabs.GetThemeStylebox("tab_selected") is StyleBoxTexture).IsTrue();
        AssertThat(tabs.GetThemeIcon("increment").ResourcePath).IsEqual("res://assets/ui/arrow_right.png");
        UiTheme.ApplyTextScale(button.GetWindow(), 1.5f);
        AssertThat(button.GetThemeDefaultFontSize()).IsEqual(24);
        AssertThat(button.GetThemeFont("font").ResourcePath).IsEqual("res://assets/fonts/Silkscreen-Regular.ttf");
        AssertThat(button.GetThemeStylebox("normal") is StyleBoxTexture).IsTrue();
        UiTheme.ApplyTextScale(button.GetWindow(), 1);
    }

    [TestCase, GodotExceptionMonitor]
    public void ShippedEnglishTextHasGlyphsAndThemeAssetsUseHardAlphaPalettePixels()
    {
        Font font = GD.Load<FontFile>("res://assets/fonts/Silkscreen-Regular.ttf");
        foreach (char symbol in "←↑→↓◇⚠") { AssertThat(font.HasChar(symbol)).IsTrue(); }
        using var strings = Godot.FileAccess.Open("res://i18n/strings.csv", Godot.FileAccess.ModeFlags.Read);
        var missing = new SortedSet<int>();
        while (!strings.EofReached())
        {
            string[] row = strings.GetCsvLine(); if (row.Length < 2) { continue; }
            foreach (var rune in row[1].EnumerateRunes()) { if (!RuneIsWhitespace(rune.Value) && !font.HasChar(rune.Value)) { missing.Add(rune.Value); } }
        }
        AssertThat(string.Join(",", missing.Select(code => $"U+{code:X4}"))).IsEqual("");
        foreach (string name in new[] { "panel", "focus", "button_hover", "button_pressed", "button_disabled", "check_on", "check_off", "arrow_down", "arrow_up", "arrow_left", "arrow_right", "slider_grab" })
        {
            using Image image = GD.Load<Texture2D>("res://assets/ui/" + name + ".png").GetImage();
            AssertThat(image.GetWidth()).IsEqual(16); AssertThat(image.GetHeight()).IsEqual(16);
            for (int y = 0; y < 16; y++) { for (int x = 0; x < 16; x++) { float alpha = image.GetPixel(x, y).A; AssertThat(alpha == 0 || alpha == 1).IsTrue(); } }
        }
    }
    private static bool RuneIsWhitespace(int code) => code is 9 or 10 or 13 or 32;

    [TestCase, GodotExceptionMonitor]
    public async Task ActualMenuAndAttributedCreditsFitAtMaximumTextAndUiScale()
    {
        string folder = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "../artifacts/m8/pixel-ui-menus", Guid.NewGuid().ToString("N")));
        var app = new AppRoot(folder); using ISceneRunner runner = ISceneRunner.Load(app, autoFree: true);
        await AppTestReady.Wait(runner, app); Window window = app.GetWindow(); float scale = window.ContentScaleFactor;
        int size = window.GetThemeDefaultFontSize();
        try
        {
            window.ContentScaleFactor = 1.5f; UiTheme.ApplyTextScale(window, 1.5f); await runner.SimulateFrames(3);
            void Capture(string name)
            {
                if (DisplayServer.GetName() == "headless") { return; }
                Directory.CreateDirectory(folder); using Image image = app.GetViewport().GetTexture().GetImage();
                AssertThat(image.SavePng(Path.Combine(folder, name + "-" + RenderingServer.GetCurrentRenderingMethod() + ".png"))).IsEqual(Error.Ok);
            }
            Capture("menu");
            Button credits = app.FindChildren("*", "Button", true, false).OfType<Button>().Single(button => button.IsVisibleInTree() && button.Text == app.Tr("CREDITS"));
            credits.EmitSignal(BaseButton.SignalName.Pressed); await runner.SimulateFrames(4);
            Label attribution = app.FindChildren("*", "Label", true, false).OfType<Label>().Single(label => label.IsVisibleInTree() && label.Text == app.Tr("CREDITS_ORIGINAL"));
            AssertThat(attribution.Text.Contains("Silkscreen", StringComparison.Ordinal)).IsTrue();
            AssertThat(attribution.GetThemeFont("font").ResourcePath).IsEqual("res://assets/fonts/Silkscreen-Regular.ttf");
            Rect2 bounds = attribution.GetGlobalRect(), viewport = app.GetViewport().GetVisibleRect();
            AssertThat(bounds.Position.X >= 0 && bounds.Position.Y >= 0 && bounds.End.X <= viewport.End.X + 1 && bounds.End.Y <= viewport.End.Y + 1).IsTrue();
            Capture("credits");
        }
        finally { window.ContentScaleFactor = scale; UiTheme.ApplyTextScale(window, size / 16f); }
    }
}
