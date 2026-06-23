using System;
using System.Runtime.InteropServices;

namespace Pickets;

/// <summary>
/// Parents a WPF window to the WorkerW that sits between the wallpaper and the
/// desktop icon layer. Uses the documented Progman 0x052C trick to force Explorer
/// to spawn that WorkerW, then EnumWindows to locate it.
/// </summary>
public static class WorkerWHost
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter,
        string lpszClass, string? lpszWindow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg,
        IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    private const uint WM_SPAWN_WORKERW = 0x052C;
    private const uint SMTO_NORMAL = 0x0000;

    public static bool AttachToDesktop(IntPtr hwnd)
    {
        var progman = FindWindow("Progman", null);
        if (progman == IntPtr.Zero) return false;

        // Tells Explorer to spawn the WorkerW behind the desktop icons (if not already there).
        SendMessageTimeout(progman, WM_SPAWN_WORKERW, IntPtr.Zero, IntPtr.Zero,
            SMTO_NORMAL, 1000, out _);

        IntPtr workerW = IntPtr.Zero;
        EnumWindows((tophandle, _) =>
        {
            var defView = FindWindowEx(tophandle, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (defView != IntPtr.Zero)
            {
                // The WorkerW we want is the next sibling AFTER the one hosting SHELLDLL_DefView.
                workerW = FindWindowEx(IntPtr.Zero, tophandle, "WorkerW", null);
            }
            return true;
        }, IntPtr.Zero);

        if (workerW == IntPtr.Zero) return false;

        SetParent(hwnd, workerW);
        return true;
    }
}
