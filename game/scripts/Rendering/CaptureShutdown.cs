using System.Diagnostics;
using Godot;

namespace Dungeons.Game.Rendering;

/// <summary>Releases a completed fixture while native servers and managed bindings are still alive.</summary>
public partial class CaptureShutdown : Node
{
    private Node? _fixture;
    private readonly Stopwatch _clock = new();
    private int _exitCode, _frames, _stage;
    internal static void Begin(Node fixture, int exitCode)
    {
        fixture.ProcessMode = ProcessModeEnum.Disabled;
        fixture.GetTree().Root.AddChild(new CaptureShutdown { _fixture = fixture, _exitCode = exitCode, ProcessMode = ProcessModeEnum.Always });
    }
    public override void _Ready() { _fixture!.QueueFree(); _fixture = null; _clock.Start(); }
    public override void _Process(double delta)
    {
        // No signal awaiter, forced collection or blocking sleep survives Quit.
        // The rendered frames retire queued nodes/RIDs; the wall-clock interval
        // also permits native audio playbacks to drain when fixed-FPS is fast.
        if (_stage == 0) { if (++_frames < 3) { return; } _stage = 1; _clock.Restart(); return; }
        if (_stage == 1)
        {
            if (_clock.Elapsed.TotalSeconds < System.Math.Clamp(AudioServer.GetOutputLatency() + AudioServer.GetTimeToNextMix(), .2, 1)) { return; }
            _stage = 2; _frames = 0; return;
        }
        if (++_frames < 3) { return; }
        bool released = !ScreenshotRun.HasBoundGame;
        if (!released) { GD.PushError("Capture retained its gameplay screen after scene release."); _exitCode = 1; }
        GD.Print($"CAPTURE_SHUTDOWN={(released ? "PASS" : "FAIL")} bindingReleased={released}");
        SetProcess(false); GetTree().Quit(_exitCode);
    }
}
