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
    private const string RestoreEventName = "Pickets.RestoreRequest.v1";

    private readonly Mutex _mutex;
    private EventWaitHandle? _showEvent;
    private EventWaitHandle? _restoreEvent;
    private RegisteredWaitHandle? _showRegistration;
    private RegisteredWaitHandle? _restoreRegistration;

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
    public void ListenForRequests(Action onShowRequested, Action onRestoreRequested)
    {
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _restoreEvent = new EventWaitHandle(false, EventResetMode.AutoReset, RestoreEventName);
        _showRegistration = ThreadPool.RegisterWaitForSingleObject(
            _showEvent,
            (_, _) => onShowRequested(),
            state: null,
            millisecondsTimeOutInterval: Timeout.Infinite,
            executeOnlyOnce: false);
        _restoreRegistration = ThreadPool.RegisterWaitForSingleObject(
            _restoreEvent,
            (_, _) => onRestoreRequested(),
            state: null,
            millisecondsTimeOutInterval: Timeout.Infinite,
            executeOnlyOnce: false);
    }

    /// <summary>Second instance: ping the original to surface itself. Best-effort -- if the event
    /// can't be opened (e.g. the first instance hasn't created it yet), we simply exit quietly.</summary>
    public static void SignalExistingInstance()
        => Signal(ShowEventName);

    public static void SignalRestoreRequest()
        => Signal(RestoreEventName);

    /// <summary>Waits for the owning instance to release the mutex. Used only by the silent
    /// uninstall recovery path so setup cannot remove the executable while recovery is active.</summary>
    public bool WaitForOwnerExit(TimeSpan timeout)
    {
        if (IsFirstInstance) return true;

        var acquired = false;
        try
        {
            acquired = _mutex.WaitOne(timeout);
            return acquired;
        }
        catch (AbandonedMutexException)
        {
            // The prior process ended without releasing normally; this thread now owns the mutex.
            acquired = true;
            return true;
        }
        finally
        {
            if (acquired) _mutex.ReleaseMutex();
        }
    }

    private static void Signal(string eventName)
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(eventName, out var ev))
            {
                ev.Set();
                ev.Dispose();
            }
        }
        catch { /* signalling is a nicety, never fatal */ }
    }

    public void Dispose()
    {
        _showRegistration?.Unregister(waitObject: null);
        _restoreRegistration?.Unregister(waitObject: null);
        _showEvent?.Dispose();
        _restoreEvent?.Dispose();
        try { if (IsFirstInstance) _mutex.ReleaseMutex(); } catch { /* not owned / already released */ }
        _mutex.Dispose();
    }
}
