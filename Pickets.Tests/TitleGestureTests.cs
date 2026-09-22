using System.Windows;

namespace Pickets.Tests;

public sealed class TitleGestureTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void SmallJitterRemainsClick_AndEitherAxisCanStartDrag(double scale)
    {
        var gesture = new TitleGesture();
        var start = new Point(-500, 200);
        var dpi = new DpiScale(scale, scale);
        gesture.Begin(start, dpi, 4, 6);
        Assert.False(gesture.Move(start + new Vector(3 * scale, -5 * scale)));
        Assert.True(gesture.Release(start));
        Assert.False(gesture.Release(start));

        foreach (var offset in new[] { new Vector(4 * scale, 0), new Vector(-4 * scale, 0),
            new Vector(0, 6 * scale), new Vector(0, -6 * scale) })
        {
            gesture.Begin(start, dpi, 4, 6);
            Assert.True(gesture.Move(start + offset));
            // Returning to the original position after a drag must never collapse the stack.
            Assert.False(gesture.Release(start));
        }
    }

    [Fact]
    public void LostCaptureOrEscapeCancelsClick_AndNextPressStillWorks()
    {
        var gesture = new TitleGesture();
        gesture.Begin(new Point(20, 20), new DpiScale(1, 1), 4, 4);
        gesture.Cancel();
        Assert.False(gesture.Release(new Point(20, 20)));
        gesture.Begin(new Point(20, 20), new DpiScale(1, 1), 4, 4);
        Assert.True(gesture.Release(new Point(20, 20)));
    }

    [Fact]
    public void ReleaseBeyondToleranceWithoutMoveEventDoesNotToggle()
    {
        var gesture = new TitleGesture();
        gesture.Begin(new Point(20, 20), new DpiScale(1, 1), 4, 4);
        Assert.False(gesture.Release(new Point(40, 20)));
    }
}
