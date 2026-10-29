using Dungeons.Core.Content;
using Dungeons.Core.Level;
using Dungeons.Core.Sim;
using Dungeons.Game.Input;
using Godot;

namespace Dungeons.Game;

public partial class GameplayScreen
{
    private ReplayDocument? _replayDocument;
    private ReplayPlayback? _playback;
    private bool _replayReported;
    public bool IsReplay => _playback is not null;
    public bool ReplayComplete => _playback?.Complete == true;
    public event Action? ReplayFinished;
    /// <summary>Replays verified commands without an attempt/profile/recorder. Seed, difficulty and editor origin come from the file.</summary>
    public void BuildReplay(LevelDocument level, ContentRegistry content, ReplayDocument replay)
    {
        ReplayVerification verification = ReplayVerifier.Verify(GameSession.Create(level, content, replay.Snapshot.Seed, replay.Snapshot.Difficulty, startOverride: replay.Snapshot.StartOverride), replay);
        if (!verification.Success) { throw new InvalidDataException($"{verification.Message} tick={verification.DivergingTick} expected={verification.ExpectedHash:x16} actual={verification.ActualHash:x16}"); }
        _replayDocument = replay; Build(level, content, replay.Snapshot.Seed);
    }
    private void ReportReplayCompletion()
    {
        if (!ReplayComplete || _replayReported) { return; } _replayReported = true;
        GD.Print($"REPLAY_PLAYBACK=PASS ticks={_playback!.ConsumedTicks} finalHash={Session.Hash():x16} state={Session.State}");
        // A partial recording can end during respawn. Keep Back above the death
        // overlay; terminal recordings retain their existing results controls.
        var layer = new CanvasLayer { Name = "ReplayStatus", Layer = Session.State == SessionState.Running ? 10 : 8 }; AddChild(layer);
        var centre = new CenterContainer(); layer.AddChild(centre); centre.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var panel = new PanelContainer { CustomMinimumSize = new(320, 96) }; centre.AddChild(panel);
        var box = new VBoxContainer(); panel.AddChild(box);
        box.AddChild(new Label { Text = Tr("REPLAY_COMPLETE"), HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart });
        var back = new Button { Text = Tr("BACK"), CustomMinimumSize = new(0, 48) }; box.AddChild(back); back.Pressed += () => QuitRequested?.Invoke();
        if (Session.State == SessionState.Running) { back.GrabFocus(); }
        ReplayFinished?.Invoke();
    }
}
