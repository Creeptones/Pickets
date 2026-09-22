using System;

namespace Pickets;

internal readonly record struct StackPage(int Index, int Count, int Capacity, int Start, int Length)
{
    internal bool Contains(int index) => index >= Start && index < Start + Length;
}

internal static class StackViewport
{
    // Reserve enough vertical space for useful scrollable bodies, even if every picket is open.
    internal const double MinimumOpenHeight = 96;

    internal static StackPage Page(int count, double workHeight, int requestedPage, double workWidth, bool horizontal)
    {
        var capacity = Math.Max(1, (int)Math.Floor(horizontal ? workWidth / 160 : workHeight / MinimumOpenHeight));
        var pages = Math.Max(1, (int)Math.Ceiling(count / (double)capacity));
        var page = Math.Clamp(requestedPage, 0, pages - 1);
        var start = page * capacity;
        return new StackPage(page, pages, capacity, start, Math.Max(0, Math.Min(capacity, count - start)));
    }
}
