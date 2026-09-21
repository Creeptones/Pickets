using System;
using System.Collections.Generic;

namespace Pickets;

internal readonly record struct CapturedDesktopIcon(string Path, POINT OriginalPosition);

/// <summary>Pure layout-side recovery logic. Collection order favors the current display profile,
/// then the latest seed, so duplicate paths use the position most likely to match this desktop.</summary>
internal static class IconCaptureRecovery
{
    public static IReadOnlyList<CapturedDesktopIcon> Collect(LayoutFile layout, string preferredProfile)
    {
        var captured = new Dictionary<string, POINT>(StringComparer.OrdinalIgnoreCase);

        void Collect(IEnumerable<PicketState>? states)
        {
            if (states == null) return;
            foreach (var state in states)
            foreach (var item in state.Items)
            {
                if (!item.HasOriginalPos || string.IsNullOrWhiteSpace(item.Path) ||
                    captured.ContainsKey(item.Path)) continue;
                captured[item.Path] = new POINT(item.OriginalX!.Value, item.OriginalY!.Value);
            }
        }

        if (layout.Profiles.TryGetValue(preferredProfile, out var preferred)) Collect(preferred);
        Collect(layout.LastProfileSeed);
        foreach (var pair in layout.Profiles)
            if (!pair.Key.Equals(preferredProfile, StringComparison.Ordinal)) Collect(pair.Value);

        var result = new List<CapturedDesktopIcon>(captured.Count);
        foreach (var pair in captured) result.Add(new CapturedDesktopIcon(pair.Key, pair.Value));
        return result;
    }

    public static void Release(LayoutFile layout, IEnumerable<string> restoredPaths)
    {
        var restored = new HashSet<string>(restoredPaths, StringComparer.OrdinalIgnoreCase);

        void ReleaseStates(IEnumerable<PicketState>? states)
        {
            if (states == null) return;
            foreach (var state in states)
            foreach (var item in state.Items)
            {
                if (!restored.Contains(item.Path)) continue;
                item.OriginalX = null;
                item.OriginalY = null;
            }
        }

        foreach (var states in layout.Profiles.Values) ReleaseStates(states);
        ReleaseStates(layout.LastProfileSeed);
    }
}
