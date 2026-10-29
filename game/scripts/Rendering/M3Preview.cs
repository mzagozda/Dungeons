using Dungeons.Core.Level;
using Dungeons.Core.Sim;
using Godot;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Dungeons.Game.Rendering;

/// <summary>Temporary rendered acceptance harness; normal menu wiring follows after the visual foundation.</summary>
public partial class M3Preview : Node3D
{
    private WorldView _view = null!;
    private GameSession _session = null!;
    private double _accumulator;
    private int _frames;
    private string _screenshot = "";
    private readonly StringBuilder _capture = new("frame,frameMs,processDeltaMs,drawCalls,triangles,textureBytes,enabledLights,chunks,renderCpuMs,renderGpuMs\n");
    private double _captureSeconds, _captured;
    private int _samples;
    private ulong _previousFrame;
    private bool _editorPreview;
    public override void _Ready()
    {
        try { Initialize(); }
        catch (Exception error) { GD.PushError(error.ToString()); SetProcess(false); GetTree().Quit(1); }
    }
    private void Initialize()
    {
        var content = ContentBridge.LoadRegistry(); LevelDocument level = ContentBridge.LoadBuiltins(content)[0];
        foreach (string argument in ScreenshotRun.Arguments)
        {
            if (argument.StartsWith("--m3-screenshot=", StringComparison.Ordinal)) { _screenshot = argument["--m3-screenshot=".Length..]; }
            else if (argument.StartsWith("--perf-capture=", StringComparison.Ordinal)) { _captureSeconds = double.Parse(argument["--perf-capture=".Length..], CultureInfo.InvariantCulture); }
            else if (argument == "--perf-uncapped") { DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled); }
            else if (argument.StartsWith("--m3-location=", StringComparison.Ordinal))
            {
                string[] parts = argument["--m3-location=".Length..].Split(','); JsonObject source = level.ToJsonObject();
                foreach (JsonNode? node in source["entities"]!.AsArray())
                { if (node!["type"]!.GetValue<string>() == "core:player_spawn") { node["x"] = float.Parse(parts[0], CultureInfo.InvariantCulture); node["z"] = float.Parse(parts[1], CultureInfo.InvariantCulture); } }
                LevelLoadResult moved = LevelLoader.Load(source.ToJsonString(), content);
                if (!moved.Success) { throw new InvalidDataException("Invalid visual test spawn: " + string.Join('\n', moved.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))); }
                level = moved.Document!;
            }
        }
        _editorPreview = ScreenshotRun.Arguments.Contains("--editor-preview");
        _view = new WorldView(); AddChild(_view);
        if (ScreenshotRun.Active) { _view.SetProcess(false); }
        if (_editorPreview)
        {
            _view.Build(level); EntityPlacement spawn = level.Entities.First(placement => placement.Definition.Id == "core:player_spawn");
            _view.FocusPreview(new(spawn.Position.X, level.BakeGrid().HeightAt(spawn.Position), spawn.Position.Z));
        }
        else { _session = new(level, 1); _view.Build(level, content, _session); }
        if (_captureSeconds > 0) { RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true); }
        GD.Print($"PERF_CONTEXT maxFps={Engine.MaxFps} timeScale={Engine.TimeScale} refreshRate={DisplayServer.ScreenGetRefreshRate()} vsync={DisplayServer.WindowGetVsyncMode()} physicsTicks={Engine.PhysicsTicksPerSecond} scale3D={GetViewport().Scaling3DScale}");
    }
    public override void _Process(double delta)
    {
        if (ScreenshotRun.Active)
        {
            delta = GameConstants.Dt;
            if (!_view.ReadyForPlay) { _view._Process(0); return; }
            _view._Process(delta);
        }
        ulong now = Time.GetTicksUsec(); double wallMilliseconds = _previousFrame == 0 ? delta * 1000 : (now - _previousFrame) / 1000.0; _previousFrame = now;
        if (_session is not null && (!ScreenshotRun.Active || _frames < 120))
        {
            _accumulator += System.Math.Min(delta, GameConstants.MaxFrameDelta); int steps = 0;
            while (_accumulator >= GameConstants.Dt && steps++ < GameConstants.MaxTicksPerFrame) { _session.Step(default(PlayerInput)); _accumulator -= GameConstants.Dt; }
            if (steps > GameConstants.MaxTicksPerFrame) { _accumulator = 0; }
            _view.Interpolate((float)(_accumulator / GameConstants.Dt)); _session.DrainEvents();
        }
        if (!_view.ReadyForPlay || ++_frames < 120 || _screenshot.Length == 0) { return; }
        if (ScreenshotRun.Active && _frames == 120) { return; } // Render the final tick before readback.
        if (_captureSeconds > 0)
        {
            _captured += wallMilliseconds / 1000; _samples++;
            double cpu = RenderingServer.ViewportGetMeasuredRenderTimeCpu(GetViewport().GetViewportRid()) + RenderingServer.GetFrameSetupTimeCpu(), gpu = RenderingServer.ViewportGetMeasuredRenderTimeGpu(GetViewport().GetViewportRid());
            _capture.Append(CultureInfo.InvariantCulture, $"{_samples},{wallMilliseconds:F4},{delta * 1000:F4},{RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame)},{RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame)},{RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TextureMemUsed)},{_view.EnabledLightCount},{_view.ChunkCount},{cpu:F4},{gpu:F4}\n");
            if (_captured < _captureSeconds) { return; }
            using Godot.FileAccess csv = Godot.FileAccess.Open(_screenshot + ".csv", Godot.FileAccess.ModeFlags.Write); csv.StoreString(_capture.ToString());
        }
        using Image image = GetViewport().GetTexture().GetImage(); Error saved = ScreenshotRun.Save(image, _screenshot);
        ScreenshotRun.Complete(saved == Error.Ok, image, _session);
        GD.Print($"M3_PREVIEW={(saved == Error.Ok ? "PASS" : "FAIL")} chunks={_view.ChunkCount} water={_view.WaterMeshCount} lights={_view.EnabledLightCount} hero={_session?.World.Players.Count ?? 0} preview={_editorPreview} drawCalls={RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame)} triangles={RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame)}");
        GetTree().Quit(saved == Error.Ok ? 0 : 1);
    }
}
