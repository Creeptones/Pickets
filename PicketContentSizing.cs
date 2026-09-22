using System;

namespace Pickets;

internal static class PicketContentSizing
{
    internal const double EmptyHeight = 152;
    internal static double LineHeight(double fontSize) => fontSize * 1.3;
    internal static double CellWidth(bool large, double fontSize)
        => (large ? 128 : 96) * Math.Max(1, fontSize / 12);
}
