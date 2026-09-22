using System;
using System.Collections.Generic;
using System.Linq;

namespace Pickets;

internal readonly record struct CapturedDesktopIcon(string Path, POINT OriginalPosition);

/// <summary>Pure layout-side recovery logic. Collection order favors the current display profile,
/// then the latest seed, so duplicate paths use the position most likely to match this desktop.</summary>
internal static class IconCaptureRecovery
{
    internal static bool TryRestore(CapturedDesktopIcon icon,
        Func<CapturedDesktopIcon, bool> restore, Func<string, ReferenceStatus> check)
        // Offline or inaccessible paths are not proof that the captured item was deleted.
        => restore(icon) || check(icon.Path) == ReferenceStatus.Missing;

    // Keep ownership metadata intact: returning to an older profile must still collect its icons.
    // The caller must keep the current windows when restoration fails so recovery stays reachable.
    internal static bool TryRestoreInactive(LayoutFile layout, string currentProfile,
        IEnumerable<PicketState> incoming, Func<CapturedDesktopIcon, bool> restore)
    {
        var retained = incoming.SelectMany(state => state.Items)
            .Where(item => item.HasOriginalPos)
            .Select(item => item.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var succeeded = true;
        foreach (var icon in Collect(layout, currentProfile))
            if (!retained.Contains(icon.Path) && !restore(icon)) succeeded = false;
        return succeeded;
    }

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
