using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using static Pickets.ShellInteropConstants;

namespace Pickets;

/// <summary>
/// Hides/restores desktop icons by repositioning them off-screen via IFolderView.
/// The file is never moved on disk -- only its icon's screen position changes.
/// Requires "Auto arrange icons" AND "Align icons to grid" to be OFF.
/// </summary>
public static class DesktopIconHider
{
    // Far enough off-screen that no monitor sees it, mild enough that Windows
    // doesn't apply its "extreme value" safety clamp back into visible bounds.
    private const int OFFSCREEN_X = -2000;
    private const int OFFSCREEN_Y = -2000;
    private const int MAX_PATH = 260;

    public static POINT? Hide(string path)
    {
        Logger.Log($"Hide('{path}') called");
        return WithDesktopView(fv =>
        {
            var pidl = FindItemPidl(fv, path);
            if (pidl == IntPtr.Zero) { Logger.Log("  no matching item in view"); return (POINT?)null; }
            try
            {
                var hrPos = fv.GetItemPosition(pidl, out var current);
                Logger.Log($"  GetItemPosition hr=0x{hrPos:X8}, pos=({current.X},{current.Y})");
                if (hrPos != 0) current = new POINT(0, 0);

                var hrSel = fv.SelectAndPositionItems(1,
                    new[] { pidl },
                    new[] { new POINT(OFFSCREEN_X, OFFSCREEN_Y) },
                    SVSI_POSITIONITEM | SVSI_NOSTATECHANGE);
                Logger.Log($"  SelectAndPositionItems hr=0x{hrSel:X8}");

                // Re-read where it actually ended up (Windows may have clamped/snapped).
                if (fv.GetItemPosition(pidl, out var afterPos) == 0)
                    Logger.Log($"  after-move pos=({afterPos.X},{afterPos.Y})");

                if (current.X < -1500 && current.Y < -1500) return (POINT?)null;
                return (POINT?)current;
            }
            finally { ShellApi.ILFree(pidl); }
        });
    }

    public static bool Restore(string path, POINT originalPos)
    {
        Logger.Log($"Restore('{path}', {originalPos.X},{originalPos.Y})");
        return WithDesktopView(fv =>
        {
            var pidl = FindItemPidl(fv, path);
            if (pidl == IntPtr.Zero) return false;
            try
            {
                var hr = fv.SelectAndPositionItems(1,
                    new[] { pidl },
                    new[] { originalPos },
                    SVSI_POSITIONITEM | SVSI_NOSTATECHANGE);
                Logger.Log($"  SelectAndPositionItems hr=0x{hr:X8}");
                return hr == 0;
            }
            finally { ShellApi.ILFree(pidl); }
        });
    }

    public static bool ReapplyHidden(string path)
    {
        return WithDesktopView(fv =>
        {
            var pidl = FindItemPidl(fv, path);
            if (pidl == IntPtr.Zero) return false;
            try
            {
                var hr = fv.SelectAndPositionItems(1,
                    new[] { pidl },
                    new[] { new POINT(OFFSCREEN_X, OFFSCREEN_Y) },
                    SVSI_POSITIONITEM | SVSI_NOSTATECHANGE);
                return hr == 0;
            }
            finally { ShellApi.ILFree(pidl); }
        });
    }

