using Dungeons.Core.Math;
using Dungeons.Core.Sim;
using Dungeons.Game.Input;
using Xunit;

namespace Dungeons.Core.Tests;

public sealed class InputLatchTests
{
    [Fact]
    public void InputLatchTestOneFrame240HzTapSurvivesUntilTickAndIsConsumedOnce()
    {
        var latch = new CommandLatch();
        latch.Sample(new(1, 0), Vec2.North, true, InputButtons.Attack, InputButtons.Attack);
        for (int frame = 0; frame < 3; frame++) { latch.Sample(new(0, 1), new(1, 0), true, InputButtons.None, InputButtons.None); }
        PlayerInput first = latch.ConsumeTickInput(), second = latch.ConsumeTickInput();
        Assert.Equal(InputButtons.Attack, first.Pressed); Assert.Equal(InputButtons.None, first.Held);
        Assert.Equal(new Vec2(0, 1), first.Move); Assert.Equal(new Vec2(1, 0), first.Aim); Assert.True(first.AimValid);
        Assert.Equal(InputButtons.None, second.Pressed); Assert.Equal(first.Move, second.Move);
        latch.Press(InputButtons.Roll); latch.Press(InputButtons.Potion); latch.Sample(default, default, false, InputButtons.Attack, InputButtons.Attack);
        Assert.Equal(InputButtons.Roll | InputButtons.Potion | InputButtons.Attack, latch.ConsumeTickInput().Pressed);
        Assert.Equal(InputButtons.Attack, latch.ConsumeTickInput().Held);
        latch.Press(InputButtons.Ranged); latch.Clear(); Assert.Equal(default, latch.ConsumeTickInput());
    }
}
