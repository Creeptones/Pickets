using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace Pickets;

internal static class StackLayout
{
    internal const double TitleHeight = 32;

    internal static bool[] CollapseStates(IReadOnlyList<bool> current, int clicked, bool collapse, bool accordion)
    {
        var result = current.ToArray();
        for (var i = 0; i < result.Length; i++)
            if (i == clicked) result[i] = collapse;
            else if (accordion && !collapse) result[i] = true;
        return result;
    }

    internal static IReadOnlyList<Rect> Arrange(Point origin, double width, double expandedHeight,
        IReadOnlyList<bool> collapsed, bool horizontal, Rect work, double scaleX = 1, double scaleY = 1)
    {
        if (collapsed.Count == 0) return Array.Empty<Rect>();
        width = Math.Max(1, Math.Min(width, horizontal ? work.Width / collapsed.Count : work.Width));
        width = Math.Floor(width * scaleX) / scaleX;
        var title = Math.Ceiling(TitleHeight * scaleY) / scaleY;
        var openCount = collapsed.Count(c => !c);
        var availableHeight = horizontal ? work.Height
            : openCount == 0 ? title : (work.Height - (collapsed.Count - openCount) * title) / openCount;
        var height = Math.Max(title, Math.Min(expandedHeight, availableHeight));
        height = Math.Floor(height * scaleY) / scaleY;
        var heights = collapsed.Select(c => c ? title : height).ToArray();
        var totalWidth = horizontal ? width * collapsed.Count : width;
        var totalHeight = horizontal ? heights.Max() : heights.Sum();
        var x = Math.Clamp(origin.X, work.Left, Math.Max(work.Left, work.Right - totalWidth));
        var y = Math.Clamp(origin.Y, work.Top, Math.Max(work.Top, work.Bottom - totalHeight));
        x = Math.Round(x * scaleX) / scaleX;
        y = Math.Round(y * scaleY) / scaleY;
        var result = new List<Rect>();
        foreach (var itemHeight in heights)
        {
            result.Add(new Rect(x, y, width, itemHeight));
            if (horizontal) x += width;
            else y += itemHeight;
        }
        return result;
    }
}
