namespace Pickets.Tests;

public sealed class RenderFrameLoopTests
{
    [Theory]
    [InlineData(60)]
    [InlineData(120)]
    [InlineData(144)]
    [InlineData(165)]
    [InlineData(240)]
    public void EveryUniqueFrameUpdatesAllAnimations_WithoutA60HzLimit(int hz)
    {
        EventHandler? subscribed = null;
        var starts = 0;
        var stops = 0;
        using var loop = new RenderFrameLoop(handler => { starts++; subscribed = handler; },
            _ => { stops++; subscribed = null; });
        var first = 0;
        var second = 0;
        Action<TimeSpan> a = _ => first++;
        Action<TimeSpan> b = _ => second++;
        loop.AddAnimation(a);
        loop.AddAnimation(b);
        Assert.Equal(1, starts);
        for (var frame = 0; frame < hz; frame++)
        {
            var time = TimeSpan.FromSeconds((double)frame / hz);
            Assert.NotNull(subscribed);
            loop.ProcessFrame(time);
            loop.ProcessFrame(time); // Extra WPF layout callback for the same target frame.
        }
        Assert.Equal(hz, first);
        Assert.Equal(hz, second);
        loop.RemoveAnimation(a);
        Assert.Equal(0, stops);
        loop.RemoveAnimation(b);
        Assert.Equal(1, stops);
        Assert.Null(subscribed);
    }

    [Fact]
    public void GeometryStorm_RefreshesChromeOnceAfterEveryStackHasMoved()
    {
        using var loop = new RenderFrameLoop(_ => { }, _ => { });
        var positions = new int[3];
        var refreshes = 0;
        void Refresh()
        {
            Assert.All(positions, position => Assert.Equal(10, position));
            refreshes++;
        }
        for (var i = 0; i < positions.Length; i++)
        {
            var index = i;
            loop.AddAnimation(_ =>
            {
                positions[index] = 10;
                for (var change = 0; change < 100; change++) loop.Request(Refresh);
            });
        }
        for (var change = 0; change < 1000; change++) loop.Request(Refresh);
        Assert.Equal(0, refreshes);
        loop.ProcessFrame(TimeSpan.Zero);
        Assert.Equal(1, refreshes);
    }

    [Fact]
    public void Completion_RefreshesFinalGeometryAndDetachesInTheSameFrame()
    {
        var subscribed = false;
        using var loop = new RenderFrameLoop(_ => subscribed = true, _ => subscribed = false);
        var finalRefresh = false;
        Action<TimeSpan> animation = null!;
        animation = _ =>
        {
            loop.RemoveAnimation(animation);
            loop.Request(() => finalRefresh = true);
        };
        loop.AddAnimation(animation);
        loop.ProcessFrame(TimeSpan.FromMilliseconds(220));
        Assert.True(finalRefresh);
        Assert.False(subscribed);
    }

    [Fact]
    public void WorkQueuedByChrome_WaitsForNextFrameRatherThanReentering()
    {
        using var loop = new RenderFrameLoop(_ => { }, _ => { });
        var calls = 0;
        Action update = null!;
        update = () => { calls++; loop.Request(update); loop.ProcessFrame(TimeSpan.FromMilliseconds(1)); };
        loop.Request(update);
        loop.ProcessFrame(TimeSpan.Zero);
        Assert.Equal(1, calls);
        loop.ProcessFrame(TimeSpan.Zero);
        Assert.Equal(1, calls);
        loop.ProcessFrame(TimeSpan.FromMilliseconds(7));
        Assert.Equal(2, calls);
    }

    [Fact]
    public void RemovingAnotherAnimationDuringFrame_DoesNotUpdateAClosedStack()
    {
        using var loop = new RenderFrameLoop(_ => { }, _ => { });
        var closedCalls = 0;
        Action<TimeSpan> closed = _ => closedCalls++;
        loop.AddAnimation(_ => loop.RemoveAnimation(closed));
        loop.AddAnimation(closed);
        loop.ProcessFrame(TimeSpan.Zero);
        Assert.Equal(0, closedCalls);
    }

    [Fact]
    public void Dispose_DetachesAndDropsPendingWork()
    {
        var stops = 0;
        var calls = 0;
        var loop = new RenderFrameLoop(_ => { }, _ => stops++);
        loop.AddAnimation(_ => calls++);
        loop.Request(() => calls++);
        loop.Dispose();
        loop.Dispose();
        loop.Request(() => calls++);
        loop.AddAnimation(_ => calls++);
        loop.ProcessFrame(TimeSpan.Zero);
        Assert.Equal(1, stops);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void FailedAnimation_DoesNotLeaveAPermanentRenderingSubscription()
    {
        var stops = 0;
        using var loop = new RenderFrameLoop(_ => { }, _ => stops++);
        loop.AddAnimation(_ => throw new InvalidOperationException("animation failed"));
        Assert.Throws<InvalidOperationException>(() => loop.ProcessFrame(TimeSpan.Zero));
        Assert.Equal(1, stops);
    }
}
