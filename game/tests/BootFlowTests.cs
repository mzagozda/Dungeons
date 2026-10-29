using Dungeons.Core.Util;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace Dungeons.Game.Tests;

[TestSuite, RequireGodotRuntime]
public sealed partial class BootFlowTests
{
    private sealed class DelayedLoader : IAppStartupLoader
    {
        internal readonly TaskCompletionSource<AppStartupData> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Cancelled;
        public Task<AppStartupData> LoadAsync(string directory, ILogger? log, CancellationToken cancellationToken)
        { cancellationToken.Register(() => { Cancelled = true; Completion.TrySetCanceled(cancellationToken); }); return Completion.Task; }
    }
    private sealed class RetryLoader(AppStartupData data) : IAppStartupLoader
    {
        private int _calls;
        public Task<AppStartupData> LoadAsync(string directory, ILogger? log, CancellationToken cancellationToken)
            => ++_calls == 1 ? Task.FromException<AppStartupData>(new IOException("Injected startup IO failure.")) : Task.FromResult(data);
    }
    private sealed partial class Counter : Node
    {
        internal int Frames;
        public override void _Process(double delta) => Frames++;
    }
    private static string Folder() => ProjectSettings.GlobalizePath("user://boot-test-" + Guid.NewGuid().ToString("N"));
    private static AppStartupData Data(string directory)
    { var content = ContentBridge.LoadRegistry(); return new(content, CatalogBridge.Scan(content, directory)); }

    [TestCase, GodotExceptionMonitor]
    public async Task DelayedContentKeepsTheTreeResponsiveAndLogoHasABoundedLifetime()
    {
        string directory = Folder(); var data = Data(System.IO.Path.Combine(directory, "levels")); var source = new DelayedLoader();
        var app = new AppRoot(directory, source); using ISceneRunner runner = ISceneRunner.Load(app, autoFree: true);
        var counter = new Counter(); app.AddChild(counter); await runner.SimulateFrames(4);
        AssertThat(app.StartupComplete).IsFalse(); AssertThat(app.HasNode("BootScreen")).IsTrue();
        TextureRect logo = app.GetNode("BootScreen").FindChildren("BootLogo", "TextureRect", true, false).OfType<TextureRect>().Single();
        AssertThat(logo.IsVisibleInTree()).IsTrue(); int before = counter.Frames;
        await Task.Delay(1650); await runner.SimulateFrames(2);
        AssertThat(counter.Frames > before + 2).IsTrue(); AssertThat(logo.IsVisibleInTree()).IsFalse();
        AssertThat(app.FindChildren("*", "Button", true, false).OfType<Button>().Any(button => button.Text == app.Tr("PLAY"))).IsFalse();
        source.Completion.SetResult(data); await AppTestReady.Wait(runner, app);
        AssertThat(app.HasNode("BootScreen")).IsFalse();
        AssertThat(app.FindChildren("*", "Button", true, false).OfType<Button>().Any(button => button.IsVisibleInTree() && button.Text == app.Tr("PLAY"))).IsTrue();
    }
    [TestCase, GodotExceptionMonitor]
    public async Task RemovingTheBootRootCancelsItsOutstandingLoader()
    {
        var container = new Node(); using ISceneRunner runner = ISceneRunner.Load(container, autoFree: true);
        var source = new DelayedLoader(); var app = new AppRoot(Folder(), source); container.AddChild(app); await runner.SimulateFrames(3);
        AssertThat(app.StartupComplete).IsFalse(); app.QueueFree(); await runner.SimulateFrames(2);
        AssertThat(source.Cancelled).IsTrue(); AssertThat(source.Completion.Task.IsCanceled).IsTrue();
    }
    [TestCase, GodotExceptionMonitor]
    public async Task ActualStartupValidatesOnAWorkerWithPreparedPreviewBytes()
    {
        int caller = System.Environment.CurrentManagedThreadId;
        AppStartupData data = await new AppStartupLoader().LoadAsync(Folder(), null, CancellationToken.None);
        AssertThat(data.WorkerThreadId != caller).IsTrue(); AssertThat(data.Content.IsValid).IsTrue();
        AssertThat(data.Catalogue.Single(entry => entry.Id == "core.mossy_hollow").PreviewBytes.Length > 0).IsTrue();
    }
    [TestCase, GodotExceptionMonitor]
    public async Task StartupFailureIsObservedAndTheNativeRetryCanLoadAgain()
    {
        if (DisplayServer.GetName() == "headless")
        {
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); bool observed = false;
            try { await new AppStartupLoader().LoadAsync(Folder(), null, cancelled.Token); } catch (OperationCanceledException) { observed = true; }
            AssertThat(observed).IsTrue(); return;
        }
        string directory = Folder(); var data = Data(System.IO.Path.Combine(directory, "levels"));
        var app = new AppRoot(directory, new RetryLoader(data)); using ISceneRunner runner = ISceneRunner.Load(app, autoFree: true); await runner.SimulateFrames(5);
        AssertThat(app.StartupComplete).IsFalse();
        Button retry = app.FindChildren("*", "Button", true, false).OfType<Button>().Single(button => button.IsVisibleInTree() && button.Text == app.Tr("RETRY"));
        retry.EmitSignal(BaseButton.SignalName.Pressed); await AppTestReady.Wait(runner, app);
        AssertThat(app.FindChildren("*", "Button", true, false).OfType<Button>().Any(button => button.IsVisibleInTree() && button.Text == app.Tr("PLAY"))).IsTrue();
    }
}
