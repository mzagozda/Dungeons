using Dungeons.Core.Sim;
using Dungeons.Game.Input;
using Dungeons.Game.Persistence;
using Godot;
using CoreVec2 = Dungeons.Core.Math.Vec2;

namespace Dungeons.Game.UI;

/// <summary>Draggable preview uses the same hit geometry and transactional spacing rules as play.</summary>
public partial class TouchLayoutEditor : Control
{
    private readonly TouchLayout _layout = new();
    private ControlSettings _settings = new();
    private InputButtons _drag;
    private static readonly Vector2 PreviewSize = new(880, 400);
    public event Action<Dictionary<string, TouchPosition>>? Edited;
    public TouchLayout Layout => _layout;
    public TouchLayoutEditor() { CustomMinimumSize = new(0, 220); MouseFilter = MouseFilterEnum.Stop; ClipContents = true; }
    public void Configure(ControlSettings settings)
    { _settings = settings; _layout.Configure(new(880, 400), new(20, 12), new(20, 12), settings.TouchScale, settings.LeftHanded); _layout.TrySetPositions(Placements(settings.TouchPositions)); QueueRedraw(); }
    private static TouchPlacement[] Placements(Dictionary<string, TouchPosition> positions) => positions.Select(pair => new TouchPlacement(Enum.Parse<InputButtons>(pair.Key), new(pair.Value.X, pair.Value.Y))).ToArray();
    public bool TryMove(InputButtons action, TouchPosition canonicalPosition)
    {
        var positions = new Dictionary<string, TouchPosition>(_settings.TouchPositions, StringComparer.Ordinal) { [action.ToString()] = canonicalPosition };
        if (!_layout.TrySetPositions(Placements(positions))) { return false; }
        _settings = _settings with { TouchPositions = positions }; Edited?.Invoke(positions); QueueRedraw(); return true;
    }
    private (float scale, Vector2 origin) Transform()
    { float scale = MathF.Min(Size.X / PreviewSize.X, Size.Y / PreviewSize.Y); return (scale, (Size - PreviewSize * scale) / 2); }
    public override void _GuiInput(InputEvent input)
    {
        var transform = Transform(); if (transform.scale <= 0) { return; }
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } button)
        {
            _drag = 0;
            if (button.Pressed)
            {
                float closest = float.MaxValue;
                foreach (TouchTarget target in _layout.Targets)
                {
                    Vector2 centre = transform.origin + new Vector2(target.Centre.X, target.Centre.Z) * transform.scale; float distance = button.Position.DistanceTo(centre);
                    if (distance <= MathF.Max(22, target.Radius * transform.scale) && distance < closest) { closest = distance; _drag = target.Action; }
                }
            }
            AcceptEvent();
        }
        else if (input is InputEventMouseMotion motion && _drag != 0)
        {
            Vector2 point = (motion.Position - transform.origin) / transform.scale;
            CoreVec2 normalized = new(Mathf.Clamp((point.X - _layout.Minimum.X) / (_layout.Maximum.X - _layout.Minimum.X), 0, 1), Mathf.Clamp((point.Y - _layout.Minimum.Z) / (_layout.Maximum.Z - _layout.Minimum.Z), 0, 1));
            TryMove(_drag, new(_settings.LeftHanded ? 1 - normalized.X : normalized.X, normalized.Z)); AcceptEvent();
        }
    }
    public override void _Draw()
    {
        var transform = Transform(); if (transform.scale <= 0) { return; }
        DrawSetTransform(transform.origin, 0, Vector2.One * transform.scale);
        DrawRect(new(Vector2.Zero, PreviewSize), new Color("#152433"));
        DrawRect(new(new Vector2(20, 12), new Vector2(840, 376)), new Color(1, 1, 1, .35f), false, 2);
        Vector2 joystick = new(_settings.LeftHanded ? 740 : 140, 265); DrawCircle(joystick, 60 * _settings.TouchScale, new Color(1, 1, 1, .18f));
        foreach (TouchTarget target in _layout.Targets)
        {
            Vector2 centre = new(target.Centre.X, target.Centre.Z); DrawCircle(centre, target.Radius, new Color(1, 1, 1, .3f)); DrawArc(centre, target.Radius, 0, Mathf.Tau, 40, Colors.White, 2, true);
            DrawString(GetThemeDefaultFont(), centre - new Vector2(target.Radius, -5), Tr("ACTION_" + target.Action.ToString().ToUpperInvariant()), HorizontalAlignment.Center, target.Radius * 2, 16);
        }
    }
}
