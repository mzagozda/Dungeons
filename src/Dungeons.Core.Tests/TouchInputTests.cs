using Dungeons.Core.Math;
using Dungeons.Core.Sim;
using Dungeons.Game.Input;
using Xunit;

namespace Dungeons.Core.Tests;

public sealed class TouchInputTests
{
    private static TouchState Create()
    { var touch = new TouchState(); touch.Layout.Configure(new(880, 400), new(24, 0), new(24, 16)); return touch; }
    private static TouchTarget Target(TouchState touch, InputButtons action)
    { foreach (TouchTarget target in touch.Layout.Targets) { if (target.Action == action) { return target; } } throw new ArgumentException(nameof(action)); }

    [Fact]
    public void FloatingJoystickHasDeadZoneAnalogRangeFollowAndNoMotionFromWorldTap()
    {
        TouchState touch = Create(); Assert.True(touch.Press(41, new(25, 20), 0)); Assert.Equal(Vec2.Zero, touch.Sample(0).Move);
        touch.Release(41, new(25, 20), .01); Assert.Equal(Vec2.Zero, touch.Sample(.01).Move);
        touch.Press(41, new(200, 200), 1); touch.Drag(41, new(219, 200), 1.01); Assert.Equal(Vec2.Zero, touch.Sample(1.01).Move);
        touch.Drag(41, new(240, 200), 1.02); Assert.Equal(new Vec2(.5f, 0), touch.Sample(1.02).Move);
        touch.Drag(41, new(350, 200), 1.03); Assert.Equal(new Vec2(260, 200), touch.JoystickOrigin); Assert.Equal(new Vec2(1, 0), touch.Sample(1.03).Move);
        touch.Release(41, new(350, 200), 1.04); Assert.False(touch.JoystickActive); Assert.Equal(Vec2.Zero, touch.Sample(1.04).Move);
        Assert.False(touch.Press(8, new(500, 100), 2)); Assert.Equal(default, touch.Sample(2).Pressed);
    }

    [Fact]
    public void MultitouchButtonTapAndSlideOffDoNotInterruptMovement()
    {
        TouchState touch = Create(); touch.Press(7, new(200, 250), 0); touch.Drag(7, new(250, 250), .01);
        TouchTarget attack = Target(touch, InputButtons.Attack), roll = Target(touch, InputButtons.Roll);
        Assert.True(touch.Press(99, attack.Centre, .02)); Assert.False(touch.Press(101, attack.Centre, .02));
        Assert.Equal(InputButtons.Attack, touch.Sample(.03).Held); touch.Release(99, attack.Centre, .04);
        TouchFrame tap = touch.Sample(.04); Assert.Equal(InputButtons.Attack, tap.Pressed); Assert.True(tap.Move.X > 0);
        Assert.Equal(InputButtons.None, touch.Sample(.05).Pressed); touch.Press(11, roll.Centre, .06);
        touch.Drag(11, roll.Centre + new Vec2(100, 0), .07); touch.Drag(11, roll.Centre, .08); touch.Release(11, roll.Centre, .09);
        Assert.Equal(InputButtons.None, touch.Sample(.09).Pressed); Assert.True(touch.JoystickActive);
        touch.Press(11, roll.Centre, .1); touch.Release(11, roll.Centre, .11, cancelled: true); Assert.Equal(InputButtons.None, touch.Sample(.11).Pressed);
        touch.Clear(); Assert.False(touch.JoystickActive); Assert.Equal(default, touch.Sample(.12).Held);
    }

