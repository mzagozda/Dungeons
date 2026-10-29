using Dungeons.Game.Input;
using Dungeons.Game.Persistence;
using Dungeons.Game.Rendering;
using Godot;

namespace Dungeons.Game.UI;

/// <summary>Reviewable settings draft; Apply commits atomically, Back discards unapplied edits.</summary>
public partial class SettingsPanel : CanvasLayer
{
    private SettingsRuntime _runtime = null!;
    private GameSettings _draft = new();
    private Control _root = null!;
    private Label _status = null!;
    private VBoxContainer _video = null!;
    private TouchLayoutEditor _touch = null!;
    private readonly Dictionary<string, Button> _bindingButtons = new(StringComparer.Ordinal);
    private readonly List<BoxContainer> _rows = [];
    private readonly Dictionary<string, InputBinding[]> _defaults = SettingsBindings.Defaults();
    private string? _capture;
    private bool _closing;
    private MarginContainer _margin = null!;
    private (Vector2 start, Vector2 end)? _previewInsets;
    private Label _title = null!;
    private VBoxContainer _mainBox = null!;
    private ScrollContainer _touchScroll = null!;
    public GameSettings Draft => _draft;
    public event Action? Closed;
    public void Build(SettingsRuntime runtime)
    { _runtime = runtime; _draft = runtime.Current; Layer = 20; CreateUi(); }
    private void CreateUi()
    {
        _root?.QueueFree(); _bindingButtons.Clear(); _rows.Clear();
        _root = new Control { Name = "SettingsRoot", MouseFilter = Control.MouseFilterEnum.Stop }; AddChild(_root); _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var shade = new ColorRect { Color = new("#15151c"), MouseFilter = Control.MouseFilterEnum.Stop }; _root.AddChild(shade); shade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var margin = new MarginContainer(); _margin = margin; _root.AddChild(margin); margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        foreach (string edge in new[] { "left", "right", "top", "bottom" }) { margin.AddThemeConstantOverride("margin_" + edge, 24); }
        var box = new VBoxContainer(); _mainBox = box; margin.AddChild(box); box.AddThemeConstantOverride("separation", 12);
        var title = new Label { Text = Tr("SETTINGS") }; _title = title; title.AddThemeFontSizeOverride("font_size", 30); box.AddChild(title);
        var tabs = new TabContainer { Name = "SettingsTabs", SizeFlagsVertical = Control.SizeFlags.ExpandFill }; box.AddChild(tabs);
        _video = Tab(tabs, "VIDEO"); VideoUi(); AudioUi(Tab(tabs, "AUDIO")); ControlsUi(Tab(tabs, "CONTROLS")); AccessibilityUi(Tab(tabs, "ACCESSIBILITY"));
        _status = new Label { Text = "", Visible = false, AutowrapMode = TextServer.AutowrapMode.WordSmart }; box.AddChild(_status);
        var actions = new HBoxContainer(); box.AddChild(actions);
        AddButton(actions, "SETTINGS_DEFAULTS", () => { _draft = _runtime.Defaults; _capture = null; CreateUi(); });
        Button apply = AddButton(actions, "APPLY", Apply); apply.TooltipText = Tr("SETTINGS_APPLY_HINT"); apply.GrabFocus(); AddButton(actions, "BACK", Close);
    }
    private VBoxContainer Tab(TabContainer tabs, string key)
    {
        var scroll = new ScrollContainer { Name = key, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; tabs.AddChild(scroll); tabs.SetTabTitle(tabs.GetTabCount() - 1, Tr(key));
        var margin = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; scroll.AddChild(margin);
        foreach (string edge in new[] { "left", "right", "top", "bottom" }) { margin.AddThemeConstantOverride("margin_" + edge, 16); }
        var box = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; margin.AddChild(box); box.AddThemeConstantOverride("separation", 12); return box;
    }
    private HBoxContainer Row(VBoxContainer box, string key)
    {
        var outer = new BoxContainer { Vertical = GetViewport().GetVisibleRect().Size.X < 650, CustomMinimumSize = new(0, 42) }; box.AddChild(outer); _rows.Add(outer);
        outer.AddChild(new Label { Text = Tr(key), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new(150, 0) });
        var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; outer.AddChild(row); return row;
    }
    private void Choice(VBoxContainer box, string key, string[] values, int selected, Action<int> changed)
    {
        var option = new OptionButton { Name = key, CustomMinimumSize = new(180, 42) }; Row(box, key).AddChild(option);
        foreach (string value in values) { option.AddItem(Tr(value)); } option.Selected = selected; option.ItemSelected += index => changed((int)index);
    }
    private void Check(VBoxContainer box, string key, bool enabled, Action<bool> changed)
    { var check = new CheckButton { Name = key, ButtonPressed = enabled, CustomMinimumSize = new(80, 42) }; Row(box, key).AddChild(check); check.Toggled += value => changed(value); }
    private void Slider(VBoxContainer box, string key, double minimum, double maximum, double step, double value, Action<double> changed, bool percent = true)
    {
        HBoxContainer row = Row(box, key); var slider = new HSlider { Name = key, MinValue = minimum, MaxValue = maximum, Step = step, Value = value, CustomMinimumSize = new(160, 42), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; row.AddChild(slider);
        var label = new Label { Text = Number(value), CustomMinimumSize = new(60, 0), HorizontalAlignment = HorizontalAlignment.Right }; row.AddChild(label);
        slider.ValueChanged += next => { label.Text = Number(next); changed(next); };
        string Number(double number) => percent ? $"{number * 100:0}%" : $"{number:0}";
    }
    private Button AddButton(Node parent, string key, Action pressed)
    { var button = new Button { Name = key, Text = Tr(key), CustomMinimumSize = new(110, 44) }; parent.AddChild(button); button.Pressed += pressed; return button; }
    private void VideoUi()
    {
        foreach (Node child in _video.GetChildren()) { _video.RemoveChild(child); child.QueueFree(); }
        VideoSettings video = _draft.Video;
        Choice(_video, "QUALITY", ["QUALITY_LOW", "QUALITY_MEDIUM", "QUALITY_HIGH", "QUALITY_CUSTOM"], (int)video.Preset, index =>
        { _draft = _draft with { Video = (QualityPreset)index == QualityPreset.Custom ? _draft.Video with { Preset = QualityPreset.Custom } : _draft.Video.WithPreset((QualityPreset)index, OS.HasFeature("mobile")) }; VideoUi(); });
        if (!OS.HasFeature("mobile")) { Check(_video, "FULLSCREEN", video.Fullscreen, value => EditVideo(_draft.Video with { Fullscreen = value }, quality: false)); }
        Check(_video, "VSYNC", video.Vsync, value => EditVideo(_draft.Video with { Vsync = value }, quality: false));
        Slider(_video, "RESOLUTION_SCALE", .6, 1, .05, video.ResolutionScale, value => EditVideo(_draft.Video with { ResolutionScale = (float)value }));
        Choice(_video, "SHADOWS", ["OFF", "QUALITY_LOW", "QUALITY_HIGH"], (int)video.Shadows, value => EditVideo(_draft.Video with { Shadows = (ShadowQuality)value }));
        Check(_video, "GLOW", video.Glow, value => EditVideo(_draft.Video with { Glow = value }));
        if (!OS.HasFeature("mobile")) { Check(_video, "MSAA", video.Msaa, value => EditVideo(_draft.Video with { Msaa = value })); }
        else { Check(_video, "ADAPTIVE_RESOLUTION", video.Adaptive, value => EditVideo(_draft.Video with { Adaptive = value })); }
        Choice(_video, "LIGHT_LIMIT", ["FOUR_LIGHTS", "EIGHT_LIGHTS"], video.OmniLights == 4 ? 0 : 1, value => EditVideo(_draft.Video with { OmniLights = value == 0 ? 4 : 8 }));
        Slider(_video, "UI_SCALE", .8, 1.5, .05, video.UiScale, value => EditVideo(_draft.Video with { UiScale = (float)value }, quality: false));
        if (GraphicsRuntime.Instance?.CompatibilityRenderer == true) { _video.AddChild(new Label { Text = Tr("QUALITY_DEVICE_LIMIT"), AutowrapMode = TextServer.AutowrapMode.WordSmart }); }
    }
    private void EditVideo(VideoSettings video, bool quality = true)
    {
        _draft = _draft with { Video = quality ? video with { Preset = QualityPreset.Custom } : video };
        if (quality && _video.FindChild("QUALITY", true, false) is OptionButton option) { option.Selected = (int)QualityPreset.Custom; }
    }
    private void AudioUi(VBoxContainer box)
    {
        Slider(box, "MASTER_VOLUME", 0, 100, 1, _draft.Audio.Master, value => _draft = _draft with { Audio = _draft.Audio with { Master = (int)value } }, false);
        Slider(box, "MUSIC_VOLUME", 0, 100, 1, _draft.Audio.Music, value => _draft = _draft with { Audio = _draft.Audio with { Music = (int)value } }, false);
        Slider(box, "SFX_VOLUME", 0, 100, 1, _draft.Audio.Sfx, value => _draft = _draft with { Audio = _draft.Audio with { Sfx = (int)value } }, false);
        Slider(box, "UI_VOLUME", 0, 100, 1, _draft.Audio.Ui, value => _draft = _draft with { Audio = _draft.Audio with { Ui = (int)value } }, false);
        Slider(box, "AMBIENCE_VOLUME", 0, 100, 1, _draft.Audio.Ambience, value => _draft = _draft with { Audio = _draft.Audio with { Ambience = (int)value } }, false);
    }
    private void ControlsUi(VBoxContainer box)
    {
        Choice(box, "AUTO_AIM", ["OFF", "AIM_NORMAL", "AIM_STRONG"], (int)_draft.Controls.AutoAim, value => _draft = _draft with { Controls = _draft.Controls with { AutoAim = (AimAssist)value } });
        ((OptionButton)box.FindChild("AUTO_AIM", true, false)).TooltipText = Tr("TOUCH_AIM_REQUIRED");
        Check(box, "VIBRATION", _draft.Controls.Vibration, value => _draft = _draft with { Controls = _draft.Controls with { Vibration = value } });
        Check(box, "LEFT_HANDED", _draft.Controls.LeftHanded, value => { _draft = _draft with { Controls = _draft.Controls with { LeftHanded = value } }; _touch.Configure(_draft.Controls); });
        Slider(box, "TOUCH_SCALE", .8, 1.5, .05, _draft.Controls.TouchScale, value => { _draft = _draft with { Controls = _draft.Controls with { TouchScale = (float)value } }; _touch.Configure(_draft.Controls); });
        box.AddChild(new Label { Text = Tr("TOUCH_LAYOUT_HINT"), AutowrapMode = TextServer.AutowrapMode.WordSmart });
        _touch = new() { Name = "TouchLayoutEditor" }; _touchScroll = (ScrollContainer)box.GetParent().GetParent(); box.AddChild(_touch); _touch.Configure(_draft.Controls);
        _touch.Edited += positions => _draft = _draft with { Controls = _draft.Controls with { TouchPositions = positions } };
        AddButton(box, "RESET_TOUCH_LAYOUT", () => { _draft = _draft with { Controls = _draft.Controls with { TouchPositions = new() } }; _touch.Configure(_draft.Controls); });
        foreach (string action in SettingsValidation.Actions)
        {
            HBoxContainer row = Row(box, "ACTION_" + action.ToUpperInvariant());
            var binding = new Button { Name = "Binding_" + action, Text = BindingText(action), TooltipText = BindingText(action), ClipText = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new(220, 42) }; row.AddChild(binding); _bindingButtons.Add(action, binding);
            binding.Pressed += () => { _capture = action; _status.Text = Tr("PRESS_BINDING"); };
            if (action != "pause") { AddButton(row, "CLEAR_BINDING", () => { var values = DraftBindings(); values[action] = []; _draft = _draft with { Controls = _draft.Controls with { Bindings = values } }; binding.Text = BindingText(action); }); }
        }
    }
    private Dictionary<string, InputBinding[]> DraftBindings()
    { var result = new Dictionary<string, InputBinding[]>(_defaults, StringComparer.Ordinal); foreach (var pair in _draft.Controls.Bindings) { result[pair.Key] = pair.Value; } return result; }
    private string BindingText(string action)
    {
        InputBinding[] bindings = _draft.Controls.Bindings.TryGetValue(action, out InputBinding[]? values) ? values : _defaults[action];
        return bindings.Length == 0 ? Tr("UNBOUND") : string.Join(" / ", bindings.Select(BindingName));
    }
    private string BindingName(InputBinding binding)
    {
        if (binding.Kind == BindingKind.Key)
        {
            var parts = new List<string>();
            if (binding.Control && binding.Code != (int)Key.Ctrl) { parts.Add("Ctrl"); } if (binding.Alt && binding.Code != (int)Key.Alt) { parts.Add("Alt"); }
            if (binding.Shift && binding.Code != (int)Key.Shift) { parts.Add("Shift"); } if (binding.Meta && binding.Code != (int)Key.Meta) { parts.Add("Meta"); }
            parts.Add(OS.GetKeycodeString((Key)binding.Code)); return string.Join('+', parts);
        }
        if (binding.Kind == BindingKind.MouseButton) { return binding.Code switch { 1 => Tr("MOUSE_LEFT"), 2 => Tr("MOUSE_RIGHT"), 3 => Tr("MOUSE_MIDDLE"), _ => string.Format(Tr("MOUSE_NUMBER"), binding.Code) }; }
        if (binding.Kind == BindingKind.JoyAxis)
        {
            return (JoyAxis)binding.Code switch { JoyAxis.LeftX => binding.Direction < 0 ? "LS ←" : "LS →", JoyAxis.LeftY => binding.Direction < 0 ? "LS ↑" : "LS ↓",
                JoyAxis.RightX => binding.Direction < 0 ? "RS ←" : "RS →", JoyAxis.RightY => binding.Direction < 0 ? "RS ↑" : "RS ↓", JoyAxis.TriggerLeft => "LT", JoyAxis.TriggerRight => "RT", _ => "Pad " + binding.Code };
        }
        string button = (JoyButton)binding.Code switch { JoyButton.LeftShoulder => "LB", JoyButton.RightShoulder => "RB", JoyButton.DpadUp => "↑", JoyButton.DpadDown => "↓", JoyButton.DpadLeft => "←", JoyButton.DpadRight => "→", _ => ((JoyButton)binding.Code).ToString() };
        return Tr("PAD_CONTROL") + " " + button;
    }
    private void AccessibilityUi(VBoxContainer box)
    {
        Check(box, "DAMAGE_NUMBERS", _draft.Accessibility.DamageNumbers, value => _draft = _draft with { Accessibility = _draft.Accessibility with { DamageNumbers = value } });
        Check(box, "HIGH_CONTRAST_HEALTH", _draft.Accessibility.HighContrastHealth, value => _draft = _draft with { Accessibility = _draft.Accessibility with { HighContrastHealth = value } });
        Choice(box, "HEALTH_PALETTE", ["PALETTE_DEFAULT", "PALETTE_PROTANOPIA", "PALETTE_DEUTERANOPIA", "PALETTE_TRITANOPIA"], (int)_draft.Accessibility.HealthPalette, value => _draft = _draft with { Accessibility = _draft.Accessibility with { HealthPalette = (HealthPalette)value } });
        Slider(box, "TEXT_SCALE", .8, 1.5, .05, _draft.Accessibility.TextScale, value => _draft = _draft with { Accessibility = _draft.Accessibility with { TextScale = (float)value } });
        Check(box, "REDUCE_MOTION", _draft.Accessibility.ReduceMotion, value => _draft = _draft with { Accessibility = _draft.Accessibility with { ReduceMotion = value } });
    }
    public override void _Input(InputEvent input)
    {
        if (_capture is null) { if (input is InputEventKey { PhysicalKeycode: Key.Escape, Pressed: true, Echo: false }) { Dungeons.Game.Audio.AudioManager.Instance?.Play("core:sfx_ui_back"); Close(); GetViewport().SetInputAsHandled(); } return; }
        if (input is InputEventKey { PhysicalKeycode: Key.Escape, Pressed: true }) { _capture = null; _status.Text = Tr("BINDING_CANCELLED"); GetViewport().SetInputAsHandled(); return; }
        InputBinding? captured = SettingsBindings.Capture(input); if (captured is not InputBinding binding) { return; }
        var values = DraftBindings(); bool joy = binding.Kind is BindingKind.JoyAxis or BindingKind.JoyButton;
        values[_capture] = [.. values[_capture].Where(existing => (existing.Kind is BindingKind.JoyAxis or BindingKind.JoyButton) != joy), binding];
        _draft = _draft with { Controls = _draft.Controls with { Bindings = values } }; _bindingButtons[_capture].Text = BindingText(_capture);
        string[] conflicts = values.Where(pair => pair.Key != _capture && pair.Value.Contains(binding)).Select(pair => Tr("ACTION_" + pair.Key.ToUpperInvariant()).ToString()).ToArray();
        _status.Text = conflicts.Length == 0 ? Tr("BINDING_READY") : string.Format(Tr("BINDING_CONFLICT"), string.Join(", ", conflicts)); _capture = null; GetViewport().SetInputAsHandled();
    }
    public void Apply()
    { _capture = null; if (_runtime.Commit(_draft)) { _draft = _runtime.Current; _status.Text = Tr("SETTINGS_SAVED"); } else { _status.Text = Tr("SETTINGS_SAVE_FAILED"); } }
    public void Close() { if (_closing) { return; } _closing = true; _capture = null; Closed?.Invoke(); }
    public void SetSafeInsets(Vector2 start, Vector2 end) { _previewInsets = (start, end); }
    public override void _Process(double delta)
    {
        Vector2 canvasSize = GetViewport().GetVisibleRect().Size; bool narrow = canvasSize.X < 650, compact = canvasSize.Y < 420;
        foreach (BoxContainer row in _rows) { if (GodotObject.IsInstanceValid(row) && row.Vertical != narrow) { row.Vertical = narrow; } }
        if (_title is not null) { _title.Visible = !compact; _mainBox.AddThemeConstantOverride("separation", compact ? 8 : 12); _status.Visible = _status.Text.Length > 0; }
        if (_touch is not null && _touchScroll.Size.Y > 0) { _touch.CustomMinimumSize = new(0, MathF.Min(220, MathF.Max(100, _touchScroll.Size.Y - 8))); }
        if (_margin is null) { return; } Vector2 start = Vector2.Zero, end = Vector2.Zero;
        if (_previewInsets is { } preview) { start = preview.start; end = preview.end; }
        else if (OS.HasFeature("mobile"))
        {
            Rect2I safe = DisplayServer.GetDisplaySafeArea(); Vector2 pixels = GetWindow().Size;
            if (safe.Size.X > 0 && safe.Size.Y > 0 && pixels.X > 0 && pixels.Y > 0)
            { Vector2 position = (Vector2)safe.Position - (Vector2)GetWindow().Position; Vector2 factor = GetViewport().GetVisibleRect().Size / pixels; start = position.Clamp(Vector2.Zero, pixels) * factor; end = (pixels - position - (Vector2)safe.Size).Clamp(Vector2.Zero, pixels) * factor; }
        }
        int padding = compact ? 12 : 24;
        _margin.AddThemeConstantOverride("margin_left", padding + (int)MathF.Ceiling(start.X)); _margin.AddThemeConstantOverride("margin_top", padding + (int)MathF.Ceiling(start.Y));
        _margin.AddThemeConstantOverride("margin_right", padding + (int)MathF.Ceiling(end.X)); _margin.AddThemeConstantOverride("margin_bottom", padding + (int)MathF.Ceiling(end.Y));
    }
}
