using System.Windows;
using System.Windows.Media.Imaging;

namespace Pickets.Tests;

public sealed class ReleasePolishTests
{
    [Theory]
    [InlineData(20, 600, 1, false)]
    [InlineData(20, 300, 2, false)]
    [InlineData(100, 720, 1.25, false)]
    [InlineData(20, 600, 1.5, true)]
    [InlineData(1, 96, 1, false)]
    public void EveryPageFitsAndEveryPicketRemainsReachable(int count, double height, double scale, bool horizontal)
    {
        var work = new Rect(0, 0, 800, height);
        var visited = new List<int>();
        var first = StackViewport.Page(count, height, 0, work.Width, horizontal);
        for (var pageIndex = 0; pageIndex < first.Count; pageIndex++)
        {
            var page = StackViewport.Page(count, height, pageIndex, work.Width, horizontal);
            visited.AddRange(Enumerable.Range(page.Start, page.Length));
            foreach (var collapsed in new[] { false, true })
            {
                var bounds = StackLayout.Arrange(new Point(750, height - 5), 320, 320,
                    Enumerable.Repeat(collapsed, page.Length).ToArray(), horizontal, work, scale, scale);
                Assert.All(bounds, r => Assert.True(work.Contains(r), r.ToString()));
                Assert.All(bounds, r => Assert.True(r.Height >= StackLayout.TitleHeight));
                Assert.All(bounds, r => Assert.True(r.Width >= 160));
                for (var i = 1; i < bounds.Count; i++)
                    Assert.Equal(horizontal ? bounds[i - 1].Right : bounds[i - 1].Bottom,
                        horizontal ? bounds[i].Left : bounds[i].Top, 6);
            }
        }
        Assert.Equal(Enumerable.Range(0, count), visited);
    }

    [Fact]
    public void PageIndexClampsAfterMonitorOrMembershipChange()
    {
        var page = StackViewport.Page(3, 600, 99, 800, false);
        Assert.Equal(0, page.Index);
        Assert.Equal(3, page.Length);
        Assert.True(page.Contains(2));
        Assert.False(page.Contains(3));
    }

    [Fact]
    public async Task AvailabilityAndLaunchingDoNotWaitForThumbnails()
    {
        var thumbnail = new TaskCompletionSource<BitmapSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requested = false;
        var item = new PicketItem
        {
            Path = @"C:\Test\reference.txt", DisplayName = "reference.txt",
            Probe = _ => Task.FromResult(new ReferenceCheck(ReferenceStatus.Available, false)),
            ThumbnailLoader = _ => { requested = true; return thumbnail.Task; }
        };
        await item.RefreshAsync().WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(requested);
        Assert.False(thumbnail.Task.IsCompleted);
        Assert.Equal(ReferenceStatus.Available, item.Status);
        Assert.Null(item.Icon);
        thumbnail.SetResult(null);
    }

    [Fact]
    public void UnavailableReferenceHasShortStatusAndActionableHelp()
    {
        var item = PicketItem.FromPath(@"Z:\Music\Mix.wav");
        item.IsMissing = true;
        Assert.Equal("Unavailable", item.StatusText);
        Assert.Contains("Check again", item.ReferenceHelp);
        Assert.Contains(item.Path, item.ReferenceHelp);
        Assert.Contains("Unavailable", item.AccessibleName);
    }
}

