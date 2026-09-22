using System.Text.Json;
using System.Windows;

namespace Pickets.Tests;

public sealed class InteractionTests
{
    [Fact]
    public void Accordion_OpeningClosesOthers_ButOrdinaryRollupDoesNot()
    {
        Assert.Equal(new[] { true, false, true }, StackLayout.CollapseStates([false, true, false], 1, false, true));
        Assert.Equal(new[] { false, false, false }, StackLayout.CollapseStates([false, true, false], 1, false, false));
        Assert.Equal(new[] { true, true, true }, StackLayout.CollapseStates([true, false, true], 1, true, true));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void Stack_FitsWorkAreaAndKeepsEverySeamFlush(double scale)
    {
        var work = new Rect(-1000, 24, 900, 620);
        var result = StackLayout.Arrange(new Point(-120, 570), 340.3, 450, [false, true, false, true], false, work, scale, scale);
        Assert.All(result, r => Assert.True(work.Contains(r)));
        Assert.All(result, r => Assert.Equal(result[0].Width, r.Width));
        Assert.Equal(result[0].Height, result[2].Height);
        for (var i = 1; i < result.Count; i++) Assert.Equal(result[i - 1].Bottom, result[i].Top, 8);
    }

    [Fact]
    public void Accordion_SwitchDoesNotChangeOverallFootprint()
    {
        var work = new Rect(0, 0, 1280, 680);
        var a = StackLayout.Arrange(new Point(40, 500), 320, 600, [false, true, true], false, work);
        var b = StackLayout.Arrange(new Point(a[0].X, a[0].Y), 320, 600, [true, true, false], false, work);
        Assert.Equal(a[0].Top, b[0].Top);
        Assert.Equal(a[^1].Bottom, b[^1].Bottom);
        Assert.Equal(a[0].Height, b[2].Height);
    }

    [Fact]
    public void Row_UsesSameWidthAndFitsSmallerDisplay()
    {
        var work = new Rect(0, 0, 800, 600);
        var result = StackLayout.Arrange(new Point(700, 500), 400, 250, [false, true, false], true, work, 1.5, 1.5);
        Assert.All(result, r => Assert.True(work.Contains(r)));
        Assert.All(result, r => Assert.Equal(result[0].Width, r.Width));
        Assert.Equal(result[0].Right, result[1].Left, 8);
        Assert.Equal(result[1].Right, result[2].Left, 8);
    }

    [Fact]
    public void SavedStacksAndReferences_RoundTripAndSeedNewProfiles()
    {
        var state = new PicketState
        {
            GroupId = "stack", GroupOrder = 2, AccordionMode = true, IsCollapsed = true, Height = 400,
            Items = [new ItemState { Path = @"Z:\Disconnected\Music", IsFolder = true }]
        };
        var layout = new LayoutFile { FocusShortcut = "Ctrl+Alt+P", LastProfileSeed = [state], Profiles = new() { ["first"] = [state] } };
        var restored = LayoutStore.ParseWithMigration(JsonSerializer.Serialize(layout))!;
        var copy = Assert.Single(LayoutStore.GetOrSeedProfile(restored, "second"));
        Assert.Equal("stack", copy.GroupId);
        Assert.Equal(2, copy.GroupOrder);
        Assert.True(copy.AccordionMode);
        Assert.True(copy.IsCollapsed);
        Assert.NotEqual(state.Id, copy.Id);
        Assert.Equal(400, copy.Height);
        Assert.Equal("Ctrl+Alt+P", restored.FocusShortcut);
        Assert.True(Assert.Single(copy.Items).IsFolder);
        Assert.Equal(state.Items[0].Path, copy.Items[0].Path);
    }

    [Fact]
    public void OlderLayout_LeavesGroupsMarkedForOneTimeMigration()
    {
        var layout = LayoutStore.ParseWithMigration("""{"Version":2,"Profiles":{"test":[{"Title":"Old","Items":[]}]}}""")!;
        Assert.Null(Assert.Single(layout.Profiles["test"]).GroupId);
        Assert.Equal("Ctrl+Alt+D", layout.FocusShortcut);
    }

    [Theory]
    [InlineData("Ctrl+Alt+D", true)]
    [InlineData("Ctrl+Shift+P", true)]
    [InlineData("Alt+F9", true)]
    [InlineData("None", true)]
    [InlineData("D", false)]
    [InlineData("Ctrl+Alt+Delete", false)]
    [InlineData("Ctrl+Alt+Left", false)]
    [InlineData("Win+D", false)]
    [InlineData("nonsense", false)]
    public void Shortcut_ValidatesWithoutRegisteringGlobalKeys(string text, bool valid)
        => Assert.Equal(valid, FocusShortcut.TryParse(text, out _));

    [Fact]
    public async Task ReferenceProbe_PreservesPathsAcrossUnavailableAndRecoveredStates()
    {
        var directory = Directory.CreateTempSubdirectory("PicketsReferences-");
        try
        {
            var path = Path.Combine(directory.FullName, "available.txt");
            Assert.Equal(ReferenceStatus.Missing, (await FileReferenceProbe.CheckAsync(path)).Status);
            await File.WriteAllTextAsync(path, "original content");
            Assert.Equal(ReferenceStatus.Available, (await FileReferenceProbe.CheckAsync(path)).Status);
            Assert.True((await FileReferenceProbe.CheckAsync(directory.FullName)).IsFolder);
            Assert.Equal("original content", await File.ReadAllTextAsync(path));
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public void InvalidReference_IsUnavailableNotDeleted()
    {
        var item = PicketItem.FromPath("?:invalid");
        Assert.Equal(ReferenceStatus.Unavailable, FileReferenceProbe.Check(item.Path).Status);
        Assert.Equal("?:invalid", item.Path);
    }
}
