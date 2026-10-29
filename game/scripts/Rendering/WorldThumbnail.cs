using System.Diagnostics;
using Dungeons.Core.Level;
using Godot;

namespace Dungeons.Game.Rendering;

public partial class WorldView
{
    /// <summary>Renders an isolated, session-free overview to PNG bytes. Call on the active SceneTree's main thread.
    /// Requires a real rendering backend; a headless/dummy renderer cannot capture images.</summary>
    public static async Task<byte[]> CapturePreview(LevelDocument document, int width = 320, int height = 180)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (width is < 2 or > 2048 || height is < 2 or > 2048) { throw new ArgumentOutOfRangeException(nameof(width), "Thumbnail dimensions must be 2–2048 pixels."); }
        if (OS.GetThreadCallerId() != OS.GetMainThreadId() || Engine.GetMainLoop() is not SceneTree tree)
        { throw new InvalidOperationException("Thumbnail capture requires the active SceneTree's main thread."); }
        if (DisplayServer.GetName() == "headless") { throw new NotSupportedException("PNG capture requires a real rendering backend, not the headless dummy renderer."); }
        // A caller may be in _Ready while SceneTree.Root is still installing its main scene.
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        var viewport = new SubViewport { Name = "LevelThumbnail", Size = new(width, height), OwnWorld3D = true,
            ProcessMode = ProcessModeEnum.Always, RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            TransparentBg = false, AudioListenerEnable2D = false, AudioListenerEnable3D = false };
        tree.Root.AddChild(viewport);
        try
        {
            var view = new WorldView(); viewport.AddChild(view); view.Build(document);
            bool compatibility = RenderingServer.GetCurrentRenderingMethod() == "gl_compatibility";
            view.ApplyGraphics(GraphicsOptions.ForPreset(compatibility ? QualityPreset.Low : QualityPreset.High, false), 1, compatibility);
            long started = Stopwatch.GetTimestamp();
            while (!view.ReadyForPlay)
            {
                if (Stopwatch.GetElapsedTime(started).TotalSeconds > 30) { throw new TimeoutException("Thumbnail terrain did not finish within 30 seconds."); }
                await view.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            }
            view.SetProcess(false);
            foreach (var light in view._lights) { light.light.LightEnergy = light.energy; }
            view.FrameOverview(width / (float)height);
            // The final chunk and camera changes must reach the renderer before reading its target.
            await view.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            await view.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            await view.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using Image image = viewport.GetTexture().GetImage();
            if (image.IsEmpty() || image.GetWidth() != width || image.GetHeight() != height) { throw new IOException("The renderer returned an empty or incorrectly sized thumbnail."); }
            byte[] png = image.SavePngToBuffer(); if (png.Length == 0) { throw new IOException("PNG encoding failed."); }
            return png;
        }
        finally { tree.Root.RemoveChild(viewport); viewport.Free(); }
    }

    private void FrameOverview(float aspect)
    {
        Aabb bounds = new(new Vector3(0, -1, 0), new Vector3(_document.Width, 1, _document.Height));
        Include(this);
        Vector3 center = bounds.GetCenter(); float distance = bounds.Size.Length() + 10;
        Camera.Position = center + new Vector3(1, 1.4f, 1).Normalized() * distance; Camera.LookAt(center);
        Basis inverse = Camera.GlobalBasis.Inverse(); float halfWidth = 0, halfHeight = 0;
        for (int corner = 0; corner < 8; corner++)
        {
            Vector3 projected = inverse * (bounds.GetEndpoint(corner) - center);
            halfWidth = MathF.Max(halfWidth, MathF.Abs(projected.X)); halfHeight = MathF.Max(halfHeight, MathF.Abs(projected.Y));
        }
        Camera.KeepAspect = Camera3D.KeepAspectEnum.Height;
        Camera.SetOrthogonal(MathF.Max(halfHeight, halfWidth / aspect) * 2.16f, .1f, distance * 2 + 100);
        _sun.DirectionalShadowMaxDistance = distance * 2; _focus = center; CullLights();
        void Include(Node node)
        {
            if (node is VisualInstance3D visual && visual.IsVisibleInTree())
            {
                Aabb local = visual.GetAabb();
                for (int corner = 0; corner < 8; corner++) { bounds = bounds.Expand(visual.GlobalTransform * local.GetEndpoint(corner)); }
            }
            foreach (Node child in node.GetChildren()) { Include(child); }
        }
    }
}
