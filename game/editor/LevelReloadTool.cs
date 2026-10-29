using System.Diagnostics;
using Dungeons.Core.Content;
using Dungeons.Core.Level;
using Dungeons.Core.Sim;
using Dungeons.Game.Input;
using Dungeons.Game.Rendering;
using Dungeons.Game.UI;
using Godot;

namespace Dungeons.Game.Editor;

/// <summary>Desktop development host: edit level.json on disk to start a new unrecorded epoch.</summary>
public partial class LevelReloadTool : Node3D
{
    public string LevelPath { get; set; } = "res://levels/mossy_hollow/level.json";
    public GameSession Session { get; private set; } = null!;
    public WorldView View { get; private set; } = null!;
    public LevelDocument Document { get; private set; } = null!;
    public string LastError { get; private set; } = "";
    public int RejectedChanges { get; private set; }
    public double LastReloadMilliseconds { get; private set; }
    private ContentRegistry _content = null!;
    private readonly InputAdapter _input = new();
    private Label _status = null!;
    private (long ticks, long length) _observed;
    private double _poll, _accumulator;
    private Stopwatch? _reload;

    public override void _Ready()
    {
        try
        {
            foreach (string argument in OS.GetCmdlineUserArgs())
            { if (argument.StartsWith("--reload-level=", StringComparison.Ordinal)) { LevelPath = argument["--reload-level=".Length..]; } }
            LevelPath = ProjectSettings.GlobalizePath(LevelPath); _content = ContentBridge.LoadRegistry();
            Document = ReadDocument(); _observed = Stamp(); Session = new(Document, seed: 42, record: false);
            InputAdapter.EnsureBindings(); View = new(); AddChild(View); View.Build(Document, _content, Session);
            var hud = new Hud(); AddChild(hud); hud.Build(Session, View);
            var layer = new CanvasLayer { Layer = 10 }; AddChild(layer);
            _status = new Label { Position = new(16, 48), Text = Tr("EDITOR_RELOAD_HELP") }; layer.AddChild(_status);
        }
        catch (Exception error) { GD.PushError(error.ToString()); SetProcess(false); SetProcessUnhandledInput(false); }
    }

    public override void _Process(double delta)
    {
        if (Session is null || View is null) { return; }
        if (_reload is not null && View.ReadyForPlay)
        {
            LastReloadMilliseconds = _reload.Elapsed.TotalMilliseconds; _reload = null;
            _status.Text = string.Format(Tr("EDITOR_RELOAD_READY"), Session.ReloadRevision, LastReloadMilliseconds);
            GD.Print($"M9_HOT_RELOAD revision={Session.ReloadRevision} milliseconds={LastReloadMilliseconds:F3} source={Document.SourceHash}");
        }
        _poll += delta;
        if (_poll >= .05 && _reload is null && View.ReadyForPlay)
        {
            _poll = 0;
            try { var stamp = Stamp(); if (stamp != _observed) { _observed = stamp; ReloadFromDisk(); } }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { Reject(error.Message); }
        }
        if (!View.ReadyForPlay || _reload is not null || Session.State != SessionState.Running) { _accumulator = 0; return; }
        _input.Sample(View.Camera, Session.World);
        if (_input.GamepadDisconnected) { Session.Pause(); _input.Clear(); return; }
        _accumulator += System.Math.Min(delta, GameConstants.MaxFrameDelta); int steps = 0;
        while (_accumulator >= GameConstants.Dt && steps++ < GameConstants.MaxTicksPerFrame)
        { Session.Step(_input.ConsumeTickInput(Session.World)); _accumulator -= GameConstants.Dt; }
        if (_accumulator >= GameConstants.Dt) { _accumulator = 0; }
        View.ConsumeEvents(Session.DrainEvents()); View.Interpolate((float)(_accumulator / GameConstants.Dt));
    }

    private void ReloadFromDisk()
    {
        var timer = Stopwatch.StartNew();
        try
        {
            LevelDocument next = ReadDocument();
            if (next.SourceHash == Document.SourceHash)
            { if (LastError.Length > 0) { _status.Text = Tr("EDITOR_RELOAD_UNCHANGED"); } LastError = ""; return; }
            ChunkRect changed = LevelDocumentDiff.ChangedChunks(Document, next);
            Session.ReloadLevel(next); View.Reload(next, Session, changed); Document = next;
            _input.Clear(); _accumulator = 0; LastError = ""; _reload = timer;
        }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { Reject(error.Message); }
    }

    private LevelDocument ReadDocument()
    {
        using Stream stream = System.IO.File.OpenRead(LevelPath);
        if (stream.Length > LevelPackage.MaxBytes) { throw new InvalidDataException("Level exceeds 64 MiB."); }
        IAssetSource assets = LevelAssetBridge.Read(Path.GetDirectoryName(LevelPath)!, LevelPackage.MaxBytes - (int)stream.Length);
        LevelLoadResult result = LevelLoader.Load(stream, _content, assets: assets);
        if (!result.Success || result.Document is null) { throw new InvalidDataException(string.Join('\n', result.Diagnostics)); }
        return result.Document;
    }
    private (long ticks, long length) Stamp()
    { var file = new FileInfo(LevelPath); return (file.LastWriteTimeUtc.Ticks, file.Length); }
    private void Reject(string message)
    {
        if (LastError == message) { return; }
        LastError = message; RejectedChanges++; _status.Text = string.Format(Tr("EDITOR_RELOAD_REJECTED"), message);
        GD.Print("M9_RELOAD_REJECTED " + message);
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if (Session is null) { return; }
        if (input.IsActionPressed("pause") && input is not InputEventKey { Echo: true })
        { if (Session.State == SessionState.Paused) { Session.Resume(); } else { Session.Pause(); } _input.Clear(); GetViewport().SetInputAsHandled(); }
        else if (Session.State == SessionState.Running) { _input.HandleEvent(input); }
    }
    public override void _Notification(int what)
    { if (what == NotificationApplicationFocusOut && Session is not null) { Session.Pause(); _input.Clear(); } }
}
