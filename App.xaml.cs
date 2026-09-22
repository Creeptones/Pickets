using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Pickets;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
    Justification = "WPF owns the Application lifetime; all native and managed resources are released in OnExit.")]
public partial class App : Application
{
    private readonly List<PicketWindow> _pickets = new();
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
    private bool _isRecoveryMode;
    private TrayIcon? _tray;
    private ExplorerRestartWatcher? _explorerWatcher;
    // Explorer fires TaskbarCreated as soon as its shell window exists, but the WorkerW we re-parent
    // to may take a beat longer to spawn -- debounce so the re-attach lands on a ready desktop.
    private DispatcherTimer? _explorerRestartDebounce;
    // Watchdog that keeps captured icons hidden -- Windows clamps off-screen icons back onto the
    // desktop whenever Explorer commits a layout change (e.g. the user drags any other icon).
    private DispatcherTimer? _rehideTimer;

    public IReadOnlyList<PicketWindow> Pickets => _pickets;
    internal RenderFrameLoop Frames { get; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var silentRecoveryRequested = e.Args.Any(arg =>
            arg.Equals("--restore-icons-silent", StringComparison.OrdinalIgnoreCase));
        var recoveryRequested = silentRecoveryRequested || e.Args.Any(arg =>
            arg.Equals("--restore-icons", StringComparison.OrdinalIgnoreCase));

        // Single-instance guard FIRST -- before any desktop manipulation. A second launch must not
        // re-hide icons or re-fire the WorkerW spawn (which previously dragged the live desktop
        // icons off-screen). Instead it pings the running instance to surface its pickets, then quits.
        _singleInstance = new SingleInstance();
        if (!_singleInstance.IsFirstInstance)
        {
            _isSecondaryInstance = true;
            if (silentRecoveryRequested)
            {
                SingleInstance.SignalPrepareRecovery();
                if (!_singleInstance.WaitForOwnerExit(TimeSpan.FromSeconds(30)))
                {
                    Shutdown((int)RecoveryExitCode.OwnerDidNotExit);
                    return;
                }
                Logger.StartSession();
                RunEmergencyRecovery(silent: true);
                return;
            }
            if (recoveryRequested) SingleInstance.SignalRestoreRequest();
            else                   SingleInstance.SignalExistingInstance();
            Shutdown();
            return;
        }

        Logger.StartSession();
        Logger.Log("=== App startup ===");

        InstallCrashHandlers();
        ApplyMenuPalette();
        SystemParameters.StaticPropertyChanged += MenuAccessibilityChanged;

        if (recoveryRequested)
        {
            RunEmergencyRecovery(silentRecoveryRequested);
            return;
        }

        var loadedLayout = LayoutStore.Load();
        if (loadedLayout == null)
        {
            _isRecoveryMode = true; // Never overwrite unreadable layouts with an empty session.
            MessageBox.Show("Pickets could not read your saved layout or its backup. Your saved files have been left unchanged. Check the diagnostic log before trying again.",
                "Pickets recovery", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown((int)RecoveryExitCode.LayoutUnavailable);
            return;
        }
        _layout = loadedLayout;
        // Complete readiness before creating windows or hiding any desktop icons.
        if ((!_layout.HasCompletedOnboarding || !DesktopReadiness.Read().IsReady) && !TryShowWelcome())
        {
            _isRecoveryMode = true; // No windows were loaded; do not save an empty profile on exit.
            Shutdown();
            return;
        }

        _activeProfile = DisplayProfile.CurrentKey();
        Logger.Log($"Active display profile: {_activeProfile}");
        LoadActiveProfile();

        // Always keep at least one picket around so the user has a UI surface to act from.
        if (_pickets.Count == 0)
            CreatePicket(200, 200);

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

        // Configurable global shortcut: summon keyboard focus, or hide when already focused.
        RegisterFocusShortcut();

        // Desktop double-click gesture: double-click on the bare desktop toggles picket visibility.
        _desktopHook = new DesktopDoubleClickHook(ToggleAllPicketsVisibility);

        // Shift+right-click-drag on the desktop to lasso icons into a new picket.
        _lassoOverlay = new LassoOverlay();
        _lassoHook = new DesktopLassoHook();
        _lassoHook.DragStarted += pt => Dispatcher.BeginInvoke(() =>
            _lassoOverlay.Show(new RECT { left = pt.X, top = pt.Y, right = pt.X, bottom = pt.Y }));
        _lassoHook.DragUpdated += rect => Dispatcher.BeginInvoke(() =>
            _lassoOverlay.UpdateRect(rect));
        _lassoHook.DragEnded += rect => Dispatcher.BeginInvoke(() =>
        {
            _lassoOverlay.Hide();
            CreatePicketFromLasso(rect);
        });
        _lassoHook.DragCancelled += () => Dispatcher.BeginInvoke(() => _lassoOverlay.Hide());

        // Tray icon: the always-reachable control surface. The pickets can all be hidden at once, so
        // without this there's no way to prove the app is running or to quit it.
        _tray = new TrayIcon(
            onToggleVisibility: ToggleAllPicketsVisibility,
            onFocus: FocusPickets,
            onShortcutSettings: () => ConfigureFocusShortcut(null),
            onKeyboardHelp: PicketWindow.ShowKeyboardHelp,
            onNewPicket:         () => CreatePicket(300, 200),
            onShowWelcome:       ShowWelcome,
            onReleaseAndQuit:    ReleaseAllCapturedIconsAndQuit,
            onQuit:              QuitAndRestoreIcons,
            onExitHidden:        Shutdown,
            onAbout:             ShowAbout,
            getRunAtLogin:      () => StartupEntry.IsEnabled,
            setRunAtLogin:      enabled => { if (enabled) StartupEntry.Enable(); else StartupEntry.Disable(); });

        // A later launch (or our own SignalExistingInstance) asks us to surface the pickets. The
        // callback arrives on a thread-pool thread, so hop to the UI thread before touching windows.
        _singleInstance.ListenForRequests(
            () => Dispatcher.BeginInvoke(SurfaceAllPickets),
            () => Dispatcher.BeginInvoke(() => ReleaseAllCapturedIconsAndQuit(confirm: false)),
            () => Dispatcher.BeginInvoke(PrepareForSilentRecovery));

        // Re-attach pickets to the new WorkerW whenever Explorer restarts, so they never get orphaned.
        _explorerRestartDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
        _explorerRestartDebounce.Tick += (_, _) =>
        {
            _explorerRestartDebounce!.Stop();
            ReattachPicketsToDesktop();
        };
        _explorerWatcher = new ExplorerRestartWatcher();
        _explorerWatcher.Restarted += () => Dispatcher.BeginInvoke(() =>
        {
            _explorerRestartDebounce?.Stop();
            _explorerRestartDebounce?.Start();
        });
    }

    private void ShowWelcome() => TryShowWelcome();

    private bool TryShowWelcome()
    {
        var firstRun = !_layout.HasCompletedOnboarding;
        var welcome = new WelcomeWindow(DesktopShortcut.Exists, StartupEntry.IsEnabled);
        if (welcome.ShowDialog() != true) return false;

        if (welcome.CreateDesktopShortcut && !DesktopShortcut.TryCreate(out var shortcutError))
        {
            MessageBox.Show($"Pickets could not create the desktop shortcut.\n\n{shortcutError}",
                "Pickets", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        if (welcome.RunAtLogin) StartupEntry.Enable();
        else                    StartupEntry.Disable();

        _layout.HasCompletedOnboarding = true;
        if (_pickets.Count == 0) LayoutStore.Save(_layout);
        else SaveLayout();
        if (firstRun)
            _tray?.ShowBalloon("Pickets is ready",
                "Drag in an icon to begin. Reopen the quick start guide from the tray anytime.");
        return true;
    }

    /// <summary>Makes every picket visible and brings them forward -- the response to a second launch
    /// attempt, so re-running the app feels like "focus the existing one" rather than a silent no-op.</summary>
    private void SurfaceAllPickets()
    {
        PicketsHidden = false;
        foreach (var f in _pickets)
        {
            f.ShowIfOnStackPage();
            if (f.IsOnStackPage) f.Activate();
        }
        _tray?.ShowBalloon("Pickets is already running",
            "Your pickets are on the desktop. Use the tray icon to hide them or quit.");
    }

    /// <summary>After an Explorer restart the old WorkerW is gone, so the pickets are orphaned and
    /// invisible. Snapshot, tear down, and respawn them: each new window re-parents to the freshly
    /// spawned WorkerW (via PicketWindow.OnSourceInitialized) and re-hides its captured icons.</summary>
    private void ReattachPicketsToDesktop()
    {
        Logger.Log("Explorer restarted -- respawning pickets onto the new WorkerW.");
        var states = _pickets.Select(f => f.ToState()).ToList();
        foreach (var f in _pickets.ToList())
            f.CloseForLayoutChange();
        _pickets.Clear();

        foreach (var state in states)
            SpawnPicket(state);
        foreach (var group in _pickets.GroupBy(p => p.GroupId)) group.First().NormalizeConnectedGroup();

        if (_pickets.Count == 0)
            CreatePicket(200, 200);
    }

    /// <summary>Re-hides any captured desktop icons Explorer has clamped back on-screen. Items we
    /// never captured from the desktop have no saved position and are ignored.</summary>
    private void RehideDriftedIcons()
    {
        var paths = _pickets
            .SelectMany(f => f.Items)
            .Where(i => i.Kind == ItemKind.File && i.OriginalDesktopPos.HasValue)
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
            if (_isRecoveryMode) Shutdown((int)RecoveryExitCode.LayoutUnavailable);
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

    /// <summary>Instantiates picket windows for the currently-active display profile, clamping
    /// every saved position into the visible work area first so nothing spawns off-screen.</summary>
    private void LoadActiveProfile()
    {
        var profilePickets = LayoutStore.GetOrSeedProfile(_layout, _activeProfile);
        foreach (var state in profilePickets)
        {
            DisplayProfile.ClampToVisibleWorkArea(state);
            SpawnPicket(state);
        }

        // Older layouts allowed small seams and fractional size differences inside a connected
        // group. Repair them only after every member exists, so cluster discovery sees the whole
        // saved stack rather than normalizing one window at a time while it is being spawned.
        foreach (var picket in _pickets.Where(p => p.NeedsGroupMigration).ToList())
            if (picket.NeedsGroupMigration) picket.JoinTouchingGroups(migrateOnly: true);
        foreach (var picket in _pickets.GroupBy(p => p.GroupId).Select(g => g.First()))
            picket.NormalizeConnectedGroup();
    }

    private void InstallDisplayChangeWatcher()
    {
        // Message-only window to receive WM_DISPLAYCHANGE regardless of which picket has focus.
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
            // Same monitors, but a picket might have landed off the new work area (Parsec can
            // shift the work-area origin without changing resolution strings). Re-clamp in place.
            ClampPicketsToWorkArea();
            return;
        }

        Logger.Log($"Display profile changed: '{_activeProfile}' -> '{newKey}'");

        // Persist current captures before replacing their windows. A failed save/restore keeps
        // those windows available, clamped to the new display so the user can still recover.
        if (!SaveLayout())
        {
            ClampPicketsToWorkArea();
            _tray?.ShowBalloon("Display layout kept", "Pickets could not save your layout. Your current pickets remain available; check the diagnostic log.");
            return;
        }
        var incoming = LayoutStore.GetOrSeedProfile(_layout, newKey);
        if (!IconCaptureRecovery.TryRestoreInactive(_layout, _activeProfile, incoming, RestoreCapturedIcon))
        {
            ClampPicketsToWorkArea();
            _tray?.ShowBalloon("Desktop icons need recovery", "Windows could not restore every icon. Your current pickets were kept; choose Quit Pickets to retry recovery.");
            return;
        }

        // Close current picket windows -- positions in the new monitor space differ entirely.
        foreach (var f in _pickets.ToList())
            f.CloseForLayoutChange();
        _pickets.Clear();

        _activeProfile = newKey;
        LoadActiveProfile();

        if (_pickets.Count == 0)
            CreatePicket(200, 200);

        SaveLayout();
    }

    private void ClampPicketsToWorkArea()
    {
        foreach (var picket in _pickets)
        {
            var state = picket.ToState();
            DisplayProfile.ClampToVisibleWorkArea(state);
            picket.Left = state.X; picket.Top = state.Y;
            picket.Width = state.Width; picket.Height = state.Height;
        }
        foreach (var group in _pickets.GroupBy(p => p.GroupId)) group.First().NormalizeConnectedGroup();
        MarkDirty();
    }

    /// <summary>Spawns a picket sized to the lasso rect, capturing any desktop icons inside it.</summary>
    private void CreatePicketFromLasso(RECT rect)
    {
        const int MIN_DIM = 40;
        int width = Math.Max(MIN_DIM, rect.right - rect.left);
        int height = Math.Max(MIN_DIM, rect.bottom - rect.top);

        // Enumerate on the original rect, not the expanded picket bounds.
        var captured = DesktopIconHider.EnumerateItemsInRect(rect);

        var state = new PicketState
        {
            Title = captured.Count > 0 ? "Lassoed" : "New picket",
            X = rect.left,
            Y = rect.top,
            Width = Math.Max(240, width),
            Height = Math.Max(180, height),
            ColorKey = PicketColors.Get(_layout.DefaultColorKey).Key,
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

        var picket = SpawnPicket(state);
        MarkDirty();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Frames.Dispose();
        SystemParameters.StaticPropertyChanged -= MenuAccessibilityChanged;
        // A secondary instance set up none of the subsystems below and -- crucially -- must NOT
        // SaveLayout(), or it would persist its empty picket list over the real layout. Just release
        // its mutex handle and leave.
        if (_isSecondaryInstance || _isRecoveryMode)
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

    /// <summary>If any picket is visible, hides them all; otherwise shows them all.</summary>
    internal bool PicketsHidden { get; private set; }
    internal bool IsShuttingDown { get; private set; }

    public new void Shutdown() { IsShuttingDown = true; base.Shutdown(); }
    public new void Shutdown(int exitCode) { IsShuttingDown = true; base.Shutdown(exitCode); }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        IsShuttingDown = true;
        base.OnSessionEnding(e);
    }

    internal void HidePickets(bool explain = false)
    {
        foreach (var picket in _pickets) picket.SettleStack();
        PicketsHidden = true;
        foreach (var picket in _pickets) picket.Hide();
        if (explain) _tray?.ShowBalloon("Pickets is hidden",
            "Pickets is still running. Use the tray or focus shortcut to return; choose Quit to restore desktop icons.");
    }

    internal void ShowPickets()
    {
        PicketsHidden = false;
        foreach (var picket in _pickets) picket.ShowIfOnStackPage();
    }

    public void ToggleAllPicketsVisibility()
    {
        if (_pickets.Any(f => f.IsVisible)) HidePickets();
        else ShowPickets();
    }

    private void MarkDirty()
    {
        _stateDirty = true;
        // Dragging, resizing and reflow can raise dozens of events for one visible frame.
        // Inspect the final geometry once, rather than all intermediate window bounds.
        if (!IsShuttingDown) Frames.Request(RefreshPicketChrome);
    }

    private void RefreshPicketChrome()
    {
        foreach (var group in _pickets.GroupBy(p => p.GroupId))
        {
            var members = group.ToList();
            foreach (var picket in members) picket.RefreshLinkState(members);
        }
    }

    /// <summary>Applies a theme to every saved profile, every active Picket, and future Pickets.
    /// Choosing a theme normally remains scoped to the Picket whose menu was used.</summary>
    public void ApplyColorThemeToAll(string key)
    {
        var canonicalKey = PicketColors.Get(key).Key;
        _layout.DefaultColorKey = canonicalKey;

        foreach (var profile in _layout.Profiles.Values)
            foreach (var state in profile)
                state.ColorKey = canonicalKey;
        if (_layout.LastProfileSeed != null)
            foreach (var state in _layout.LastProfileSeed)
                state.ColorKey = canonicalKey;
        foreach (var appearance in _layout.Appearances.Values)
            appearance.ColorKey = canonicalKey;

        foreach (var picket in _pickets)
            picket.ApplyColorTheme(canonicalKey);
        MarkDirty();
    }

    public int PicketCount => _pickets.Count;

    public PicketWindow CreatePicket(double x, double y, PicketWindow? attachAfter = null)
    {
        attachAfter?.SettleStack();
        var state = new PicketState
        {
            Title = "New picket",
            X = x, Y = y,
            Width = 320, Height = 240,
            ColorKey = PicketColors.Get(_layout.DefaultColorKey).Key,
        };
        var picket = SpawnPicket(state);
        if (attachAfter != null) picket.AttachAfter(attachAfter);
        else picket.JoinTouchingGroups();
        picket.FocusForKeyboard();
        MarkDirty();
        return picket;
    }

    public void DeletePicket(PicketWindow picket)
    {
        var group = picket.SettleStack();
        var anchor = new Point(group[0].Left, group[0].Top);
        if (!picket.TryReleaseAllReferences()) return;
        _pickets.Remove(picket);
        picket.CloseForLayoutChange();
        var remaining = group.Where(p => p != picket).ToList();
        if (remaining.Count > 0)
        {
            remaining[0].Left = anchor.X;
            remaining[0].Top = anchor.Y;
            remaining[0].NormalizeConnectedGroup();
        }
        MarkDirty();
    }

    private PicketWindow SpawnPicket(PicketState state)
    {
        ApplyGlobalAppearance(state);
        var picket = new PicketWindow(state);
        picket.LayoutChanged += (_, _) => MarkDirty();
        _pickets.Add(picket);
        picket.Show();
        if (PicketsHidden) picket.Hide();
        // Spawning a new picket may already overlap an existing one -- refresh everyone's link state.
        Frames.Request(RefreshPicketChrome);
        return picket;
    }

    /// <summary>Makes a picket's look follow it across every display. If the shared map already has
    /// an entry for this title, the picket adopts it; otherwise the picket's own saved look seeds the
    /// map. Only color/transparency/blur are shared -- position and contents stay per-display.</summary>
    private void ApplyGlobalAppearance(PicketState state)
    {
        if (_layout.Appearances.TryGetValue(state.Title, out var look))
        {
            state.ColorKey = look.ColorKey;
            state.TransparencyKey = look.TransparencyKey;
            state.TransparencyCustomPercent = look.TransparencyCustomPercent;
            state.BlurEnabled = look.BlurEnabled;
        }
        else
        {
            _layout.Appearances[state.Title] = LookOf(state);
        }
    }

    private static PicketAppearance LookOf(PicketState s) => new()
    {
        ColorKey = s.ColorKey,
        TransparencyKey = s.TransparencyKey,
        TransparencyCustomPercent = s.TransparencyCustomPercent,
        BlurEnabled = s.BlurEnabled,
    };

    private bool SaveLayout()
    {
        // Persist only the active profile -- other profiles in _layout remain untouched.
        var states = _pickets.Select(f => f.ToState()).ToList();
        _layout.Profiles[_activeProfile] = states;
        _layout.LastProfileSeed = states;
        // Appearance is global: each picket's current look updates the shared, per-title map so the
        // same color/transparency/blur shows on every display the next time that profile loads.
        foreach (var s in states)
            _layout.Appearances[s.Title] = LookOf(s);
        return LayoutStore.TrySave(_layout);
    }

    /// <summary>Normal quit: leave the saved ownership metadata intact, but make desktop icons
    /// visible while Pickets is not running. A later launch collects them again.</summary>
    public void QuitAndRestoreIcons()
    {
        var result = MessageBox.Show(
            "Restore all hidden desktop icons and quit Pickets?\n\n" +
            "Your layout stays saved and Pickets will collect the icons again next launch.",
            "Pickets", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (result != MessageBoxResult.OK) return;

        // Stop the watchdog first, or it would race us and shove the icons we're about to restore
        // straight back off-screen.
        _rehideTimer?.Stop();

        if (!SaveLayout())
        {
            _rehideTimer?.Start();
            MessageBox.Show("Pickets could not save your layout. It will stay open so captured icons remain recoverable. Check the diagnostic log and try again.",
                "Pickets", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        // Include captures from previous display profiles, including older stranded icons.
        var captured = IconCaptureRecovery.Collect(_layout, _activeProfile);
        var restored = RestoreCapturedIcons(captured);
        if (restored.Count != captured.Count)
        {
            var quitAnyway = MessageBox.Show(
                $"Windows restored {restored.Count} of {captured.Count} captured icon(s).\n\n" +
                "Some icons remain hidden. Quit anyway? Choose No to keep Pickets running and retry.",
                "Pickets", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (quitAnyway != MessageBoxResult.Yes)
            {
                _rehideTimer?.Start();
                return;
            }
        }

        SaveLayout();
        Shutdown();
    }

    /// <summary>Permanently releases icon ownership in every display profile before quitting.
    /// Picket shortcuts remain, but future launches will not hide their desktop icons.</summary>
    public void ReleaseAllCapturedIconsAndQuit() => ReleaseAllCapturedIconsAndQuit(confirm: true);

    private void ReleaseAllCapturedIconsAndQuit(bool confirm)
    {
        if (confirm)
        {
            var result = MessageBox.Show(
                "Release every captured icon back to the desktop and quit?\n\n" +
                "Picket shortcuts will remain, but future launches will no longer hide those " +
                "desktop icons.",
                "Pickets", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (result != MessageBoxResult.OK) return;
        }

        _rehideTimer?.Stop();
        SaveLayout();
        var captured = IconCaptureRecovery.Collect(_layout, _activeProfile);
        var restored = RestoreCapturedIcons(captured);
        IconCaptureRecovery.Release(_layout, restored);
        var restoredSet = restored.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in _pickets.SelectMany(picket => picket.Items))
            if (restoredSet.Contains(item.Path)) item.OriginalDesktopPos = null;
        SaveLayout();

        if (confirm)
            MessageBox.Show(
                $"Released {restored.Count} of {captured.Count} captured desktop icon(s)." +
                (restored.Count == captured.Count
                    ? ""
                    : "\n\nIcons Windows could not restore remain captured so recovery can retry later."),
                "Pickets", MessageBoxButton.OK,
                restored.Count == captured.Count ? MessageBoxImage.Information : MessageBoxImage.Warning);
        Shutdown();
    }

    private void RunEmergencyRecovery(bool silent = false)
    {
        _isRecoveryMode = true;
        var recoveredLayout = LayoutStore.LoadForRecovery();
        if (recoveredLayout == null)
        {
            if (!silent) MessageBox.Show("Pickets could not read your saved layouts. Recovery has stopped to protect your desktop icons. Check the diagnostic log and try again.",
                "Pickets recovery", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown((int)RecoveryExitCode.LayoutUnavailable);
            return;
        }
        _layout = recoveredLayout;
        _activeProfile = DisplayProfile.CurrentKey();
        var captured = IconCaptureRecovery.Collect(_layout, _activeProfile);
        var restored = RestoreCapturedIcons(captured);
        IconCaptureRecovery.Release(_layout, restored);
        var saved = LayoutStore.TrySave(_layout);
        Logger.Log($"Emergency recovery released {restored.Count} of {captured.Count} captured icon(s).");

        if (!silent)
        {
            MessageBox.Show(
                $"Emergency recovery restored {restored.Count} of {captured.Count} captured desktop icon(s).\n\n" +
                (!saved ? "The restored state could not be saved. Check folder permissions and retry recovery."
                    : restored.Count == captured.Count
                    ? "Pickets will no longer hide those icons on future launches."
                    : "Icons Windows could not restore remain captured so you can retry recovery later."),
                "Pickets recovery", MessageBoxButton.OK,
                saved && restored.Count == captured.Count ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        Shutdown((int)RecoveryOutcome.From(restored.Count, captured.Count, saved));
    }

    private void PrepareForSilentRecovery()
    {
        // The waiting recovery process takes exclusive ownership after this process exits.
        // Do not exit if its input cannot be saved, or newly captured icons could be missed.
        _rehideTimer?.Stop();
        if (!SaveLayout())
        {
            _rehideTimer?.Start();
            return;
        }
        Shutdown();
    }

    private static List<string> RestoreCapturedIcons(IEnumerable<CapturedDesktopIcon> captured)
    {
        var restored = new List<string>();
        foreach (var icon in captured)
        {
            if (RestoreCapturedIcon(icon))
                restored.Add(icon.Path);
        }
        return restored;
    }

    private static bool RestoreCapturedIcon(CapturedDesktopIcon icon)
        => IconCaptureRecovery.TryRestore(icon,
            captured => DesktopIconHider.Restore(captured.Path, captured.OriginalPosition),
            path => FileReferenceProbe.Check(path).Status);

    public static void ShowAbout() => new AboutWindow().ShowDialog();
}
