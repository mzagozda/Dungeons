using Dungeons.Core.Content;
using Godot;

namespace Dungeons.Game.UI;

/// <summary>One bounded canvas pool tweens original pickup glyphs to live HUD targets.</summary>
public partial class PickupFlights : Control
{
    public const int Capacity = 64;
    private struct Flight { internal Vector2 Start, Bend; internal PickupKind Kind; internal double Started; internal bool Active; }
    private readonly Flight[] _flights = new Flight[Capacity];
    private readonly Vector2[] _diamond = new Vector2[4];
    private readonly System.Random _random = new(0x5049434b);
    private Func<PickupKind, Vector2> _target = null!;
    private int _next;
    private double _time;
    private bool _reduceMotion;
    public int ActiveCount { get; private set; }
    public float UiScale { get; set; } = 1;
    public bool ReduceMotion { get => _reduceMotion; set { _reduceMotion = value; if (value) { Clear(); } } }
    public PickupFlights() { MouseFilter = MouseFilterEnum.Ignore; }
    public void Bind(Func<PickupKind, Vector2> target)
    { _target = target; SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); }
    public void Collect(PickupKind kind, Vector2 viewportPosition)
    {
        if (ReduceMotion || !viewportPosition.IsFinite() || _target is null || Size.X < 32 || Size.Y < 32) { return; }
        Vector2 start = Local(viewportPosition);
        if (!new Rect2(Vector2.Zero, Size).HasPoint(start)) { return; }
        Vector2 target = Local(_target(kind));
        if (!target.IsFinite()) { return; }
        ref Flight flight = ref _flights[_next]; _next = (_next + 1) % Capacity; if (!flight.Active) { ActiveCount++; }
        flight = new() { Active = true, Kind = kind, Started = _time, Start = start,
            Bend = new((start.X + target.X) * .5f + (_random.NextSingle() - .5f) * 60 * UiScale, MathF.Min(start.Y, target.Y) - 48 * UiScale) };
        QueueRedraw();
    }
    public void Clear()
    { Array.Clear(_flights); ActiveCount = _next = 0; QueueRedraw(); }
    public override void _Process(double delta) => Advance(delta);
    public void Advance(double delta)
    {
        bool redraw = ActiveCount > 0;
        _time += System.Math.Max(0, delta);
        for (int i = 0; i < Capacity; i++)
        { if (_flights[i].Active && _time - _flights[i].Started >= .65) { _flights[i].Active = false; ActiveCount--; } }
        if (redraw) { QueueRedraw(); }
    }
    public override void _Draw()
    {
        for (int i = 0; i < Capacity; i++)
        {
            Flight flight = _flights[i]; if (!flight.Active) { continue; }
            float t = Mathf.Clamp((float)((_time - flight.Started) / .65), 0, 1); t = 1 - MathF.Pow(1 - t, 3);
            Vector2 target = Local(_target(flight.Kind)); if (!target.IsFinite()) { continue; }
            Vector2 point = (1 - t) * (1 - t) * flight.Start + 2 * (1 - t) * t * flight.Bend + t * t * target;
            float border = 10 * UiScale; point = point.Clamp(new(border, border), Size - new Vector2(border, border));
            float alpha = Mathf.Clamp((float)((.65 - (_time - flight.Started)) / .2), 0, 1);
            DrawSetTransform(point, scale: Vector2.One * UiScale);
            if (flight.Kind == PickupKind.Emerald)
            {
                _diamond[0] = new(0, -8); _diamond[1] = new(6, 0); _diamond[2] = new(0, 8); _diamond[3] = new(-6, 0);
                DrawColoredPolygon(_diamond, new Color(.29f, .81f, .47f, alpha)); DrawLine(new(0, -6), new(-4, 0), new Color(.78f, 1, .7f, alpha), 2);
            }
            else if (flight.Kind == PickupKind.Food)
            {
                DrawRect(new Rect2(-7, -5, 14, 12), new Color(.95f, .25f, .18f, alpha));
                DrawRect(new Rect2(0, -8, 6, 3), new Color(.3f, .75f, .3f, alpha));
            }
            else
            {
                for (int arrow = -1; arrow <= 1; arrow++)
                { float x = arrow * 4; DrawLine(new(x, 7), new(x, -6), new Color(.8f, .74f, .57f, alpha), 2); DrawLine(new(x - 2, -3), new(x, -7), new Color(.9f, .94f, .97f, alpha), 2); }
            }
        }
        DrawSetTransform(Vector2.Zero);
    }
    private Vector2 Local(Vector2 viewportPosition) => GetGlobalTransformWithCanvas().AffineInverse() * viewportPosition;
}
