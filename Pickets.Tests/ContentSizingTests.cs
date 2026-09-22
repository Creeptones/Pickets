using System.Windows;

namespace Pickets.Tests;

public sealed class ContentSizingTests
{
    [Fact]
    public void ExistingLayoutsUseAutoSizing_AndExplicitManualSizingSurvivesProfileCopies()
    {
        var layout = LayoutStore.ParseWithMigration("""
            {"Version":2,"Profiles":{"a":[{"Id":"manual","AutoSizeRows":false,"Height":417},{"Id":"existing","Height":200}]}}
            """)!;
        var copied = LayoutStore.GetOrSeedProfile(layout, "b");
        Assert.False(copied[0].AutoSizeRows);
        Assert.Equal(417, copied[0].Height);
        Assert.True(copied[1].AutoSizeRows);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void MixedContentHeightsStayAttachedAndFitWorkArea(double scale)
    {
        var bounds = StackLayout.Arrange(new Point(30, 600), 400, 300, [false, false, true, false],
            false, new Rect(0, 0, 1200, 500), scale, scale, [110, 240, 300, 170]);
        Assert.True(bounds[0].Height < bounds[1].Height);
        for (var i = 1; i < bounds.Count; i++) Assert.Equal(bounds[i - 1].Bottom, bounds[i].Top, 6);
        Assert.All(bounds, rect => Assert.True(new Rect(0, 0, 1200, 500).Contains(rect)));
    }
}

