using System.Globalization;
using Dungeons.Core.Sim;
using Dungeons.Game.Persistence;
using Dungeons.Game.UI;
using Godot;

namespace Dungeons.Game.Rendering;

/// <summary>Bounded, presentation-only combat labels projected above authoritative actors.</summary>
public partial class CombatOverlay : CanvasLayer
{
    private sealed class Mark(ProgressBar bar, Label alert, HealthBarPattern pattern)
    { internal readonly ProgressBar Bar = bar; internal readonly Label Alert = alert; internal readonly HealthBarPattern Pattern = pattern; internal int Id; internal double HealthUntil, AlertUntil; }
    private sealed class Number(Label label)
    { internal readonly Label Label = label; internal Vector3 Anchor; internal float Jitter; internal double Started = -1; }
    private readonly Mark[] _marks = new Mark[64];
    private readonly Number[] _numbers = new Number[128];
    private readonly RandomNumberGenerator _random = new() { Seed = 0x434f4d424154 };
    private GameSession _session = null!;
    private Camera3D _camera = null!;
    private double _time;
    private int _nextNumber;
    /// <summary>Accessibility preference for combat text.</summary>
    public bool DamageNumbersEnabled { get; set; } = true;
    /// <summary>Scale shared by bars, alert markers and damage text.</summary>
    public float UiScale { get; set; } = 1;
    /// <summary>Visible labels for scene-level checks.</summary>
    public int ActiveNumberCount => _numbers.Count(n => n.Label.Visible);
    /// <summary>Visible enemy health bars.</summary>
    public int VisibleBarCount => _marks.Count(m => m.Bar.Visible);
    /// <summary>Visible alert markers.</summary>
    public int VisibleAlertCount => _marks.Count(m => m.Alert.Visible);
    /// <summary>Allocates the bounded pools outside gameplay reactions.</summary>
    public void Build(GameSession session, Camera3D camera)
    {
        _session = session; _camera = camera; Layer = 1; Name = "CombatOverlay";
        for (int i = 0; i < _marks.Length; i++)
        {
            var bar = new ProgressBar { ShowPercentage = false, MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false, Size = new(48, 6) };
            bar.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = new("#231c24"), BorderColor = new("#100e14"), BorderWidthBottom = 1, BorderWidthTop = 1, BorderWidthLeft = 1, BorderWidthRight = 1 });
            bar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = new("#d34444") });
            var pattern = new HealthBarPattern { Visible = false }; bar.AddChild(pattern);
            AddChild(bar); _marks[i] = new(bar, Text(24, new("#ffe17b")), pattern);
            _marks[i].Alert.Text = "!";
        }
        for (int i = 0; i < _numbers.Length; i++) { _numbers[i] = new(Text(16, Colors.White)); }
    }
    private Label Text(int size, Color color)
    {
        var label = new Label { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore, HorizontalAlignment = HorizontalAlignment.Center, Modulate = color, Size = new(64, 30) };
        label.AddThemeFontSizeOverride("font_size", size); label.AddThemeColorOverride("font_outline_color", new("#17121f")); label.AddThemeConstantOverride("outline_size", 3);
        AddChild(label); return label;
    }
    /// <summary>Reads Core events without consuming a simulation RNG stream.</summary>
    public void ConsumeEvents(ReadOnlySpan<SimEvent> events)
    {
        foreach (SimEvent ev in events)
        {
            Entity? entity = _session.World.FindEntity(ev.EntityId);
            if (entity is null) { continue; }
            if (ev.Kind == SimEventKind.EnemyAlerted && entity.Kind == EntityKind.Enemy) { For(entity.Id).AlertUntil = _time + .3; }
            if (ev.Kind != SimEventKind.DamageApplied) { continue; }
            if (entity.Kind == EntityKind.Enemy) { For(entity.Id).HealthUntil = _time + 3; }
            if (!DamageNumbersEnabled) { continue; }
            Number number = _numbers[_nextNumber++ % _numbers.Length]; number.Started = _time;
            number.Anchor = new(ev.Position.X, entity.Y + 2.1f, ev.Position.Z); number.Jitter = _random.RandfRange(-12, 12);
            number.Label.Text = ev.Amount.ToString(CultureInfo.InvariantCulture); number.Label.Modulate = entity.Kind == EntityKind.Player ? new("#ff5252") : Colors.White;
        }
    }
    private Mark For(int id)
    {
        foreach (Mark mark in _marks) { if (mark.Id == id) { return mark; } }
        foreach (Mark mark in _marks)
        {
            if (mark.Id == 0 || _session.World.FindEntity(mark.Id) is not { Alive: true })
            { mark.Id = id; mark.HealthUntil = mark.AlertUntil = 0; mark.Bar.Visible = mark.Alert.Visible = false; return mark; }
        }
        // At most 64 live enemies are supported by Core; an expired dead mark can be reused.
        throw new InvalidOperationException("Combat overlay exceeded the live monster cap.");
    }
    public override void _Process(double delta)
    { if (_session is not null) { Advance(delta, 1); } }
    /// <summary>Advances only presentation timers and projects interpolated actor heads.</summary>
    public void Advance(double delta, float alpha)
    {
        _time += delta;
        foreach (Mark mark in _marks)
        {
            Entity? entity = _session.World.FindEntity(mark.Id);
            if (entity is not { Alive: true }) { mark.Bar.Visible = mark.Alert.Visible = false; continue; }
            var pos = Dungeons.Core.Math.Vec2.Lerp(entity.PrevPos, entity.Pos, alpha);
            Vector3 head = new(pos.X, Mathf.Lerp(entity.PrevY, entity.Y, alpha) + 2.1f, pos.Z);
            bool visible = !_camera.IsPositionBehind(head); Vector2 screen = _camera.UnprojectPosition(head);
            mark.Bar.Visible = visible && entity.Health.Current < entity.Health.Max && _time < mark.HealthUntil;
            mark.Bar.MaxValue = entity.Health.Max; mark.Bar.Value = entity.Health.Current;
            if (mark.Pattern.Visible) { mark.Pattern.Size = new(mark.Bar.Size.X * entity.Health.Current / System.Math.Max(1, entity.Health.Max), mark.Bar.Size.Y); mark.Pattern.QueueRedraw(); }
            mark.Bar.Scale = Vector2.One * UiScale; mark.Bar.Position = screen - new Vector2(24, 6) * UiScale;
            mark.Alert.Visible = visible && _time < mark.AlertUntil; mark.Alert.Scale = Vector2.One * UiScale;
            mark.Alert.Position = screen - new Vector2(32, 80) * UiScale;
        }
        foreach (Number number in _numbers)
        {
            double age = _time - number.Started;
            number.Label.Visible = DamageNumbersEnabled && number.Started >= 0 && age < .7 && !_camera.IsPositionBehind(number.Anchor);
            if (!number.Label.Visible) { continue; }
            number.Label.Scale = Vector2.One * UiScale;
            number.Label.Position = _camera.UnprojectPosition(number.Anchor) + new Vector2(number.Jitter - 32, -30 - 40 * (float)(age / .7)) * UiScale;
            Color color = number.Label.Modulate; color.A = age <= .4 ? 1 : (float)((.7 - age) / .3); number.Label.Modulate = color;
        }
    }
    public void ApplyAccessibility(AccessibilitySettings settings)
    {
        DamageNumbersEnabled = settings.DamageNumbers;
        foreach (Mark mark in _marks)
        {
            mark.Pattern.Visible = settings.HighContrastHealth;
            mark.Bar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = HealthBarPattern.Tint(settings.HealthPalette, enemy: true) });
            mark.Alert.AddThemeFontSizeOverride("font_size", (int)MathF.Round(24 * settings.TextScale));
        }
        foreach (Number number in _numbers)
        { number.Label.AddThemeFontSizeOverride("font_size", (int)MathF.Round(16 * settings.TextScale)); if (!settings.DamageNumbers) { number.Label.Visible = false; number.Started = -1; } }
    }
}
