using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Pickets;

public partial class App : Application
{
    private readonly List<FenceWindow> _fences = new();
    private DispatcherTimer? _saveDebounce;
    private bool _stateDirty;
    private GlobalHotKey? _quickHide;
    private DesktopDoubleClickHook? _desktopHook;
    private DesktopLassoHook? _lassoHook;
    private LassoOverlay? _lassoOverlay;

    // Full in-memory layout (all profiles). We only ever mutate the list for _activeProfile.
    private LayoutFile _layout = new();
    private string _activeProfile = "";
    private HwndSource? _displayWatcher;
    private const int WM_DISPLAYCHANGE = 0x007E;
    // Debounces bursts of DISPLAYCHANGE messages (Parsec and display driver swaps often fire 3-5
    // notifications back-to-back as modes/orientations settle).
    private DispatcherTimer? _displayChangeDebounce;

    private SingleInstance? _singleInstance;
    private bool _isSecondaryInstance;
    private TrayIcon? _tray;
    private ExplorerRestartWatcher? _explorerWatcher;
    // Explorer fires TaskbarCreated as soon as its shell window exists, but the WorkerW we re-parent
    // to may take a beat longer to spawn -- debounce so the re-attach lands on a ready desktop.
    private DispatcherTimer? _explorerRestartDebounce;
    // Watchdog that keeps captured icons hidden -- Windows clamps off-screen icons back onto the
    // desktop whenever Explorer commits a layout change (e.g. the user drags any other icon).
    private DispatcherTimer? _rehideTimer;

    public IReadOnlyList<FenceWindow> Fences => _fences;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single-instance guard FIRST -- before any desktop manipulation. A second launch must not
        // re-hide icons or re-fire the WorkerW spawn (which previously dragged the live desktop
        // icons off-screen). Instead it pings the running instance to surface its fences, then quits.
        _singleInstance = new SingleInstance();
        if (!_singleInstance.IsFirstInstance)
        {
            SingleInstance.SignalExistingInstance();
            _isSecondaryInstance = true;
            Shutdown();
            return;
        }

        Logger.Reset();
        Logger.Log("=== App startup ===");

        // Carry the user's layout + run-at-login across the DesktopFences -> Pickets rename. No-op
        // once Pickets state exists. Must run before LayoutStore.Load() below.
        LegacyMigration.Run();

        InstallCrashHandlers();

        var autoArrange = DesktopIconHider.IsAutoArrangeOn();
        var snapToGrid = DesktopIconHider.IsSnapToGridOn();

