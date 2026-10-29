using Dungeons.Core.Sim;
using Godot;

namespace Dungeons.Game.Rendering;

public partial class WorldView
{
    private Godot.Environment _environment = null!;
    private DirectionalLight3D _sun = null!;
    private bool _authoredShadows, _authoredGlow;
    private int _lightLimit = 8;
    public GraphicsOptions Graphics { get; private set; }
    public bool CanMeasureGraphics => ReadyForPlay && IsProcessing() && CanProcess() && _session is { State: SessionState.Running };
    public void ApplyGraphics(GraphicsOptions options, float scale, bool compatibility)
    {
        Graphics = options; _lightLimit = options.OmniLights;
        _sun.ShadowEnabled = _authoredShadows && options.Shadows;
        _environment.GlowEnabled = _authoredGlow && options.Glow && !compatibility;
        GetViewport().Msaa3D = options.Msaa ? Viewport.Msaa.Msaa2X : Viewport.Msaa.Disabled;
        GetViewport().Scaling3DMode = Viewport.Scaling3DModeEnum.Bilinear;
        ApplyResolution(scale); CullLights();
    }
    public void ApplyResolution(float scale) => GetViewport().Scaling3DScale = scale;
    public override void _ExitTree() { GraphicsRuntime.Instance?.Detach(this); }
}
