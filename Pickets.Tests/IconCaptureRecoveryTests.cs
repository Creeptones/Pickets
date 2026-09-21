namespace Pickets.Tests;

public sealed class IconCaptureRecoveryTests
{
    [Fact]
    public void CollectAndRelease_PrefersCurrentProfileAndClearsEveryProfile()
    {
        var layout = new LayoutFile
        {
            Profiles = new()
            {
                ["other"] = [State("C:\\Desktop\\Example.lnk", 900, 800)],
                ["current"] = [State("C:\\Desktop\\Example.lnk", 100, 200)],
            },
            LastProfileSeed = [State("C:\\Desktop\\Example.lnk", 500, 600)],
        };

        var captured = IconCaptureRecovery.Collect(layout, "current");
        IconCaptureRecovery.Release(layout, captured.Select(icon => icon.Path));

        var icon = Assert.Single(captured);
        Assert.Equal(100, icon.OriginalPosition.X);
        Assert.Equal(200, icon.OriginalPosition.Y);
        Assert.All(layout.Profiles.Values.SelectMany(states => states).SelectMany(state => state.Items),
            item => Assert.False(item.HasOriginalPos));
        Assert.All(layout.LastProfileSeed!.SelectMany(state => state.Items),
            item => Assert.False(item.HasOriginalPos));
    }

    [Fact]
    public void CollectAndRelease_DeduplicatesPathsWithoutCaseSensitivity()
    {
        var layout = new LayoutFile
        {
            Profiles = new()
            {
                ["current"] =
                [
                    State("C:\\Desktop\\Example.lnk", 10, 20),
                    State("c:\\desktop\\EXAMPLE.lnk", 30, 40),
                ],
            },
        };

        Assert.Single(IconCaptureRecovery.Collect(layout, "current"));
    }

    [Fact]
    public void Release_ClearsOnlySuccessfullyRestoredPaths()
    {
        var layout = new LayoutFile
        {
            Profiles = new()
            {
                ["current"] =
                [
                    State("C:\\Desktop\\Restored.lnk", 10, 20),
                    State("C:\\Desktop\\Failed.lnk", 30, 40),
                ],
            },
        };

        IconCaptureRecovery.Release(layout, ["C:\\Desktop\\Restored.lnk"]);

        Assert.False(layout.Profiles["current"][0].Items[0].HasOriginalPos);
        Assert.True(layout.Profiles["current"][1].Items[0].HasOriginalPos);
    }

    private static PicketState State(string path, int x, int y) => new()
    {
        Items = [new ItemState { Path = path, OriginalX = x, OriginalY = y }],
    };
}
