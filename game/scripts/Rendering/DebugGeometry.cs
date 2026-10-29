using System.Text.Json.Nodes;
using Dungeons.Core.Level;
using Dungeons.Core.Sim;
using Godot;

namespace Dungeons.Game.Rendering;

/// <summary>Optional view-only geometry for navigation, collision, authored triggers and paths.</summary>
public partial class DebugGeometry : Node3D
{
    private GameSession _session = null!;
    private LevelDocument _level = null!;
    private readonly List<(float x, float z, float w, float h)> _areas = [];
    private readonly ImmediateMesh _mesh = new();
    private readonly StandardMaterial3D _material = new() { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true, NoDepthTest = true, TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest };
    private bool _nav, _circles, _triggers, _paths;
    private int _frames;
    /// <summary>Copies authored areas once; subsequent geometry reads runtime snapshots only.</summary>
    public void Build(LevelDocument level, GameSession session)
    {
        _level = level; _session = session; Visible = false; AddChild(new MeshInstance3D { Mesh = _mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        if (level.ToJsonObject()["triggers"] is JsonArray triggers)
        {
            foreach (JsonNode? node in triggers)
            {
                if (node?["event"]?["area"] is JsonObject area)
                { _areas.Add((area["x"]!.GetValue<float>(), area["z"]!.GetValue<float>(), area["w"]!.GetValue<float>(), area["h"]!.GetValue<float>())); }
            }
        }
    }
    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (!Visible || @event is not InputEventKey { Pressed: true, Echo: false } key) { return; }
        switch (key.PhysicalKeycode)
        { case Key.F4: _nav = !_nav; break; case Key.F5: _circles = !_circles; break; case Key.F6: _triggers = !_triggers; break; case Key.F7: _paths = !_paths; break; default: return; }
        Draw(); GetViewport().SetInputAsHandled();
    }
    public override void _Process(double delta) { if (Visible && _session is not null && _frames++ % 6 == 0) { Draw(); } }
    private void Draw()
    {
        _mesh.ClearSurfaces(); _mesh.SurfaceBegin(Mesh.PrimitiveType.Lines, _material);
        Rectangle(0, 0, _level.Width, _level.Height, 0.04f, new("#e2b644"));
        if (_nav)
        {
            for (int z = 0; z < _level.Height; z++)
            { for (int x = 0; x < _level.Width; x++) { Rectangle(x, z, 1, 1, _session.World.Grid.HeightAt(new(x + 0.5f, z + 0.5f)) + 0.04f, _session.World.Grid.IsWalkable(x, z) ? new("#3ae374") : new("#ff5a3c")); } }
        }
        if (_circles)
        {
            foreach (Entity entity in _session.World.Entities)
            {
                if (entity.Radius <= 0) { continue; }
                for (int side = 0; side < 16; side++)
                {
                    float a = side * Mathf.Tau / 16, b = (side + 1) * Mathf.Tau / 16;
                    Line(new(entity.Pos.X + MathF.Cos(a) * entity.Radius, entity.Y + 0.05f, entity.Pos.Z + MathF.Sin(a) * entity.Radius),
                        new(entity.Pos.X + MathF.Cos(b) * entity.Radius, entity.Y + 0.05f, entity.Pos.Z + MathF.Sin(b) * entity.Radius), new("#e2b644"));
                }
            }
        }
        if (_triggers) { foreach (var area in _areas) { Rectangle(area.x, area.z, area.w, area.h, 0.08f, new("#c49bcc")); } }
        if (_paths)
        {
            foreach (Entity entity in _session.World.Entities)
            {
                Vector3 previous = new(entity.Pos.X, entity.Y + 0.06f, entity.Pos.Z);
                for (int i = 0; i < entity.Path.Length; i++)
                {
                    var waypoint = entity.Path[i]; var position = waypoint.Center; Vector3 next = new(position.X, _session.World.Grid.HeightAt(position) + 0.06f, position.Z);
                    Line(previous, next, new("#76b8bb")); previous = next;
                }
            }
        }
        _mesh.SurfaceEnd();
    }
    private void Rectangle(float x, float z, float w, float h, float y, Color color)
    { Line(new(x, y, z), new(x + w, y, z), color); Line(new(x + w, y, z), new(x + w, y, z + h), color); Line(new(x + w, y, z + h), new(x, y, z + h), color); Line(new(x, y, z + h), new(x, y, z), color); }
    private void Line(Vector3 a, Vector3 b, Color color) { _mesh.SurfaceSetColor(color); _mesh.SurfaceAddVertex(a); _mesh.SurfaceAddVertex(b); }
}
