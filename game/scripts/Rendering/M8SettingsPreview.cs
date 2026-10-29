using System.Globalization;
using System.Text.Json.Nodes;
using Dungeons.Game.Persistence;
using Dungeons.Game.UI;
using Godot;

namespace Dungeons.Game.Rendering;

/// <summary>Actual settings widgets and accessible HUD at desktop or simulated phone density.</summary>
public partial class M8SettingsPreview : Node
{
    private string _path = "", _mode = "";
    private GameplayScreen _game = null!;
    private SettingsPanel? _panel;
    private int _frames;
    private ulong _hash;
    private string _focus = "";
    private bool _phone;
    public override void _Ready()
    {
        try
        {
            string[] args = ScreenshotRun.Arguments; float ui = 1, text = 1;
            foreach (string arg in args)
            {
                if (arg.StartsWith("--settings-screenshot=", StringComparison.Ordinal)) { _path = ProjectSettings.GlobalizePath(arg[22..]); }
                if (arg.StartsWith("--settings-mode=", StringComparison.Ordinal)) { _mode = arg[16..]; }
                if (arg.StartsWith("--settings-focus=", StringComparison.Ordinal)) { _focus = arg[17..]; }
                if (arg.StartsWith("--settings-ui=", StringComparison.Ordinal)) { ui = float.Parse(arg[14..], CultureInfo.InvariantCulture); }
                if (arg.StartsWith("--settings-text=", StringComparison.Ordinal)) { text = float.Parse(arg[16..], CultureInfo.InvariantCulture); }
            }
            if (_path.Length == 0 || _mode is not ("video" or "audio" or "controls" or "accessibility" or "hud")) { throw new ArgumentException("Settings preview mode/output required."); }
            bool phone = args.Contains("--settings-phone"); _phone = phone;
            var runtime = new SettingsRuntime(); AddChild(runtime); runtime.Initialize(System.IO.Path.ChangeExtension(_path, ".store"), GameSettings.Defaults(QualityPreset.High, false), applyWindow: false);
            GameSettings settings = runtime.Current with { Video = runtime.Current.Video with { UiScale = ui }, Accessibility = new() { HighContrastHealth = true, HealthPalette = HealthPalette.Protanopia, ReduceMotion = true, TextScale = text } };
            runtime.Commit(settings);
            Window window = GetWindow(); window.ContentScaleFactor = ui * (phone ? window.GetVisibleRect().Size.Y / 400 : 1); UiTheme.ApplyTextScale(window, text);
            var content = ContentBridge.LoadRegistry(); _game = new CaptureGameplayScreen(); AddChild(_game); _game.Build(ContentBridge.LoadBuiltins(content)[0], content, 42, touchPreview: phone); _game.SetProcess(false); _hash = _game.Session.Hash();
            _game._Notification((int)NotificationApplicationFocusOut);
            _game._UnhandledInput(new InputEventMouseMotion { Position = new(100, 100), Relative = new(20, 10) });
            _game.Touch?.ConfigurePreview(new(880, 400), new(28, 4, 12, 16));
            if (_mode != "hud")
            {
                _panel = new(); AddChild(_panel); _panel.Build(runtime); if (phone) { _panel.SetSafeInsets(new(28 / ui, 4 / ui), new(12 / ui, 16 / ui)); }
                ((TabContainer)_panel.FindChild("SettingsTabs", true, false)).CurrentTab = Array.IndexOf(new[] { "video", "audio", "controls", "accessibility" }, _mode);
            }
        }
        catch (Exception error) { Fail(error); }
    }
    public override void _Process(double delta)
    {
        try
        {
            if (!_game.View.ReadyForPlay) { return; }
            _game._Process(0);
            if (_frames == 5 && _panel is not null && _focus.Length > 0)
            {
                Control target = (Control)_panel.FindChild(_focus == "layout" ? "TouchLayoutEditor" : "Binding_attack", true, false);
                Node? parent = target.GetParent(); while (parent is not null && parent is not ScrollContainer) { parent = parent.GetParent(); }
                ((ScrollContainer?)parent)?.EnsureControlVisible(target);
            }
            if (_frames++ < 20) { return; }
            if (_game.Session.Hash() != _hash) { throw new InvalidOperationException("Settings changed Core state."); }
            if (_phone && !_game.Hud.UsesTouch) { throw new InvalidOperationException("The static phone fixture switched away from touch input."); }
            if (_panel is not null && ((TabContainer)_panel.FindChild("SettingsTabs", true, false)).Size.Y < 100) { throw new InvalidOperationException("Settings chrome leaves too little room for its scrollable fields."); }
            if (_mode == "hud")
            {
                Control root = _game.Hud.GetNode<Control>("HudRoot"); ProgressBar health = root.GetChildren().OfType<ProgressBar>().Single();
                foreach (Label label in root.FindChildren("*", "Label", true, false).OfType<Label>().Where(label => label.IsVisibleInTree() && label.Text.Length > 0 && !health.IsAncestorOf(label)))
                {
                    Rect2 bounds = label.GetGlobalRect(), area = root.GetGlobalRect();
                    Node? parent = label.GetParent();
                    while (parent is Control control && parent != root) { if (control.ClipContents) { bounds = bounds.Intersection(control.GetGlobalRect()); } parent = parent.GetParent(); }
                    if (!bounds.HasArea()) { continue; }
                    if (bounds.Position.X < area.Position.X - 1 || bounds.End.X > area.End.X + 1 || bounds.End.Y > health.GetGlobalRect().Position.Y + 1)
                    { throw new InvalidOperationException($"HUD text exceeds its safe region or overlaps the health bar: {label.Text}; rect={bounds}; safe={area}; health={health.GetGlobalRect()}"); }
                }
            }
            using Image image = GetViewport().GetTexture().GetImage(); if (ScreenshotRun.Save(image, _path) != Error.Ok) { throw new IOException("Settings screenshot could not be saved."); }
            ScreenshotRun.Complete(true, image, _game.Session);
            ScreenshotRun.WriteText(System.IO.Path.ChangeExtension(_path, ".json"), new JsonObject { ["mode"] = _mode, ["width"] = image.GetWidth(), ["height"] = image.GetHeight(), ["canvasWidth"] = GetViewport().GetVisibleRect().Size.X, ["canvasHeight"] = GetViewport().GetVisibleRect().Size.Y, ["canvasScale"] = GetWindow().ContentScaleFactor, ["coreUnchanged"] = true, ["hash"] = _hash.ToString("x16") }.ToJsonString());
            GD.Print("M8_SETTINGS_PREVIEW=PASS mode=" + _mode); GetTree().Quit(); SetProcess(false);
        }
        catch (Exception error) { Fail(error); }
    }
    private void Fail(Exception error) { GD.PushError(error.ToString()); SetProcess(false); GetTree().Quit(1); }
}
