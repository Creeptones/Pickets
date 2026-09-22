namespace Pickets.Tests;

public sealed class IconCaptureRecoveryTests
{
    [Theory]
    [InlineData(ReferenceStatus.Missing, true)]
    [InlineData(ReferenceStatus.Unavailable, false)]
    [InlineData(ReferenceStatus.Available, false)]
    [InlineData(ReferenceStatus.Checking, false)]
    public void FailedShellRestore_OnlyConfirmedDeletionCountsAsReleased(ReferenceStatus status, bool expected)
    {
        var icon = new CapturedDesktopIcon(@"C:\Desktop\Example.lnk", new POINT(10, 20));
        Assert.Equal(expected, IconCaptureRecovery.TryRestore(icon, _ => false, _ => status));
    }

    [Fact]
    public void SuccessfulShellRestore_DoesNotWaitForFileAvailability()
    {
        var icon = new CapturedDesktopIcon(@"C:\Desktop\Example.lnk", new POINT(10, 20));
        Assert.True(IconCaptureRecovery.TryRestore(icon, _ => true,
            _ => throw new InvalidOperationException("A successful restore needs no file probe.")));
    }

    [Fact]
    public void ProfileRoundTrip_RestoresIconAbsentFromIncomingProfileAndKeepsOwnership()
    {
        const string path = @"C:\Desktop\Example.lnk";
        var layout = new LayoutFile
        {
            Profiles = new() { ["A"] = [State(path, 100, 200)], ["B"] = [] }
        };
        var visible = false; // Loading A has hidden its captured icon.
        bool Restore(CapturedDesktopIcon icon)
        {
            Assert.Equal(path, icon.Path);
            visible = true;
            return true;
        }

        Assert.True(IconCaptureRecovery.TryRestoreInactive(layout, "A", layout.Profiles["B"], Restore));
        Assert.True(visible);
        Assert.True(layout.Profiles["A"][0].Items[0].HasOriginalPos);
        Assert.True(IconCaptureRecovery.TryRestoreInactive(layout, "B", layout.Profiles["A"], Restore));
        visible = false; // Loading A again re-hides the capture.
        Assert.True(IconCaptureRecovery.TryRestoreInactive(layout, "A", layout.Profiles["B"], Restore));
        Assert.True(visible);

        // Normal Quit in B must also find captures from A and preserve them for the next launch.
        var quitCaptures = IconCaptureRecovery.Collect(layout, "B");
        Assert.Equal(path, Assert.Single(quitCaptures).Path);
        Assert.True(layout.Profiles["A"][0].Items[0].HasOriginalPos);
    }

    [Fact]
    public void ProfileChange_DoesNotRestoreCapturesRetainedByIncomingProfile()
    {
        var layout = new LayoutFile
        {
            Profiles = new() { ["A"] = [State(@"C:\Desktop\Example.lnk", 10, 20)] }
        };
        Assert.True(IconCaptureRecovery.TryRestoreInactive(layout, "A",
            [State(@"c:\desktop\EXAMPLE.lnk", 30, 40)], _ => throw new InvalidOperationException("Retained capture must stay hidden.")));
    }

    [Fact]
    public void ProfileChange_OrdinaryReferenceDoesNotKeepItsDesktopIconHidden()
    {
        var state = State(@"C:\Desktop\Example.lnk", 10, 20);
        var layout = new LayoutFile { Profiles = new() { ["A"] = [state] } };
        var restored = new List<string>();
        Assert.True(IconCaptureRecovery.TryRestoreInactive(layout, "A",
            [new PicketState { Items = [new ItemState { Path = state.Items[0].Path }] }],
            icon => { restored.Add(icon.Path); return true; }));
        Assert.Equal(state.Items[0].Path, Assert.Single(restored));
    }

    [Fact]
    public void ProfileChange_FailedRestoreBlocksTransitionAndPreservesAllRecoveryMetadata()
    {
        var layout = new LayoutFile
        {
            Profiles = new()
            {
                ["A"] = [State(@"C:\Desktop\Failed.lnk", 10, 20)],
                ["old"] = [State(@"C:\Desktop\OlderCapture.lnk", 30, 40)],
                ["B"] = []
            }
        };
        var attempted = new List<string>();
        Assert.False(IconCaptureRecovery.TryRestoreInactive(layout, "A", layout.Profiles["B"],
            icon => { attempted.Add(icon.Path); return !icon.Path.EndsWith("Failed.lnk", StringComparison.Ordinal); }));
        Assert.Equal(2, attempted.Count);
        Assert.Equal(2, IconCaptureRecovery.Collect(layout, "A").Count);
        Assert.True(IconCaptureRecovery.TryRestoreInactive(layout, "A", layout.Profiles["B"], _ => true));
        Assert.Equal(2, IconCaptureRecovery.Collect(layout, "B").Count);
    }

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
