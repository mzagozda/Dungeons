using System.Diagnostics;
using Godot;

namespace Dungeons.Game;

public partial class AppRoot
{
    private readonly IAppStartupLoader _startupLoader = new AppStartupLoader();
    private CancellationTokenSource? _startupCancellation;
    private Task<AppStartupData>? _startupTask;
    private CanvasLayer? _boot;
    private TextureRect? _bootLogo;
    private readonly Stopwatch _bootClock = new();
    /// <summary>Successful application data/service initialization, independently of level loading.</summary>
    public bool StartupComplete { get; private set; }
    /// <summary>Embeds an alternate cancellable startup source without changing simulation rules.</summary>
    public AppRoot(string dataDirectory, IAppStartupLoader startupLoader) : this(dataDirectory)
    { _startupLoader = startupLoader ?? throw new ArgumentNullException(nameof(startupLoader)); }

    private void BeginStartup()
    {
        if (_screenshotTest is null)
        {
            if (_logs is null) { _logs = new(); AddChild(_logs); _logs.Initialize(DataDirectory); }
            _boot = new CanvasLayer { Name = "BootScreen", Layer = 100 }; AddChild(_boot);
            var background = new ColorRect { Color = new("#1a1a1f") }; _boot.AddChild(background); background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            var centre = new CenterContainer(); _boot.AddChild(centre); centre.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            var box = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center }; centre.AddChild(box); box.AddThemeConstantOverride("separation", 16);
            _bootLogo = new TextureRect { Name = "BootLogo", Texture = GD.Load<Texture2D>("res://assets/ui/probe_icon.svg"), CustomMinimumSize = new(128, 128), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
            box.AddChild(_bootLogo); box.AddChild(new Label { Text = Tr("GAME_TITLE"), HorizontalAlignment = HorizontalAlignment.Center });
            box.AddChild(new Label { Name = "BootStatus", Text = Tr("LOADING_GAME"), HorizontalAlignment = HorizontalAlignment.Center });
            _back = ConfirmQuit;
        }
        _bootClock.Restart(); _startupCancellation = new();
        // A synchronous preparation failure follows the same retry/CLI error path.
        _startupTask = _startupLoader.LoadAsync(LevelsDirectory, _logs?.Logger, _startupCancellation.Token);
    }
    private bool AdvanceStartup()
    {
        if (_startupTask is null) { BeginStartup(); return false; }
        if (_bootLogo is not null && _bootClock.Elapsed.TotalSeconds >= 1.5) { _bootLogo.Visible = false; }
        if (!_startupTask.IsCompleted) { return false; }
        AppStartupData data = _startupTask.GetAwaiter().GetResult(); _content = data.Content; _catalog = data.Catalogue;
        ClearBoot(); return true;
    }
    private void ClearBoot()
    { _boot?.QueueFree(); _boot = null; _bootLogo = null; _bootClock.Stop(); }
    private void CancelStartup()
    {
        _startupCancellation?.Cancel();
        // The worker owns independent FileAccess/DirAccess instances and no GPU or
        // scene-tree work. Drain it before engine teardown can destroy its IO backend.
        if (_startupTask is not null)
        { try { _startupTask.GetAwaiter().GetResult(); } catch (Exception) { /* Completion is observed; the active error path reports failures. */ } }
        _startupTask = null; _startupCancellation?.Dispose(); _startupCancellation = null;
    }
    private void RetryStartup()
    { CancelStartup(); _menu?.QueueFree(); _menu = null; StartupComplete = false; _startupPending = true; }
    public override void _ExitTree() { CancelStartup(); }
}
