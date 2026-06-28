using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace Pickets;

/// <summary>
/// Builds a stable key describing the current monitor arrangement (resolution + virtual-screen
/// position of each physical monitor) so picket layouts can be saved per-profile. When the user
/// streams in via Parsec at 1920x1200, the key changes, and a separate picket layout loads --
/// pickets saved at 4K coordinates no longer end up off-screen on the laptop canvas.
/// </summary>
internal static class DisplayProfile
{
    private delegate bool MonitorEnumProc(IntPtr hMon, IntPtr hdc, ref RECT lprc, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfn, IntPtr dwData);

    /// <summary>Returns a deterministic key like "1920x1200+0,0;2560x1440+1920,0" for the current
    /// monitor set. Sorted by (top, left) so reboot/hot-plug reordering doesn't cause false diffs.</summary>
    public static string CurrentKey()
    {
        var monitors = EnumerateWorkAreas();
        if (monitors.Count == 0) return "unknown";

        var parts = monitors
            .Select(m => m.rcMonitor)
            .OrderBy(r => r.top).ThenBy(r => r.left)
            .Select(r => $"{r.right - r.left}x{r.bottom - r.top}+{r.left},{r.top}");
        return string.Join(";", parts);
    }

    /// <summary>Every monitor's full info (rcMonitor + rcWork).</summary>
    public static List<MONITORINFO> EnumerateWorkAreas()
    {
        var list = new List<MONITORINFO>();
        MonitorEnumProc cb = (IntPtr hMon, IntPtr _hdc, ref RECT _r, IntPtr _d) =>
        {
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (WindowInterop.GetMonitorInfo(hMon, ref mi))
                list.Add(mi);
            return true;
        };
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, cb, IntPtr.Zero);
        return list;
    }

    /// <summary>Clamps a picket's position+size so that it lies within some monitor's work area.
    /// If the picket's current monitor has vanished (Parsec dropped a screen, resolution shrank),
    /// the picket is relocated to the primary work area with its size shrunk to fit.</summary>
    public static void ClampToVisibleWorkArea(PicketState s)
    {
        var monitors = EnumerateWorkAreas();
        if (monitors.Count == 0) return;

        // Prefer the monitor whose work area currently contains the picket's top-left. If none does
        // (picket is fully off-screen), pick the primary.
        var primary = monitors.FirstOrDefault(m => (m.dwFlags & 1) != 0); // MONITORINFOF_PRIMARY
        if (primary.cbSize == 0) primary = monitors[0];

        MONITORINFO target = primary;
        foreach (var m in monitors)
        {
            var w = m.rcWork;
            bool contains = s.X >= w.left && s.X < w.right && s.Y >= w.top && s.Y < w.bottom;
            if (contains) { target = m; break; }
        }

        var work = target.rcWork;
        double maxW = work.right - work.left;
        double maxH = work.bottom - work.top;

        // Don't let a saved width/height exceed the target work area. Leave a small margin so
        // resize thumbs remain reachable on tiny screens.
        const double MARGIN = 4;
        s.Width  = Math.Max(120, Math.Min(s.Width,  maxW - MARGIN * 2));
        s.Height = Math.Max(32,  Math.Min(s.Height, maxH - MARGIN * 2));

        // Clamp top-left so the whole picket stays inside the work area.
        double minX = work.left + MARGIN;
        double minY = work.top + MARGIN;
        double maxX = work.right  - s.Width  - MARGIN;
        double maxY = work.bottom - s.Height - MARGIN;
        s.X = Math.Max(minX, Math.Min(s.X, maxX));
        s.Y = Math.Max(minY, Math.Min(s.Y, maxY));
    }
}
