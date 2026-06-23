using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Pickets;

/// <summary>
/// Installs a low-level mouse hook to detect a left double-click on the bare desktop and run an action.
///
/// Caveat: WindowFromPoint can't tell us whether the user double-clicked a desktop *icon* or empty
/// space (icons aren't separate hwnds -- they live inside SysListView32). Cross-process hit-testing
/// would require remote LVHITTESTINFO marshalling, which is overkill for this. So a double-click on
/// an icon will both launch the icon AND toggle the fences -- noisy but harmless. Users who want
/// precision can use the Ctrl+Alt+D hotkey instead.
/// </summary>
public sealed class DesktopDoubleClickHook : IDisposable
{
    private readonly Action _action;
    private readonly LowLevelMouseProc _proc;     // store delegate to keep GC from collecting it
    private IntPtr _hookHandle;

    private readonly uint _doubleClickTime;
    private readonly int  _doubleClickX, _doubleClickY;

    private uint  _lastClickTime;
    private POINT _lastClickPos;

    public DesktopDoubleClickHook(Action action)
    {
        _action = action;
        _proc   = MouseHookCallback;

        _doubleClickTime = WindowInterop.GetDoubleClickTime();
        _doubleClickX    = WindowInterop.GetSystemMetrics(WindowInterop.SM_CXDOUBLECLK);
        _doubleClickY    = WindowInterop.GetSystemMetrics(WindowInterop.SM_CYDOUBLECLK);

        // WH_MOUSE_LL needs an hMod, but for managed code we pass the module handle of the .NET host.
        var hMod = WindowInterop.GetModuleHandle(null);
        _hookHandle = WindowInterop.SetWindowsHookEx(WindowInterop.WH_MOUSE_LL, _proc, hMod, 0);
    }

    public bool Installed => _hookHandle != IntPtr.Zero;

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam.ToInt32() == WindowInterop.WM_LBUTTONDOWN)
        {
            var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

            uint dt = data.time - _lastClickTime;
            int  dx = Math.Abs(data.pt.X - _lastClickPos.X);
            int  dy = Math.Abs(data.pt.Y - _lastClickPos.Y);

            bool isDoubleClick = dt <= _doubleClickTime && dx <= _doubleClickX && dy <= _doubleClickY;

            if (isDoubleClick && IsDesktopAtPoint(data.pt))
            {
                _action();
                // Reset so a third click doesn't form another "double-click" with this one.
                _lastClickTime = 0;
            }
            else
            {
                _lastClickTime = data.time;
                _lastClickPos  = data.pt;
            }
        }
        return WindowInterop.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private static bool IsDesktopAtPoint(POINT pt)
    {
        var hwnd = WindowInterop.WindowFromPoint(pt);
        if (hwnd == IntPtr.Zero) return false;
        var sb = new StringBuilder(64);
        WindowInterop.GetClassName(hwnd, sb, sb.Capacity);
        var cls = sb.ToString();
        // Class hierarchy on Win10/11 desktop: Progman -> SHELLDLL_DefView -> SysListView32
        // (or WorkerW when wallpaper engines/slideshow are active).
        return cls is "SysListView32" or "SHELLDLL_DefView" or "Progman" or "WorkerW";
    }

    public void Dispose()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            WindowInterop.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }
}