    [Fact]
    public void BowTapAutoAimsDragFiresOnReleaseAndReturningToDeadZoneCancels()
    {
        TouchState touch = Create(); TouchTarget bow = Target(touch, InputButtons.Ranged);
        touch.Press(1, bow.Centre, 0); Assert.Equal(InputButtons.None, touch.Sample(.02).Pressed);
        touch.Release(1, bow.Centre, .05); TouchFrame tap = touch.Sample(.05); Assert.Equal(InputButtons.Ranged, tap.Pressed); Assert.False(tap.AimValid);
        touch.Press(2, bow.Centre, 1); touch.Drag(2, bow.Centre + new Vec2(30, -40), 1.01);
        TouchFrame aim = touch.Sample(1.01); Assert.Equal(InputButtons.None, aim.Pressed); Assert.True(aim.AimValid); Assert.Equal(new Vec2(.6f, -.8f), aim.Aim);
        touch.Release(2, bow.Centre + new Vec2(30, -40), 1.02); TouchFrame shot = touch.Sample(1.02);
        Assert.Equal(InputButtons.Ranged, shot.Pressed); Assert.True(shot.AimValid); Assert.Equal(aim.Aim, shot.Aim);
        touch.Press(3, bow.Centre, 2); touch.Drag(3, bow.Centre + new Vec2(40, 0), 2.01); touch.Release(3, bow.Centre + new Vec2(5, 0), 2.02);
        Assert.Equal(InputButtons.None, touch.Sample(2.02).Pressed);
        touch.Press(4, bow.Centre, 3); touch.Sample(3.13); touch.Release(4, bow.Centre, 3.14); Assert.Equal(InputButtons.None, touch.Sample(3.14).Pressed);
    }

    [Fact]
    public void BowReleaseAimSurvives240HzFramesUntilTickThenClears()
    {
        var latch = new CommandLatch(); latch.Sample(new(1, 0), default, false, 0, 0); latch.Press(InputButtons.Ranged, Vec2.North, true);
        for (int i = 0; i < 3; i++) { latch.Sample(new(.5f, 0), new(1, 0), false, 0, 0); }
        PlayerInput shot = latch.ConsumeTickInput(); Assert.Equal(InputButtons.Ranged, shot.Pressed); Assert.True(shot.AimValid); Assert.Equal(Vec2.North, shot.Aim); Assert.Equal(new Vec2(.5f, 0), shot.Move);
        PlayerInput next = latch.ConsumeTickInput(); Assert.Equal(InputButtons.None, next.Pressed); Assert.False(next.AimValid);
        latch.Press(InputButtons.Ranged, Vec2.North, true); latch.Clear(); Assert.Equal(default, latch.ConsumeTickInput());
    }

    [Theory]
    [InlineData(880, 400, 24, 16, false)]
    [InlineData(880, 400, 24, 16, true)]
    [InlineData(1024, 768, 0, 20, false)]
    [InlineData(1024, 768, 0, 20, true)]
    public void PhoneAndTabletArcFitsSafeAreaAndMirrorsWithoutOverlap(float width, float height, float sideInset, float bottomInset, bool left)
    {
        var layout = new TouchLayout(); layout.Configure(new(width, height), new(sideInset, 0), new(sideInset, bottomInset), leftHanded: left);
        TouchTarget[] buttons = layout.Targets.ToArray(); Assert.Equal(new[] { 84f, 64, 64, 56 }, buttons.Select(button => button.Radius * 2));
        foreach (TouchTarget button in buttons)
        {
            Assert.InRange(button.Centre.X - button.Radius, layout.Minimum.X, layout.Maximum.X);
            Assert.InRange(button.Centre.X + button.Radius, layout.Minimum.X, layout.Maximum.X);
            Assert.InRange(button.Centre.Z - button.Radius, layout.Minimum.Z, layout.Maximum.Z);
            Assert.InRange(button.Centre.Z + button.Radius, layout.Minimum.Z, layout.Maximum.Z);
            foreach (TouchTarget other in buttons) { if (other.Action != button.Action) { Assert.True((button.Centre - other.Centre).Length >= button.Radius + other.Radius + 12); } }
        }
        Assert.Equal(!left, layout.MovementSide(new(sideInset + 80, 100)));
        layout.SetPosition(InputButtons.Potion, new(0, 0)); Assert.InRange(layout.Targets[3].Centre.X - layout.Targets[3].Radius, layout.Minimum.X, layout.Maximum.X);
    }
}
