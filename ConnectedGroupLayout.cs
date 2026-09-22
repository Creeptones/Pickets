using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace Pickets;

internal enum GroupOrientation { Column, Row }

internal static class ConnectedGroupLayout
{
    // Connected groups have one axis. Irregular joins become a top-to-bottom stack instead of
    // leaving independently positioned members that overlap or disconnect on resize.
    internal static GroupOrientation Orientation(IReadOnlyCollection<Rect> bounds)
    {
        var commonX = bounds.Max(p => p.Left) < bounds.Min(p => p.Right);
        var commonY = bounds.Max(p => p.Top) < bounds.Min(p => p.Bottom);
        if (commonX && !commonY) return GroupOrientation.Column;
        if (commonY && !commonX) return GroupOrientation.Row;
        if (!commonX && !commonY) return GroupOrientation.Column;
        var xSpread = bounds.Max(p => p.Left + p.Width / 2) - bounds.Min(p => p.Left + p.Width / 2);
        var ySpread = bounds.Max(p => p.Top + p.Height / 2) - bounds.Min(p => p.Top + p.Height / 2);
        return ySpread >= xSpread ? GroupOrientation.Column : GroupOrientation.Row;
    }

    // Inputs are already ordered along the chosen axis. Use rounded sizes as well as origins,
    // so each next edge exactly equals the prior edge at fractional display scaling.
    internal static IReadOnlyList<Rect> Reflow(IReadOnlyList<Rect> ordered, GroupOrientation orientation,
        double scaleX, double scaleY)
    {
        var result = new List<Rect>(ordered.Count);
        if (ordered.Count == 0) return result;
        var left = Math.Round(ordered[0].Left * scaleX) / scaleX;
        var top = Math.Round(ordered[0].Top * scaleY) / scaleY;
        foreach (var bounds in ordered)
        {
            var width = Math.Ceiling(bounds.Width * scaleX) / scaleX;
            var height = Math.Ceiling(bounds.Height * scaleY) / scaleY;
            result.Add(new Rect(left, top, width, height));
            if (orientation == GroupOrientation.Column) top += height;
            else left += width;
        }
        return result;
    }
}
