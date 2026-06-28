using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Pickets;

/// <summary>
/// Raises <see cref="Restarted"/> when Explorer relaunches. Explorer broadcasts the registered
/// "TaskbarCreated" message to every top-level window when its shell comes back up (taskbar/Explorer
/// crash, or a manual restart). Pickets listens so it can re-attach its pickets to the freshly
/// spawned WorkerW -- without this, an Explorer restart orphans the pickets and they stay invisible
/// until the app is manually relaunched.
///
/// A message-only (HWND_MESSAGE) window is deliberately NOT used here: broadcast messages are only
/// delivered to genuine top-level windows, so we create a normal (hidden, never-shown) one.
/// </summary>
public sealed class ExplorerRestartWatcher : NativeWindow, IDisposable
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterWindowMessage(string lpString);

    private readonly int _taskbarCreatedMsg;

    public event Action? Restarted;

    public ExplorerRestartWatcher()
    {
        _taskbarCreatedMsg = RegisterWindowMessage("TaskbarCreated");
        CreateHandle(new CreateParams()); // default params => a top-level, unparented, hidden window
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == _taskbarCreatedMsg && _taskbarCreatedMsg != 0)
            Restarted?.Invoke();
        base.WndProc(ref m);
    }

    public void Dispose() => DestroyHandle();
}
