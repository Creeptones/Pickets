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
    private const string PrepareRecoveryEventName = "Pickets.PrepareRecovery.v1";

    private readonly Mutex _mutex;
    private EventWaitHandle? _showEvent;
    private EventWaitHandle? _restoreEvent;
    private RegisteredWaitHandle? _showRegistration;
    private RegisteredWaitHandle? _restoreRegistration;
    private EventWaitHandle? _prepareRecoveryEvent;
    private RegisteredWaitHandle? _prepareRecoveryRegistration;
    private bool _ownsMutex;

    /// <summary>True if this process is the first/owning instance. False means another is running.</summary>
    public bool IsFirstInstance { get; }

    public SingleInstance() : this(MutexName) { }

    internal SingleInstance(string mutexName)
    {
        _mutex = new Mutex(initiallyOwned: true, mutexName, out bool createdNew);
        IsFirstInstance = createdNew;
        _ownsMutex = createdNew;
    }

    /// <summary>First instance only: start listening for "surface yourself" pings from later launches.
    /// The callback fires on a thread-pool thread, so the supplied action must marshal to the UI
    /// thread itself.</summary>
    public void ListenForRequests(Action onShowRequested, Action onRestoreRequested, Action onPrepareRecovery)
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
        _prepareRecoveryEvent = new EventWaitHandle(false, EventResetMode.AutoReset, PrepareRecoveryEventName);
        _prepareRecoveryRegistration = ThreadPool.RegisterWaitForSingleObject(
            _prepareRecoveryEvent, (_, _) => onPrepareRecovery(), null, Timeout.Infinite, false);
    }

    /// <summary>Second instance: ping the original to surface itself. Best-effort -- if the event
    /// can't be opened (e.g. the first instance hasn't created it yet), we simply exit quietly.</summary>
    public static void SignalExistingInstance()
        => Signal(ShowEventName);

    public static void SignalRestoreRequest()
        => Signal(RestoreEventName);

    public static void SignalPrepareRecovery() => Signal(PrepareRecoveryEventName);

    /// <summary>Waits for the owning instance to release the mutex. Used only by the silent
    /// uninstall recovery path so setup cannot remove the executable while recovery is active.</summary>
    public bool WaitForOwnerExit(TimeSpan timeout)
    {
        if (IsFirstInstance) return true;

        try
        {
            _ownsMutex = _mutex.WaitOne(timeout);
            return _ownsMutex;
        }
        catch (AbandonedMutexException)
        {
            // The prior process ended without releasing normally; this thread now owns the mutex.
            _ownsMutex = true;
            return true;
        }
        // Keep ownership until Dispose: no other launch may re-hide icons during recovery.
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
        _prepareRecoveryRegistration?.Unregister(waitObject: null);
        _showEvent?.Dispose();
        _restoreEvent?.Dispose();
        _prepareRecoveryEvent?.Dispose();
        try { if (_ownsMutex) _mutex.ReleaseMutex(); } catch { /* not owned / already released */ }
        _mutex.Dispose();
    }
}
