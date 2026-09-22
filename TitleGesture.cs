using System;
using System.Windows;

namespace Pickets;

/// <summary>Tracks a title press in screen pixels, unaffected by a moving/animating window.</summary>
internal sealed class TitleGesture
{
    private Point? _start;
    private Size _tolerance;
    internal bool IsPending => _start.HasValue;

    internal void Begin(Point screenPoint, DpiScale dpi, double horizontalTolerance, double verticalTolerance)
    {
        _start = screenPoint;
        _tolerance = new Size(horizontalTolerance * dpi.DpiScaleX, verticalTolerance * dpi.DpiScaleY);
    }

    internal bool Move(Point screenPoint)
    {
        if (_start is not Point start) return false;
        if (Math.Abs(screenPoint.X - start.X) < _tolerance.Width &&
            Math.Abs(screenPoint.Y - start.Y) < _tolerance.Height) return false;
        Cancel();
        return true;
    }

    internal bool Release(Point screenPoint)
    {
        var click = IsPending && !Move(screenPoint);
        Cancel();
        return click;
    }

    internal void Cancel() => _start = null;
}
