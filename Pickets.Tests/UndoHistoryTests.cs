namespace Pickets.Tests;

public sealed class UndoHistoryTests
{
    [Fact]
    public void FailedUndoCanBeRetried_AndDoesNotLoseEarlierActions()
    {
        var history = new UndoHistory();
        var allow = false;
        var value = 2;
        history.Record("First", () => { value--; return true; });
        history.Record("Second", () => { if (!allow) return false; value--; return true; });
        Assert.False(history.Undo());
        Assert.Equal("Second", history.Description);
        Assert.Equal(2, value);
        allow = true;
        Assert.True(history.Undo());
        Assert.Equal("First", history.Description);
        Assert.True(history.Undo());
        Assert.Equal(0, value);
        Assert.False(history.Undo());
    }

    [Fact]
    public void UndoDoesNotRecordItsOwnMutations_AndClearingInvalidatesHistory()
    {
        var history = new UndoHistory();
        history.Record("Action", () => { history.Record("Mutation", () => true); return true; });
        Assert.True(history.Undo());
        Assert.Null(history.Description);
        history.Record("Other profile", () => throw new InvalidOperationException());
        history.Clear();
        Assert.False(history.Undo());
    }

    [Fact]
    public void HistoryHasABoundedSessionLifetime()
    {
        var history = new UndoHistory();
        var calls = 0;
        for (var i = 0; i < 40; i++) history.Record("Action", () => { calls++; return true; });
        while (history.Undo()) { }
        Assert.Equal(30, calls);
    }
}
