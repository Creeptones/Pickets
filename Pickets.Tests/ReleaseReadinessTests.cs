using System.Windows;

namespace Pickets.Tests;

public sealed class ReleaseReadinessTests
{
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(null, false, false)]
    [InlineData(false, null, false)]
    [InlineData(null, null, false)]
    public void Readiness_RequiresTwoConfirmedDisabledSettings(bool? arrange, bool? grid, bool ready)
        => Assert.Equal(ready, new DesktopReadiness(arrange, grid).IsReady);

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(null, false)]
    public void Onboarding_PreservesInstalledShortcutPreference(int? preference, bool expected)
        => Assert.Equal(expected, OnboardingPreferences.ResolveShortcutDefault(false,
            @"C:\Users\Example\Apps\Pickets\Pickets.exe", @"c:\users\example\apps\pickets\", preference));

    [Fact]
    public void Onboarding_PortableCopyDoesNotInheritAnotherInstallPreference()
        => Assert.True(OnboardingPreferences.ResolveShortcutDefault(false,
            @"D:\Portable\Pickets.exe", @"C:\Installed\Pickets", 0));

    [Fact]
    public void Onboarding_DoesNotRecreateExistingShortcut()
        => Assert.False(OnboardingPreferences.ResolveShortcutDefault(true,
            @"D:\Portable\Pickets.exe", null, null));

    [Theory]
    [InlineData(3, 3, true, 0)]
    [InlineData(2, 3, true, 1)]
    [InlineData(3, 3, false, 4)]
    [InlineData(0, 0, true, 0)]
    public void Recovery_SuccessRequiresEveryIconAndDurableSave(int restored, int captured, bool saved, int code)
        => Assert.Equal(code, (int)RecoveryOutcome.From(restored, captured, saved));

    [Theory]
    [InlineData(1920, 1040, 2)]
    [InlineData(1366, 728, 1.5)]
    [InlineData(1280, 680, 2)]
    public void Welcome_AlwaysFitsTheWorkArea(double width, double height, double scale)
    {
        var available = WelcomeSizing.Available(width, height, scale, scale);
        Assert.True(available.Width * scale < width);
        Assert.True(available.Height * scale < height);
        Assert.True(available.Height < 610);
    }

    [Fact]
    public void IrregularGroup_BecomesOneFlushColumn()
    {
        Rect[] lShape = [new(0, 0, 240, 180), new(240, 0, 240, 180), new(0, 180, 240, 180)];
        var orientation = ConnectedGroupLayout.Orientation(lShape);
        Assert.Equal(GroupOrientation.Column, orientation);
        var result = ConnectedGroupLayout.Reflow(lShape, orientation, 1, 1);
        Assert.Equal(new Rect(0, 180, 240, 180), result[1]);
        Assert.Equal(new Rect(0, 360, 240, 180), result[2]);
    }

    [Fact]
    public void GridGroup_BecomesOneFlushColumn()
    {
        Rect[] grid = [new(0, 0, 240, 180), new(240, 0, 240, 180),
            new(0, 180, 240, 180), new(240, 180, 240, 180)];
        Assert.Equal(GroupOrientation.Column, ConnectedGroupLayout.Orientation(grid));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void GroupReflow_KeepsCollapsedMembersFlushAtEveryScale(double scale)
    {
        Rect[] column = [new(0.3, 1.1, 240.3, 180.3), new(0, 200, 240.3, 32), new(0, 240, 240.3, 180.3)];
        var result = ConnectedGroupLayout.Reflow(column, GroupOrientation.Column, scale, scale);
        Assert.Equal(result[0].Bottom, result[1].Top, 8);
        Assert.Equal(result[1].Bottom, result[2].Top, 8);
        Assert.All(result, bounds => Assert.Equal(result[0].Width, bounds.Width));
        Assert.Equal(result[0].Height, result[2].Height);
    }

    [Fact]
    public void RowGroup_KeepsItsOrientationAfterResize()
    {
        Rect[] row = [new(10, 20, 300, 200), new(310, 20, 300, 200), new(610, 20, 300, 200)];
        Assert.Equal(GroupOrientation.Row, ConnectedGroupLayout.Orientation(row));
        var result = ConnectedGroupLayout.Reflow(row, GroupOrientation.Row, 1.5, 1.5);
        Assert.Equal(result[0].Right, result[1].Left, 8);
        Assert.Equal(result[1].Right, result[2].Left, 8);
    }

    [Fact]
    public void Recovery_DoesNotTreatCorruptLayoutsAsEmpty()
    {
        var directory = Directory.CreateTempSubdirectory("PicketsRecoveryTests-");
        try
        {
            var primary = Path.Combine(directory.FullName, "layout.json");
            var backup = primary + ".bak";
            File.WriteAllText(primary, "{broken");
            File.WriteAllText(backup, "{also broken");
            Assert.Null(LayoutStore.LoadForRecovery(primary, backup));
            File.WriteAllText(backup, "{\"Version\":2,\"Profiles\":{}}");
            Assert.NotNull(LayoutStore.LoadForRecovery(primary, backup));
        }
        finally { directory.Delete(recursive: true); }
    }
}
