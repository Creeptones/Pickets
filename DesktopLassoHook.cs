using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Pickets;

/// <summary>
/// Shift+right-click-drag on the bare desktop to sweep out a rectangle and create a
/// new fence around enclosed icons. Shift is required in v1 so plain right-click still
/// yields Windows' desktop context menu -- we only hijack the RMB gesture once the user
/// opts in via the Shift modifier.
///
/// The hook installs globally (WH_MOUSE_LL) and lives on whatever thread constructed it
/// (the WPF UI thread in our case). Events fire synchronously from the hook callback, so
/// consumers should do minimal work and marshal anything expensive off the hook thread.
/// </summary>
public sealed class DesktopLassoHook : IDisposable
{
    private const int DRAG_THRESHOLD = 4; // px before Armed -> Dragging

    private enum State { Idle, Armed, Dragging }

    private readonly LowLevelMouseProc _proc;
    private IntPtr _hookHandle;

    private State _state = State.Idle;
    private POINT _startPt;
    private POINT _currentPt;

    /// <summary>Raised on first move past the drag threshold. Args: start screen point.</summary>
    public event Action<POINT>? DragStarted;
    /// <summary>Raised on every mouse move during a drag. Args: current rect in screen coords (left, top, right, bottom normalized).</summary>
    public event Action<RECT>? DragUpdated;
    /// <summary>Raised on right-button-up while dragging. Args: final rect in screen coords.</summary>
    public event Action<RECT>? DragEnded;
    /// <summary>Raised when drag is cancelled (e.g. right-up without passing threshold).</summary>
    public event Action? DragCancelled;

    public DesktopLassoHook()
    {
        _proc = MouseHookCallback;
        var hMod = WindowInterop.GetModuleHandle(null);
        _hookHandle = WindowInterop.SetWindowsHookEx(WindowInterop.WH_MOUSE_LL, _proc, hMod, 0);
    }

    public bool Installed => _hookHandle != IntPtr.Zero;

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0) return WindowInterop.CallNextHookEx(_hookHandle, nCode, wParam, lParam);

        var msg = wParam.ToInt32();
        var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

        switch (msg)
        {
            case WindowInterop.WM_RBUTTONDOWN:
                if (_state == State.Idle && IsShiftDown() && IsDesktopAtPoint(data.pt))
                {
                    _state = State.Armed;
                    _startPt = data.pt;
                    _currentPt = data.pt;
                    // Swallow the down so the desktop listview doesn't start its own selection / kick off context tracking.
                    return (IntPtr)1;
                }
                break;

            case WindowInterop.WM_MOUSEMOVE:
                if (_state == State.Armed)
                {
                    if (Math.Abs(data.pt.X - _startPt.X) >= DRAG_THRESHOLD ||
                        Math.Abs(data.pt.Y - _startPt.Y) >= DRAG_THRESHOLD)
                    {
                        _state = State.Dragging;
                        _currentPt = data.pt;
                        DragStarted?.Invoke(_startPt);
                        DragUpdated?.Invoke(MakeRect(_startPt, _currentPt));
                        return (IntPtr)1;
                    }
                }
                else if (_state == State.Dragging)
                {
                    _currentPt = data.pt;
                    DragUpdated?.Invoke(MakeRect(_startPt, _currentPt));
                    return (IntPtr)1;
                }
                break;

            case WindowInterop.WM_RBUTTONUP:
                if (_state == State.Dragging)
                {
                    var finalRect = MakeRect(_startPt, data.pt);
                    _state = State.Idle;
                    DragEnded?.Invoke(finalRect);
                    return (IntPtr)1; // swallow to suppress desktop context menu
                }
                else if (_state == State.Armed)
                {
                    // Armed but never dragged: user did a Shift+right-click without moving. Let the event through
                    // so Windows can still show the context menu if it wants to, and clear state.
                    _state = State.Idle;
                    DragCancelled?.Invoke();
                }
                break;
        }

        return WindowInterop.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private static bool IsShiftDown()
        => (WindowInterop.GetAsyncKeyState(WindowInterop.VK_SHIFT) & 0x8000) != 0;

    private static bool IsDesktopAtPoint(POINT pt)
    {
        var hwnd = WindowInterop.WindowFromPoint(pt);
        if (hwnd == IntPtr.Zero) return false;
        var sb = new StringBuilder(64);
        WindowInterop.GetClassName(hwnd, sb, sb.Capacity);
        var cls = sb.ToString();
        return cls is "SysListView32" or "SHELLDLL_DefView" or "Progman" or "WorkerW";
    }

    private static RECT MakeRect(POINT a, POINT b) => new()
    {
        left   = Math.Min(a.X, b.X),
        top    = Math.Min(a.Y, b.Y),
        right  = Math.Max(a.X, b.X),
        bottom = Math.Max(a.Y, b.Y),
    };

    public void Dispose()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            WindowInterop.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }
}