        if (autoArrange || snapToGrid)
        {
            var problems = (autoArrange ? "\"Auto arrange icons\"" : "") +
                           (autoArrange && snapToGrid ? " and " : "") +
                           (snapToGrid ? "\"Align icons to grid\"" : "");
            MessageBox.Show(
                $"Your desktop has {problems} enabled. Pickets can only hide " +
                "icons cleanly when both are OFF.\n\nRight-click the desktop → View → " +
                "uncheck both, then relaunch.",
                "Pickets", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        _layout = LayoutStore.Load();
        _activeProfile = DisplayProfile.CurrentKey();
        Logger.Log($"Active display profile: {_activeProfile}");
        LoadActiveProfile();

        // Always keep at least one fence around so the user has a UI surface to act from.
        if (_fences.Count == 0)
            CreateFence(200, 200);

        _saveDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _saveDebounce.Tick += (_, _) =>
        {
            if (!_stateDirty) return;
            _stateDirty = false;
            SaveLayout();
        };
        _saveDebounce.Start();

        // Keep captured icons hidden. When nothing has drifted this is just a cheap read of the
        // desktop view (no reposition, no repaint, no effect on the user's selection); it only acts
        // when Explorer has clamped a hidden icon back on-screen.
        _rehideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
        _rehideTimer.Tick += (_, _) => RehideDriftedIcons();
        _rehideTimer.Start();

        InstallDisplayChangeWatcher();

        // Ctrl+Alt+D hides/shows every fence -- the "I need a clean desktop right now" panic button.
        _quickHide = new GlobalHotKey(
            WindowInterop.MOD_CONTROL | WindowInterop.MOD_ALT,
            WindowInterop.VK_D,
            ToggleAllFencesVisibility);

        // Iconic Fences gesture: double-click on the bare desktop toggles fence visibility.
        _desktopHook = new DesktopDoubleClickHook(ToggleAllFencesVisibility);

        // Shift+right-click-drag on the desktop to lasso icons into a new fence.
        _lassoOverlay = new LassoOverlay();
        _lassoHook = new DesktopLassoHook();
        _lassoHook.DragStarted += pt => Dispatcher.BeginInvoke(() =>
            _lassoOverlay.Show(new RECT { left = pt.X, top = pt.Y, right = pt.X, bottom = pt.Y }));
        _lassoHook.DragUpdated += rect => Dispatcher.BeginInvoke(() =>
            _lassoOverlay.UpdateRect(rect));
        _lassoHook.DragEnded += rect => Dispatcher.BeginInvoke(() =>
        {
            _lassoOverlay.Hide();
            CreateFenceFromLasso(rect);
        });
        _lassoHook.DragCancelled += () => Dispatcher.BeginInvoke(() => _lassoOverlay.Hide());

        // Tray icon: the always-reachable control surface. The fences can all be hidden at once, so
        // without this there's no way to prove the app is running or to quit it.
        _tray = new TrayIcon(
            onToggleVisibility: ToggleAllFencesVisibility,
            onNewFence:         () => CreateFence(300, 200),
            onRestoreAndQuit:   RestoreAllAndQuit,
            onQuit:             Shutdown,
            getRunAtLogin:      () => StartupEntry.IsEnabled,
            setRunAtLogin:      enabled => { if (enabled) StartupEntry.Enable(); else StartupEntry.Disable(); });

        // A later launch (or our own SignalExistingInstance) asks us to surface the fences. The
        // callback arrives on a thread-pool thread, so hop to the UI thread before touching windows.
        _singleInstance.ListenForShowRequests(() => Dispatcher.BeginInvoke(SurfaceAllFences));

        // Re-attach fences to the new WorkerW whenever Explorer restarts, so they never get orphaned.
        _explorerRestartDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
        _explorerRestartDebounce.Tick += (_, _) =>
        {
            _explorerRestartDebounce!.Stop();
            ReattachFencesToDesktop();
        };
        _explorerWatcher = new ExplorerRestartWatcher();
        _explorerWatcher.Restarted += () => Dispatcher.BeginInvoke(() =>
        {
            _explorerRestartDebounce?.Stop();
            _explorerRestartDebounce?.Start();
        });
    }

    /// <summary>Makes every fence visible and brings them forward -- the response to a second launch
    /// attempt, so re-running the app feels like "focus the existing one" rather than a silent no-op.</summary>
    private void SurfaceAllFences()
    {
        foreach (var f in _fences)
        {
            f.Show();
            f.Activate();
        }
        _tray?.ShowBalloon("Pickets is already running",
            "Your fences are on the desktop. Use the tray icon to hide them or quit.");
    }

    /// <summary>After an Explorer restart the old WorkerW is gone, so the fences are orphaned and
    /// invisible. Snapshot, tear down, and respawn them: each new window re-parents to the freshly
    /// spawned WorkerW (via FenceWindow.OnSourceInitialized) and re-hides its captured icons.</summary>
    private void ReattachFencesToDesktop()
    {
        Logger.Log("Explorer restarted -- respawning fences onto the new WorkerW.");
        var states = _fences.Select(f => f.ToState()).ToList();
        foreach (var f in _fences.ToList())
            f.Close();
        _fences.Clear();

        foreach (var state in states)
            SpawnFence(state);

        if (_fences.Count == 0)
            CreateFence(200, 200);
    }

    /// <summary>Re-hides any captured desktop icons Explorer has clamped back on-screen. Skips portal
    /// fences (their items live in a real folder, not on the desktop) and items we never captured a
    /// desktop position for (e.g. files dragged in from a folder rather than off the desktop).</summary>
    private void RehideDriftedIcons()
    {
        var paths = _fences
            .Where(f => !f.IsPortal)
            .SelectMany(f => f.Items)
            .Where(i => i.Kind == ItemKind.File && i.OriginalDesktopPos.HasValue && !i.IsMissing)
            .Select(i => i.Path)
            .ToList();
        if (paths.Count == 0) return;

        int n = DesktopIconHider.ReapplyHiddenBatch(paths);
        if (n > 0) Logger.Log($"Re-hid {n} icon(s) that drifted back on-screen.");
    }

    /// <summary>Routes unhandled exceptions from all three managed sources into the log. WPF
    /// dispatcher exceptions are marked Handled so a stray UI failure (e.g., a corrupt icon
    /// during a paint) doesn't tear the whole process down and the user loses nothing --
    /// anything truly fatal will still come through AppDomain.UnhandledException with
    /// IsTerminating=true, which at least leaves a log line before the crash dump.</summary>
    private void InstallCrashHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            Logger.Log($"DispatcherUnhandledException: {args.Exception}");
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            Logger.Log($"AppDomain.UnhandledException (terminating={args.IsTerminating}): {ex}");
        };

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Logger.Log($"TaskScheduler.UnobservedTaskException: {args.Exception}");
            args.SetObserved();
        };
    }

    /// <summary>Instantiates fence windows for the currently-active display profile, clamping
    /// every saved position into the visible work area first so nothing spawns off-screen.</summary>
    private void LoadActiveProfile()
    {
        var profileFences = LayoutStore.GetOrSeedProfile(_layout, _activeProfile);
        foreach (var state in profileFences)
        {
            DisplayProfile.ClampToVisibleWorkArea(state);
            SpawnFence(state);
        }
    }

    private void InstallDisplayChangeWatcher()
    {
        // Message-only window to receive WM_DISPLAYCHANGE regardless of which fence has focus.
        var p = new HwndSourceParameters("PicketsDisplayWatcher")
        {
            ParentWindow = new IntPtr(-3), // HWND_MESSAGE
        };
        _displayWatcher = new HwndSource(p);
        _displayWatcher.AddHook(DisplayWndProc);

        _displayChangeDebounce = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(600),
        };
        _displayChangeDebounce.Tick += (_, _) =>
        {
            _displayChangeDebounce!.Stop();
            HandleDisplayChange();
        };
    }

    private IntPtr DisplayWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_DISPLAYCHANGE)
        {
            // Restart the debounce timer -- Parsec and driver swaps often fire several in a row.
            _displayChangeDebounce?.Stop();
            _displayChangeDebounce?.Start();
        }
        return IntPtr.Zero;
    }

    /// <summary>Called once the display-change burst has settled. Compares the new profile key
    /// to the active one; if different, snapshots the outgoing profile, tears down its windows,
    /// and loads/seeds the new profile.</summary>
    private void HandleDisplayChange()
    {
        var newKey = DisplayProfile.CurrentKey();

        if (newKey == _activeProfile)
        {
            // Same monitors, but a fence might have landed off the new work area (Parsec can
            // shift the work-area origin without changing resolution strings). Re-clamp in place.
            foreach (var f in _fences)
            {
                var state = f.ToState();
                DisplayProfile.ClampToVisibleWorkArea(state);
                f.Left = state.X; f.Top = state.Y;
                f.Width = state.Width; f.Height = state.Height;
            }
            MarkDirty();
            return;
        }

        Logger.Log($"Display profile changed: '{_activeProfile}' -> '{newKey}'");

        // Snapshot current fences into the outgoing profile and keep as the seed for new profiles.
        var outgoing = _fences.Select(f => f.ToState()).ToList();
        _layout.Profiles[_activeProfile] = outgoing;
        _layout.LastProfileSeed = outgoing;

        // Close current fence windows -- positions in the new monitor space differ entirely.
        foreach (var f in _fences.ToList())
            f.Close();
        _fences.Clear();

        _activeProfile = newKey;
        LoadActiveProfile();

        if (_fences.Count == 0)
            CreateFence(200, 200);

        SaveLayout();
    }

    /// <summary>Spawns a fence sized to the lasso rect, capturing any desktop icons inside it.</summary>
    private void CreateFenceFromLasso(RECT rect)
    {
        const int MIN_DIM = 40;
        int width = Math.Max(MIN_DIM, rect.right - rect.left);
        int height = Math.Max(MIN_DIM, rect.bottom - rect.top);

        // Enumerate on the original rect, not the expanded fence bounds.
        var captured = DesktopIconHider.EnumerateItemsInRect(rect);

        var state = new FenceState
        {
            Title = captured.Count > 0 ? "Lassoed" : "New fence",
            X = rect.left,
            Y = rect.top,
            Width = Math.Max(240, width),
            Height = Math.Max(180, height),
        };

        foreach (var (path, _) in captured)
        {
            var original = DesktopIconHider.Hide(path);
            state.Items.Add(new ItemState
            {
                Path = path,
                OriginalX = original?.X,
                OriginalY = original?.Y,
                Kind = ItemKind.File,
            });
        }

        var fence = SpawnFence(state);
        MarkDirty();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // A secondary instance set up none of the subsystems below and -- crucially -- must NOT
        // SaveLayout(), or it would persist its empty fence list over the real layout. Just release
        // its mutex handle and leave.
        if (_isSecondaryInstance)
        {
            _singleInstance?.Dispose();
            base.OnExit(e);
            return;
        }

        _rehideTimer?.Stop();
        _explorerRestartDebounce?.Stop();
        _explorerWatcher?.Dispose();
        _tray?.Dispose();
        _displayChangeDebounce?.Stop();
        if (_displayWatcher != null)
        {
            _displayWatcher.RemoveHook(DisplayWndProc);
            _displayWatcher.Dispose();
        }
        _lassoHook?.Dispose();
        _lassoOverlay?.Dispose();
        _desktopHook?.Dispose();
        _quickHide?.Dispose();
        SaveLayout();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    /// <summary>If any fence is visible, hides them all; otherwise shows them all.</summary>
    public void ToggleAllFencesVisibility()
    {
        if (_fences.Count == 0) return;
        var anyVisible = _fences.Any(f => f.IsVisible);
        foreach (var f in _fences)
        {
            if (anyVisible) f.Hide();
            else            f.Show();
        }
    }

    private void MarkDirty()
    {
        _stateDirty = true;
        // Any layout change can flip another fence's "touching neighbors" state, so refresh all.
        foreach (var f in _fences) f.RefreshLinkState();
    }

    public int FenceCount => _fences.Count;

    public FenceWindow CreateFence(double x, double y)
    {
        var state = new FenceState
        {
            Title = "New fence",
            X = x, Y = y,
            Width = 320, Height = 240,
        };
        var fence = SpawnFence(state);
        MarkDirty();
        return fence;
    }

    public void DeleteFence(FenceWindow fence)
    {
        // Portal items live in a real folder -- they never had a captured desktop position,
        // so the restore loop is both unnecessary and semantically wrong for them.
        if (!fence.IsPortal)
        {
            foreach (var item in fence.Items)
            {
                if (item.OriginalDesktopPos.HasValue && !item.IsMissing)
                    DesktopIconHider.Restore(item.Path, item.OriginalDesktopPos.Value);
            }
        }
        _fences.Remove(fence);
        fence.Close();
        MarkDirty();
    }

    private FenceWindow SpawnFence(FenceState state)
    {
        var fence = new FenceWindow(state);
        fence.LayoutChanged += (_, _) => MarkDirty();
        fence.Show();
        _fences.Add(fence);
        // Spawning a new fence may already overlap an existing one -- refresh everyone's link state.
        foreach (var f in _fences) f.RefreshLinkState();
        return fence;
    }

    private void SaveLayout()
    {
        // Persist only the active profile -- other profiles in _layout remain untouched.
        _layout.Profiles[_activeProfile] = _fences.Select(f => f.ToState()).ToList();
        _layout.LastProfileSeed = _layout.Profiles[_activeProfile];
        LayoutStore.Save(_layout);
    }

    /// <summary>Restores every captured icon to its original desktop position, then quits.</summary>
    public void RestoreAllAndQuit()
    {
        var result = MessageBox.Show(
            "Restore all hidden desktop icons and quit Pickets?\n\n" +
            "Your fence layout will still be saved -- icons will be hidden again next launch.",
            "Pickets", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (result != MessageBoxResult.OK) return;

        // Stop the watchdog first, or it would race us and shove the icons we're about to restore
        // straight back off-screen.
        _rehideTimer?.Stop();

        foreach (var fence in _fences)
        {
            if (fence.IsPortal) continue; // portal items aren't captured from the desktop
            foreach (var item in fence.Items)
            {
                if (item.OriginalDesktopPos.HasValue)
                    DesktopIconHider.Restore(item.Path, item.OriginalDesktopPos.Value);
            }
        }

        SaveLayout();
        Shutdown();
    }
}
