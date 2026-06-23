using System;
using System.Threading;

namespace Pickets;

/// <summary>
/// Enforces a single running instance via a named system mutex, and gives a would-be second
/// instance a way to ask the original to surface itself (via a named auto-reset event) instead of
/// starting up a duplicate. Critically, the duplicate exits BEFORE any desktop manipulation runs,
/// which is what previously yanked the live desktop icons off-screen.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    // Versioned names so a future incompatible change can coexist with an old build mid-upgrade.
    private const string MutexName     = "Pickets.SingleInstance.v1";
    private const string ShowEventName = "Pickets.ShowRequest.v1";

    private readonly Mutex _mutex;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _registration;

    /// <summary>True if this process is the first/owning instance. False means another is running.</summary>
    public bool IsFirstInstance { get; }

    public SingleInstance()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        IsFirstInstance = createdNew;
    }

    /// <summary>First instance only: start listening for "surface yourself" pings from later launches.
    /// The callback fires on a thread-pool thread, so the supplied action must marshal to the UI
    /// thread itself.</summary>
    public void ListenForShowRequests(Action onShowRequested)
    {
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _registration = ThreadPool.RegisterWaitForSingleObject(
            _showEvent,
            (_, _) => onShowRequested(),
            state: null,
            millisecondsTimeOutInterval: Timeout.Infinite,
            executeOnlyOnce: false);
    }

    /// <summary>Second instance: ping the original to surface itself. Best-effort -- if the event
    /// can't be opened (e.g. the first instance hasn't created it yet), we simply exit quietly.</summary>
    public static void SignalExistingInstance()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(ShowEventName, out var ev))
            {
                ev.Set();
                ev.Dispose();
            }
        }
        catch { /* signalling is a nicety, never fatal */ }
    }

    public void Dispose()
    {
        _registration?.Unregister(waitObject: null);
        _showEvent?.Dispose();
        try { if (IsFirstInstance) _mutex.ReleaseMutex(); } catch { /* not owned / already released */ }
        _mutex.Dispose();
    }
}