    /// <summary>Re-hides any of the given desktop items that have drifted back on-screen. Windows
    /// clamps off-screen icons back into the work area whenever Explorer commits a desktop layout
    /// change (e.g. the user drags a different icon), which makes previously-hidden icons reappear.
    /// Enumerates the desktop view once and issues a single batched reposition for ONLY the items
    /// that actually drifted -- so the steady state (nothing drifted) is a cheap read with no
    /// reposition call and therefore no visible repaint. Returns the number of icons re-hidden.</summary>
    public static int ReapplyHiddenBatch(IReadOnlyCollection<string> paths)
    {
        if (paths.Count == 0) return 0;
        return WithDesktopView(fv =>
        {
            if (fv.ItemCount(SVGIO_ALLVIEW, out int count) != 0) return 0;

            var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in paths) targets.Add(NormalizePath(p));

            var drifted = new List<IntPtr>();
            var sb = new StringBuilder(MAX_PATH);
            try
            {
                for (int i = 0; i < count; i++)
                {
                    if (fv.Item(i, out IntPtr childPidl) != 0 || childPidl == IntPtr.Zero) continue;

                    bool keepPidl =
                        ShellApi.SHGetPathFromIDList(childPidl, sb.Clear())
                        && targets.Contains(NormalizePath(sb.ToString()))
                        && fv.GetItemPosition(childPidl, out var pos) == 0
                        && !(pos.X < -1500 && pos.Y < -1500);   // already off-screen -> leave alone

                    if (keepPidl) drifted.Add(childPidl);
                    else          ShellApi.ILFree(childPidl);
                }

                if (drifted.Count == 0) return 0;

                var points = new POINT[drifted.Count];
                for (int i = 0; i < points.Length; i++) points[i] = new POINT(OFFSCREEN_X, OFFSCREEN_Y);
                fv.SelectAndPositionItems((uint)drifted.Count, drifted.ToArray(), points,
                    SVSI_POSITIONITEM | SVSI_NOSTATECHANGE);
                return drifted.Count;
            }
            finally
            {
                foreach (var p in drifted) ShellApi.ILFree(p);
            }
        });
    }

    /// <summary>Enumerates every item on the desktop whose icon origin falls inside the given
    /// screen rectangle. Positions come from IFolderView::GetItemPosition (same source the
    /// Hide/Restore path uses), so they're in the same coordinate space as screenRect.</summary>
    public static System.Collections.Generic.List<(string Path, POINT Pos)> EnumerateItemsInRect(RECT screenRect)
    {
        var found = new System.Collections.Generic.List<(string, POINT)>();
        WithDesktopView<object?>(fv =>
        {
            int hrCount = fv.ItemCount(SVGIO_ALLVIEW, out int count);
            if (hrCount != 0) return null;

            var sb = new StringBuilder(MAX_PATH);
            for (int i = 0; i < count; i++)
            {
                int hrItem = fv.Item(i, out IntPtr childPidl);
                if (hrItem != 0 || childPidl == IntPtr.Zero) continue;
                try
                {
                    if (fv.GetItemPosition(childPidl, out var pos) != 0) continue;
                    if (pos.X < screenRect.left || pos.X > screenRect.right) continue;
                    if (pos.Y < screenRect.top  || pos.Y > screenRect.bottom) continue;

                    sb.Clear();
                    if (!ShellApi.SHGetPathFromIDList(childPidl, sb)) continue;
                    var path = sb.ToString();
                    if (string.IsNullOrEmpty(path)) continue;
                    // Skip already-hidden (off-screen clamped) items from prior sessions.
                    if (pos.X < -1500 && pos.Y < -1500) continue;

                    found.Add((path, pos));
                }
                finally { ShellApi.ILFree(childPidl); }
            }
            return null;
        });
        return found;
    }

    public static bool IsAutoArrangeOn()
    {
        return WithDesktopView(fv =>
        {
            var hr = fv.GetAutoArrange();
            Logger.Log($"GetAutoArrange hr=0x{hr:X8} ({(hr == 0 ? "ON" : "OFF")})");
            return hr == 0;
        });
    }

    /// <summary>Returns whether "Align icons to grid" is on. Logs the full flag bits.</summary>
    public static bool IsSnapToGridOn()
    {
        return WithDesktopView(fv =>
        {
            if (fv is not IFolderView2 fv2)
            {
                Logger.Log("  IFolderView -> IFolderView2 QI failed");
                return false;
            }
            int hr = fv2.GetCurrentFolderFlags(out uint flags);
            Logger.Log($"  GetCurrentFolderFlags hr=0x{hr:X8}, flags=0x{flags:X8} (autoArrange={(flags & FWF_AUTOARRANGE) != 0}, snapToGrid={(flags & FWF_SNAPTOGRID) != 0})");
            return hr == 0 && (flags & FWF_SNAPTOGRID) != 0;
        });
    }

    private static IntPtr FindItemPidl(IFolderView fv, string targetPath)
    {
        int hrCount = fv.ItemCount(SVGIO_ALLVIEW, out int count);
        if (hrCount != 0) { Logger.Log($"  ItemCount hr=0x{hrCount:X8}"); return IntPtr.Zero; }

        var sb = new StringBuilder(MAX_PATH);
        var normalizedTarget = NormalizePath(targetPath);

        for (int i = 0; i < count; i++)
        {
            int hrItem = fv.Item(i, out IntPtr childPidl);
            if (hrItem != 0 || childPidl == IntPtr.Zero) continue;

            sb.Clear();
            bool ok = ShellApi.SHGetPathFromIDList(childPidl, sb);
            if (ok)
            {
                var itemPath = NormalizePath(sb.ToString());
                if (string.Equals(itemPath, normalizedTarget, StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Log($"  matched item[{i}]: {itemPath}");
                    return childPidl;
                }
            }
            ShellApi.ILFree(childPidl);
        }
        return IntPtr.Zero;
    }

    private static string NormalizePath(string p)
    {
        try { return System.IO.Path.GetFullPath(p).TrimEnd('\\'); }
        catch { return p; }
    }

    private static T? WithDesktopView<T>(Func<IFolderView, T> action)
    {
        object? shellWindowsObj = null;
        object? dispatchObj = null;
        IShellBrowser? shellBrowser = null;
        IShellView? shellView = null;

        try
        {
            var swType = Type.GetTypeFromCLSID(CLSID_ShellWindows);
            if (swType == null) return default;
            shellWindowsObj = Activator.CreateInstance(swType);
            if (shellWindowsObj is not IShellWindows sw) return default;

            object loc = (int)CSIDL_DESKTOP;
            object empty = Type.Missing;
            int hwnd;
            int hrFw = sw.FindWindowSW(ref loc, ref empty, SWC_DESKTOP, out hwnd, SWFO_NEEDDISPATCH, out dispatchObj);
            if (hrFw != 0 || dispatchObj == null) return default;

            if (dispatchObj is not IServiceProviderCom sp) return default;

            var sidGuid = SID_STopLevelBrowser;
            var sbGuid = IID_IShellBrowser;
            int hrQs = sp.QueryService(ref sidGuid, ref sbGuid, out var sbPtr);
            if (hrQs != 0 || sbPtr == IntPtr.Zero) return default;

            shellBrowser = (IShellBrowser)Marshal.GetObjectForIUnknown(sbPtr);
            Marshal.Release(sbPtr);

            int hrSv = shellBrowser.QueryActiveShellView(out shellView);
            if (hrSv != 0 || shellView == null) return default;

            if (shellView is not IFolderView folderView) return default;

            return action(folderView);
        }
        catch (Exception ex)
        {
            Logger.Log($"  WithDesktopView threw: {ex.GetType().Name}: {ex.Message}");
            return default;
        }
        finally
        {
            if (shellView != null) Marshal.ReleaseComObject(shellView);
            if (shellBrowser != null) Marshal.ReleaseComObject(shellBrowser);
            if (dispatchObj != null) Marshal.ReleaseComObject(dispatchObj);
            if (shellWindowsObj != null) Marshal.ReleaseComObject(shellWindowsObj);
        }
    }
}
