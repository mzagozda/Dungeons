using System.Diagnostics;
using GdUnit4;
using static GdUnit4.Assertions;

namespace Dungeons.Game.Tests;

internal static class AppTestReady
{
    internal static async Task Wait(ISceneRunner runner, AppRoot app)
    {
        var clock = Stopwatch.StartNew();
        while (!app.StartupComplete && clock.Elapsed.TotalSeconds < 10)
        { await runner.AwaitIdleFrame(); await Task.Delay(1); }
        AssertThat(app.StartupComplete).IsTrue(); await runner.SimulateFrames(2);
    }
}
