using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace Pickets;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
    Justification = "WPF owns the window lifetime; the drag-image helper is disposed in Closed.")]
public partial class PicketWindow : Window
{
    public string PicketId { get; }
    internal string DisplayTitle => TitleText.Text;
    public ObservableCollection<PicketItem> Items { get; } = new();

    /// <summary>Raised whenever the picket's persistent state changes (move, resize, item add/remove).</summary>
    public event EventHandler? LayoutChanged;

    private bool _isLoading;
    private bool _isCollapsed;
    private double _expandedHeight;
    private bool _isRollAnimating;
    private string _colorKey = "porcelain";
    private string _transparencyKey = "solid";
    private int _transparencyCustomPercent = 50;
    private bool _blurEnabled;

    // Group-drag: when the user grabs this picket's title, every picket whose edges currently touch
    // ours (transitively) tags along, so a snapped row/column moves as one.
    private List<PicketWindow>? _dragCluster;
    private double _dragLastLeft, _dragLastTop;

    // Snap unstick: capture screen rect at drag-start so TrySnap can disable per-axis snapping
    // once the user has pulled past UNSTICK_PIXELS on that axis. Lets diagonals flow.
    private RECT _dragStartScreenRect;

    // Starts true: the menu's Slider parses with Value="50" inside InitializeComponent and fires
    // ValueChanged before our fields/named elements exist. Constructor flips this off at the end.
    private bool _suppressSliderEvent = true;

    // Effective % (100 = fully solid, 0 = invisible). Applied to bg/title/border alpha; text stays opaque.
    private int CurrentTransparencyPercent => _transparencyKey switch
    {
        "solid"  => 100,
        "light"  => 65,
        "medium" => 40,
        "heavy"  => 20,
        "custom" => _transparencyCustomPercent,
        _ => 100,
    };

    public PicketWindow(PicketState state)
    {
        InitializeComponent();
        PicketId = state.Id;
        GroupId = state.GroupId ?? state.Id;
        GroupOrder = state.GroupOrder;
        _groupHorizontal = state.GroupHorizontal;
        _accordionMode = state.AccordionMode;
        NeedsGroupMigration = state.GroupId == null;
        TitleText.Text = state.Title;
        Left = state.X; Top = state.Y; Width = state.Width; Height = state.Height;
        ItemsHost.ItemsSource = Items;
        TitleToggle.SetExpanded = SetExpanded;
        UpdateAccessibleTitle();
        _expandedHeight = state.Height;
        _autoSizeRows = state.AutoSizeRows;

        _colorKey = PicketColors.Get(state.ColorKey).Key;
        _transparencyKey = state.TransparencyKey;
        _transparencyCustomPercent = Math.Clamp(state.TransparencyCustomPercent, 0, 100);
        _blurEnabled = state.BlurEnabled;
        ApplyVisuals();

        _isLoading = true;
        foreach (var i in state.Items)
        {
            if (i.Kind == ItemKind.Label)
            {
                Items.Add(PicketItem.CreateLabel(i.LabelText ?? ""));
                continue;
            }
            var fi = PicketItem.FromPath(i.Path, i.IsFolder);
            if (i.HasOriginalPos)
                fi.OriginalDesktopPos = new POINT(i.OriginalX!.Value, i.OriginalY!.Value);
            fi.IsLarge = i.IsLarge;
            Items.Add(fi);
            _ = fi.RefreshAsync();

            // Re-apply offscreen position on launch (Explorer may have rearranged).
            if (i.HasOriginalPos)
                DesktopIconHider.ReapplyHidden(i.Path);
        }

        // One-time compatibility path: old folder portals did not persist their mirrored children.
        // Preserve the user's intent as a normal folder icon, then ToState drops PortalPath forever.
        if (!string.IsNullOrEmpty(state.PortalPath) &&
            !Items.Any(i => string.Equals(i.Path, state.PortalPath, StringComparison.OrdinalIgnoreCase)))
        {
            var portal = PicketItem.FromPath(state.PortalPath, true);
            Items.Add(portal);
            _ = portal.RefreshAsync();
        }
        _isLoading = false;

        if (state.IsCollapsed)
            ApplyCollapseState(true);

        WatchContentItems();
        Items.CollectionChanged += (_, _) =>
        {
            WatchContentItems();
            RaiseLayoutChanged();
            QueueContentSizing();
        };
        _suppressSliderEvent = false;
        UpdateExpansionControls();
        SystemParameters.StaticPropertyChanged += AccessibilitySettingsChanged;
        Closed += (_, _) =>
        {
            EndBoxSelection(cancel: true);
            _finishRollAnimation?.Invoke();
            ClearDropFeedback();
            _dropImage.Dispose();
            StopWatchingContentItems();
            SystemParameters.StaticPropertyChanged -= AccessibilitySettingsChanged;
        };
        IsVisibleChanged += (_, _) => { if (!IsVisible) { ClearDropFeedback(); EndBoxSelection(cancel: true); } };
    }

    public PicketState ToState() => new()
    {
        Id = PicketId,
        Title = TitleText.Text,
        X = Left, Y = Top,
        Width = Width,
        Height = _expandedHeight,
        AutoSizeRows = _autoSizeRows,
        IsCollapsed = _isCollapsed,
        GroupId = GroupId,
        GroupOrder = GroupOrder,
        GroupHorizontal = _groupHorizontal,
        AccordionMode = _accordionMode,
        ColorKey = _colorKey,
        TransparencyKey = _transparencyKey,
        TransparencyCustomPercent = _transparencyCustomPercent,
        BlurEnabled = _blurEnabled,
        Items = Items.Select(i => i.Kind == ItemKind.Label
                ? new ItemState
                {
                    Kind = ItemKind.Label,
                    LabelText = i.LabelText,
                }
                : new ItemState
                {
                    Kind = ItemKind.File,
                    Path = i.Path,
                    OriginalX = i.OriginalDesktopPos?.X,
                    OriginalY = i.OriginalDesktopPos?.Y,
                    IsLarge = i.IsLarge,
                    IsFolder = i.IsFolder,
                }).ToList()
    };

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        WorkerWHost.AttachToDesktop(hwnd);
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProcSnap);
        // Acrylic relies on hwnd, which doesn't exist during the constructor's first ApplyVisuals,
        // so run it again once the window is realized.
        ApplyBlur();
    }

    // === Snap-to-edge ===
    // Snapping happens during WM_MOVING (the system asks us to validate the proposed RECT in physical
    // screen coords). Mutating the RECT here avoids any visible jitter and is naturally DPI-correct.
    private const int SNAP_PIXELS    = 8;
    private const int UNSTICK_PIXELS = 12;  // per-axis travel before that axis stops attracting

    private IntPtr WndProcSnap(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WindowInterop.WM_MOVING) return IntPtr.Zero;
        var rect = Marshal.PtrToStructure<RECT>(lParam);
        if (TrySnap(hwnd, ref rect))
            Marshal.StructureToPtr(rect, lParam, false);
        handled = true;
        return new IntPtr(1);
    }

    private bool TrySnap(IntPtr selfHwnd, ref RECT r)
    {
        var monitor = WindowInterop.MonitorFromRect(ref r, WindowInterop.MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!WindowInterop.GetMonitorInfo(monitor, ref mi)) return false;
        var work = mi.rcWork;

        int bestDx = int.MaxValue, bestDy = int.MaxValue;
        int left = r.left, top = r.top;
        int width = r.right - r.left, height = r.bottom - r.top;

        // Once the user has pulled the window more than UNSTICK_PIXELS on a given axis from where the
        // drag started, stop attracting on that axis. Initial millimeters still snap (so brushing past
        // an edge feels magnetic), but committed diagonal movement isn't yanked back to a neighbor.
        bool snapXAllowed = Math.Abs(left - _dragStartScreenRect.left) <= UNSTICK_PIXELS;
        bool snapYAllowed = Math.Abs(top  - _dragStartScreenRect.top)  <= UNSTICK_PIXELS;

        void TryX(int candidateLeft)
        {
            if (!snapXAllowed) return;
            var d = candidateLeft - left;
            if (Math.Abs(d) < Math.Abs(bestDx)) bestDx = d;
        }
        void TryY(int candidateTop)
        {
            if (!snapYAllowed) return;
            var d = candidateTop - top;
            if (Math.Abs(d) < Math.Abs(bestDy)) bestDy = d;
        }

        // Work-area edges
        if (Math.Abs(r.left - work.left) <= SNAP_PIXELS)        TryX(work.left);
        if (Math.Abs(r.right - work.right) <= SNAP_PIXELS)      TryX(work.right - width);
        if (Math.Abs(r.top - work.top) <= SNAP_PIXELS)          TryY(work.top);
        if (Math.Abs(r.bottom - work.bottom) <= SNAP_PIXELS)    TryY(work.bottom - height);

        // Other pickets -- but only if they're actually near us on the perpendicular axis. Without
        // this guard a picket in the top-left can magnetize a picket in the bottom-right just because
        // their left edges share an X coordinate, which is the source of "phantom" stickiness.
        if (Application.Current is App app)
        {
            foreach (var other in app.Pickets)
            {
                if (!other.IsOnStackPage || other.GroupId == GroupId) continue;
                var oh = new WindowInteropHelper(other).Handle;
                if (oh == selfHwnd || oh == IntPtr.Zero) continue;
                if (!WindowInterop.GetWindowRect(oh, out var o)) continue;

                bool yClose = !(r.bottom + SNAP_PIXELS < o.top || o.bottom + SNAP_PIXELS < r.top);
                bool xClose = !(r.right  + SNAP_PIXELS < o.left || o.right + SNAP_PIXELS < r.left);

                if (yClose)
                {
                    if (Math.Abs(r.left - o.right) <= SNAP_PIXELS)   TryX(o.right);
                    if (Math.Abs(r.right - o.left) <= SNAP_PIXELS)   TryX(o.left - width);
                    if (Math.Abs(r.left - o.left) <= SNAP_PIXELS)    TryX(o.left);
                    if (Math.Abs(r.right - o.right) <= SNAP_PIXELS)  TryX(o.right - width);
                }
                if (xClose)
                {
                    if (Math.Abs(r.top - o.bottom) <= SNAP_PIXELS)   TryY(o.bottom);
                    if (Math.Abs(r.bottom - o.top) <= SNAP_PIXELS)   TryY(o.top - height);
                    if (Math.Abs(r.top - o.top) <= SNAP_PIXELS)      TryY(o.top);
                    if (Math.Abs(r.bottom - o.bottom) <= SNAP_PIXELS) TryY(o.bottom - height);
                }
            }
        }

        bool snapped = false;
        if (bestDx != int.MaxValue) { r.left += bestDx; r.right += bestDx; snapped = true; }
        if (bestDy != int.MaxValue) { r.top  += bestDy; r.bottom += bestDy; snapped = true; }
        return snapped;
    }

    /// <summary>When any picket in a touching cluster gets activated, raise every cluster member to
    /// the top so the whole magnetized group surfaces together. Followers are raised with
    /// SWP_NOACTIVATE so they don't fire their own Activated and infinite-loop.</summary>
    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        var cluster = ComputeTouchingCluster();
        if (cluster.Count <= 1) return;

        const uint flags = WindowInterop.SWP_NOMOVE | WindowInterop.SWP_NOSIZE | WindowInterop.SWP_NOACTIVATE;
        // Keep lower members above upper members at shared seams. Raising the clicked window last
        // made its bottom edge cover the first pixel of the title directly beneath it.
        foreach (var member in cluster.OrderBy(member => member.Top))
        {
            var h = new WindowInteropHelper(member).Handle;
            if (h != IntPtr.Zero)
                WindowInterop.SetWindowPos(h, WindowInterop.HWND_TOP, 0, 0, 0, 0, flags);
        }
    }

    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e);

        // If we're the leader of a group drag, translate every other cluster member by our delta.
        // Followers don't have _dragCluster set, so they just no-op past this and raise normally.
        if (_dragCluster != null)
        {
            var dx = Left - _dragLastLeft;
            var dy = Top - _dragLastTop;
            _dragLastLeft = Left;
            _dragLastTop = Top;
            if (dx != 0 || dy != 0)
            {
                foreach (var member in _dragCluster)
                {
                    if (member == this) continue;
                    member.Left += dx;
                    member.Top  += dy;
                }
            }
        }

        RaiseLayoutChanged();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        RaiseLayoutChanged();
        if (sizeInfo.WidthChanged) QueueContentSizing();
    }

    private void RaiseLayoutChanged()
    {
        if (_isLoading || _isRollAnimating) return;
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    // === Title click / drag ===
    private readonly TitleGesture _titleGesture = new();

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        TitleToggle.Focus();
        ClearSelection();
        _titleGesture.Begin(PointToScreen(e.GetPosition(this)), VisualTreeHelper.GetDpi(this),
            SystemParameters.MinimumHorizontalDragDistance, SystemParameters.MinimumVerticalDragDistance);
        if (!TitleShell.CaptureMouse()) _titleGesture.Cancel();
        e.Handled = true;
    }

    private void TitleBar_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_titleGesture.IsPending) return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            CancelTitleGesture();
            return;
        }
        if (!_titleGesture.Move(PointToScreen(e.GetPosition(this)))) return;
        TitleShell.ReleaseMouseCapture();
        // Only a deliberate drag settles the animation. A click can redirect it without a jump.
        SettleStack();
        BeginGroupDrag();
        try { DragMove(); }
        finally
        {
            _dragCluster = null;
            JoinTouchingGroups();
        }
        e.Handled = true;
    }

    private void TitleBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_titleGesture.IsPending) return;
        var click = _titleGesture.Release(PointToScreen(e.GetPosition(this)));
        TitleShell.ReleaseMouseCapture();
        if (click) ToggleCollapse();
        e.Handled = true;
    }

    private void TitleBar_LostMouseCapture(object sender, MouseEventArgs e) => _titleGesture.Cancel();

    private void CancelTitleGesture()
    {
        _titleGesture.Cancel();
        if (TitleShell.IsMouseCaptured) TitleShell.ReleaseMouseCapture();
    }

    private void BeginGroupDrag()
    {
        _dragLastLeft = Left;
        _dragLastTop  = Top;
        _dragCluster  = ComputeTouchingCluster();
        WindowInterop.GetWindowRect(new WindowInteropHelper(this).Handle, out _dragStartScreenRect);
    }


    // Pickets within this many px of each other (or overlapping) count as one group and drag
    // together. Looser than requiring flush edges, so a stacked column or an overlapping pile still
    // moves as a unit. Kept below the unlink push (SHIFT) so "unlink" can still break a group apart.
    private const double GROUP_GAP = 20.0;


    private static GroupOrientation GetSimpleGroupOrientation(IReadOnlyCollection<PicketWindow> cluster)
        => ConnectedGroupLayout.Orientation(cluster.Select(p => new Rect(p.Left, p.Top, p.Width, p.Height)).ToList());

    /// <summary>True when two pickets belong to the same drag group: they form a column (overlap
    /// horizontally, within GROUP_GAP vertically), a row (overlap vertically, within GROUP_GAP
    /// horizontally), or simply overlap. Pure diagonal-corner neighbors do NOT group.</summary>
    private static bool AreGrouped(PicketWindow a, PicketWindow b)
    {
        double aL = a.Left, aR = a.Left + a.Width, aT = a.Top, aB = a.Top + a.Height;
        double bL = b.Left, bR = b.Left + b.Width, bT = b.Top, bB = b.Top + b.Height;

        bool xOverlap = aL < bR && bL < aR;
        bool yOverlap = aT < bB && bT < aB;
        bool xNear    = aL < bR + GROUP_GAP && bL < aR + GROUP_GAP;
        bool yNear    = aT < bB + GROUP_GAP && bT < aB + GROUP_GAP;

        // Column (x overlaps, y close), row (y overlaps, x close), or overlapping pile.
        return (xOverlap && yNear) || (yOverlap && xNear);
    }

    // === Title context menu ===
    private void TitleMenu_Rename_Click(object sender, RoutedEventArgs e) => BeginRename();

    private void TitleMenu_NewPicket_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current is App app)
            app.CreatePicket(Left + 30, Top + 30, this);
    }

    private void AddPicketBtn_Click(object sender, RoutedEventArgs e)
    {
        // Mark handled so the title bar's drag-move doesn't pick this click up.
        e.Handled = true;
        if (Application.Current is App app)
            app.CreatePicket(Left + 30, Top + 30, this);
    }

    private void UnlinkBtn_Click(object sender, RoutedEventArgs e) => UnlinkGroup();

    /// <summary>Updates the unlink button's visibility based on whether this picket touches any neighbor.
    /// Called by App after any picket in the system moves.</summary>
    public void RefreshLinkState()
        => RefreshLinkState(ComputeTouchingCluster());

    internal void RefreshLinkState(IReadOnlyList<PicketWindow> group)
    {
        if (UnlinkBtn == null) return;  // pre-XAML-init guard
        UnlinkBtn.Visibility = group.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        RefreshStackChrome(group);
        RefreshResizeHandles(group);
    }

    /// <summary>Only expose resize handles on the outside of a connected group. Without this,
    /// the transparent bottom thumb of an upper picket can be dragged through the title of the
    /// picket below it, resizing several independent windows in place and making them overlap.</summary>
    private void RefreshResizeHandles(IReadOnlyList<PicketWindow> group)
    {
        if (ResizeRight == null || ResizeBottom == null || ResizeBottomRight == null) return;

        var cluster = group.Where(p => p.IsOnStackPage).ToList();
        if (cluster.Count == 0) return;
        var groupRight = cluster.Max(p => p.Left + p.Width);
        var groupBottom = cluster.Max(p => p.Top + p.Height);
        var onRightEdge = Math.Abs(Left + Width - groupRight) <= GROUP_GAP;
        var onBottomEdge = Math.Abs(Top + Height - groupBottom) <= GROUP_GAP;

        ResizeRight.IsHitTestVisible = onRightEdge;
        ResizeBottom.IsHitTestVisible = onBottomEdge;
        ResizeBottomRight.IsHitTestVisible = onRightEdge && onBottomEdge;
        ResizeHint.Visibility = onRightEdge && onBottomEdge ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Makes a vertical run of separate native windows read as one continuous component.
    /// Only the outside of the group is rounded; internal top borders become consistent dividers.</summary>
    private void RefreshStackChrome(IReadOnlyList<PicketWindow> group)
    {
        if (OuterShell == null || TitleShell == null) return;

        var aligned = group.Where(other => other != this && other.IsOnStackPage && HorizontallyOverlaps(other, this));
        var hasAbove = aligned.Any(other => other.Top < Top &&
            Math.Abs(other.Top + other.Height - Top) <= GROUP_GAP);
        var hasBelow = aligned.Any(other => other.Top > Top &&
            Math.Abs(Top + Height - other.Top) <= GROUP_GAP);

        OuterShell.CornerRadius = new CornerRadius(
            hasAbove ? 0 : 9, hasAbove ? 0 : 9,
            hasBelow ? 0 : 9, hasBelow ? 0 : 9);
        OuterShell.BorderThickness = new Thickness(1, 1, 1, hasBelow ? 0 : 1);

        var roundTitleBottom = _isCollapsed && !hasBelow;
        TitleShell.CornerRadius = new CornerRadius(
            hasAbove ? 0 : 9, hasAbove ? 0 : 9,
            roundTitleBottom ? 9 : 0, roundTitleBottom ? 9 : 0);
    }

    private void TitleMenu_DeletePicket_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current is App app && app.PicketCount <= 1)
        {
            MessageBox.Show("Can't delete the last picket -- create another one first.",
                "Pickets", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var result = MessageBox.Show("Delete this picket? Captured icons will be restored to the desktop.", "Pickets",
            MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (result != MessageBoxResult.OK) return;
        if (Application.Current is App app2) app2.DeletePicket(this);
    }

    private void TitleMenu_CleanMissing_Click(object sender, RoutedEventArgs e)
    {
        CheckReferences_Click(sender, e);
    }

    private void TitleMenu_RestoreAll_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current is App app) app.ReleaseAllCapturedIconsAndQuit();
    }

    private void TitleMenu_Quit_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current is App app) app.QuitAndRestoreIcons();
    }

    private void TitleMenu_ExitHidden_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current is App app) app.Shutdown();
    }

    private void TitleMenu_About_Click(object sender, RoutedEventArgs e)
    {
        App.ShowAbout();
    }

    private void TitleMenu_HideAll_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current is App app) app.ToggleAllPicketsVisibility();
    }

    private void TitleMenu_SetColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is string key)
            ApplyColorTheme(key);
    }

    private void TitleMenu_ApplyColorToAll_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current is App app)
            app.ApplyColorThemeToAll(_colorKey);
    }

    /// <summary>Applies one canonical theme to this Picket. Used by both individual selection and
    /// the app-wide action so every path gets identical normalization, repaint, and persistence.</summary>
    public void ApplyColorTheme(string key)
    {
        _colorKey = PicketColors.Get(key).Key;
        ApplyVisuals();
        RaiseLayoutChanged();
    }

    private void TitleMenu_SetTransparency_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is string key)
        {
            _transparencyKey = key;
            ApplyVisuals();
            RaiseLayoutChanged();
        }
    }

    /// <summary>Refreshes Color/Transparency submenu checkmarks and syncs the custom slider before display.</summary>
    private void TitleBar_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.ContextMenu is not ContextMenu cm) return;
        PrepareTitleMenu(cm);
    }

    private void MenuButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = MenuButton.ContextMenu;
        PrepareTitleMenu(menu);
        menu.PlacementTarget = MenuButton;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void PrepareTitleMenu(ContextMenu cm)
    {
        if (FindMenuItemByTag(cm.Items, "AutoHeight") is MenuItem autoHeight) autoHeight.IsChecked = _autoSizeRows;
        if (FindMenuItemByTag(cm.Items, "ManualHeight") is MenuItem manualHeight) manualHeight.IsChecked = !_autoSizeRows;
        if (FindMenuItemByTag(cm.Items, "Undo") is MenuItem undo)
        {
            var description = (Application.Current as App)?.UndoHistory.Description;
            undo.IsEnabled = description != null;
            undo.Header = description == null ? "Undo" : "Undo: " + description;
        }
        var group = ComputeTouchingCluster();
        var position = group.IndexOf(this);
        if (FindMenuItemByTag(cm.Items, "StackMenu") is MenuItem stack)
        {
            stack.IsEnabled = group.Count > 1;
            stack.Header = _stackPageCount > 1 ? $"Stack (page {_stackPageIndex + 1} of {_stackPageCount})" : "Stack";
        }
        if (FindMenuItemByTag(cm.Items, "Earlier") is MenuItem earlier) earlier.IsEnabled = position > 0;
        if (FindMenuItemByTag(cm.Items, "Later") is MenuItem later) later.IsEnabled = position + 1 < group.Count;
        if (FindMenuItemByTag(cm.Items, "PreviousPage") is MenuItem previous) previous.IsEnabled = _stackPageIndex > 0;
        if (FindMenuItemByTag(cm.Items, "NextPage") is MenuItem next) next.IsEnabled = _stackPageIndex + 1 < _stackPageCount;
        RefreshSubmenuChecks(cm, "ColorMenu", _colorKey);
        RefreshSubmenuChecks(cm, "TransparencyMenu", _transparencyKey);
        RefreshBlurCheck(cm);
        RefreshLaunchAtLoginCheck(cm);
        var accordion = FindMenuItemByTag(cm.Items, "Accordion");
        if (accordion != null) accordion.IsChecked = _accordionMode;

        // Slider always reflects effective %, so dragging from any preset feels continuous.
        var (slider, label) = FindCustomSliderAndLabel(cm);
        if (slider != null)
        {
            _suppressSliderEvent = true;
            slider.Value = CurrentTransparencyPercent;
            _suppressSliderEvent = false;
        }
        if (label != null) label.Text = $"Custom: {CurrentTransparencyPercent}%";
    }

    private static void RefreshSubmenuChecks(ContextMenu cm, string parentTag, string selectedKey)
    {
        var parent = FindMenuItemByTag(cm.Items, parentTag);
        if (parent == null) return;
        foreach (var item in parent.Items.OfType<MenuItem>())
            item.IsChecked = item.Tag is string key && key == selectedKey;
    }

    private static (Slider? slider, TextBlock? label) FindCustomSliderAndLabel(ContextMenu cm)
    {
        var transparencyMenu = FindMenuItemByTag(cm.Items, "TransparencyMenu");
        var customItem = transparencyMenu?.Items.OfType<MenuItem>().FirstOrDefault(mi => "custom".Equals(mi.Tag));
        if (customItem?.Header is not StackPanel sp) return (null, null);
        var slider = sp.Children.OfType<Slider>().FirstOrDefault(s => "CustomSlider".Equals(s.Tag));
        var label  = sp.Children.OfType<TextBlock>().FirstOrDefault(t => "CustomLabel".Equals(t.Tag));
        return (slider, label);
    }

    private static MenuItem? FindMenuItemByTag(ItemCollection items, string tag)
    {
        foreach (var item in items.OfType<MenuItem>())
        {
            if (tag.Equals(item.Tag)) return item;
            var nested = FindMenuItemByTag(item.Items, tag);
            if (nested != null) return nested;
        }
        return null;
    }

    private void TransparencyCustomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressSliderEvent) return;
        _transparencyKey = "custom";
        _transparencyCustomPercent = (int)Math.Round(e.NewValue);

        // Live-update the label sibling so the user sees the number while dragging.
        if (sender is Slider s && s.Parent is StackPanel sp)
        {
            var label = sp.Children.OfType<TextBlock>().FirstOrDefault(t => "CustomLabel".Equals(t.Tag));
            if (label != null) label.Text = $"Custom: {_transparencyCustomPercent}%";
        }

        ApplyVisuals();
        RaiseLayoutChanged();
    }

    private void ApplyVisuals()
    {
        if (OuterShell == null) return; // can fire during XAML initialization
        if (SystemParameters.HighContrast) { ApplyHighContrastVisuals(); return; }
        var scheme = PicketColors.Get(_colorKey);
        var factor = CurrentTransparencyPercent / 100.0;

        // Acrylic supplies its own tint; use only a faint light/shadow overlay over it.
        // Its minimum alpha stays nonzero so native mouse and Explorer drops still hit the body.
        var bg = SurfaceGradient(scheme.Background, factor, _blurEnabled);
        // Transparency applies fully to the content canvas, but navigation chrome keeps enough
        // surface behind its contrast-selected text to remain readable on any wallpaper.
        var chromeFactor = Math.Max(0.72, factor);
        var titleB   = SurfaceGradient(scheme.TitleBackground, chromeFactor);
        var border   = FrozenBrush(ScaleAlpha(scheme.Border, Math.Max(0.55, factor)));
        var titleFg  = FrozenBrush(scheme.TitleForeground); // text always opaque
        var itemFg   = FrozenBrush(scheme.ItemForeground);

        // Interactive states use the theme accent, tying selection, hover, section labels, and the
        // rename field into the same visual system as the title/body surfaces.
        var controlHover = FrozenBrush(Color.FromArgb(0x32, scheme.Accent.R,
            scheme.Accent.G, scheme.Accent.B));
        var itemHover = FrozenBrush(Color.FromArgb(0x20, scheme.Accent.R,
            scheme.Accent.G, scheme.Accent.B));
        var sectionBg = FrozenBrush(Color.FromArgb(0x26, scheme.Accent.R,
            scheme.Accent.G, scheme.Accent.B));
        var editorBg = FrozenBrush(Color.FromArgb(0x24, scheme.TitleForeground.R,
            scheme.TitleForeground.G, scheme.TitleForeground.B));
        var editorBorder = FrozenBrush(Color.FromArgb(0x99, scheme.Accent.R,
            scheme.Accent.G, scheme.Accent.B));
        var selectionBg = FrozenBrush(Color.FromArgb(0x48, scheme.Accent.R,
            scheme.Accent.G, scheme.Accent.B));
        var selectionBorder = FrozenBrush(Color.FromArgb(0xC0, scheme.Accent.R,
            scheme.Accent.G, scheme.Accent.B));

        OuterShell.Background = bg;
        OuterShell.BorderBrush = border;
        TitleShell.Background = titleB;
        TitleText.Foreground = titleFg;
        TitleEditBox.Foreground = titleFg;
        TitleEditBox.CaretBrush = titleFg;
        Resources["PicketTitleForeground"] = titleFg;
        Resources["PicketItemForeground"] = itemFg;
        Resources["PicketControlHoverBrush"] = controlHover;
        Resources["PicketItemHoverBrush"] = itemHover;
        Resources["PicketSectionBackground"] = sectionBg;
        Resources["PicketEditorBackground"] = editorBg;
        Resources["PicketEditorBorder"] = editorBorder;
        Resources["PicketSelectionBackground"] = selectionBg;
        Resources["PicketSelectionBrush"] = selectionBg;
        Resources["PicketSelectionForeground"] = itemFg;
        Resources["PicketFocusBorder"] = titleFg;
        Resources["PicketSelectionBorder"] = selectionBorder;
        // Label halo is a Color (not a Brush) because DropShadowEffect.Color takes a Color DP.
        Resources["PicketTitleShadowColor"] = scheme.TitleShadow;
        Resources["PicketItemShadowColor"] = scheme.ItemShadow;

        ApplyBlur();
    }

    private void ApplyBlur()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return; // hasn't been realized yet; OnSourceInitialized will call us
        var scheme = PicketColors.Get(_colorKey);
        var factor = CurrentTransparencyPercent / 100.0;
        // Tint alpha is scaled down further so the blur is visibly doing work rather than being
        // masked by a near-opaque tint.
        var tint = ScaleAlpha(scheme.Background, Math.Min(1.0, factor * 0.7));
        WindowBlur.Apply(hwnd, _blurEnabled && !SystemParameters.HighContrast, tint);
    }

    private static Color ScaleAlpha(Color c, double factor)
    {
        var a = (byte)Math.Clamp((int)Math.Round(c.A * factor), 0, 255);
        return Color.FromArgb(a, c.R, c.G, c.B);
    }

    private static SolidColorBrush FrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static LinearGradientBrush SurfaceGradient(Color color, double opacity, bool acrylic = false)
    {
        static Color Shade(Color source, double amount)
        {
            byte Channel(byte value) => (byte)Math.Round(amount >= 0
                ? value + (255 - value) * amount : value * (1 + amount));
            return Color.FromArgb(source.A, Channel(source.R), Channel(source.G), Channel(source.B));
        }
        var stops = acrylic
            ? new GradientStopCollection
            {
                new(Color.FromArgb((byte)Math.Max(1, Math.Round(14 * opacity)), 255, 255, 255), 0),
                new(Color.FromArgb(1, 0, 0, 0), 0.45),
                new(Color.FromArgb((byte)Math.Max(1, Math.Round(10 * opacity)), 0, 0, 0), 1)
            }
            : new GradientStopCollection
            {
                new(ScaleAlpha(Shade(color, 0.06), opacity), 0),
                new(ScaleAlpha(color, opacity), 0.45),
                new(ScaleAlpha(Shade(color, -0.04), opacity), 1)
            };
        var brush = new LinearGradientBrush(stops, new Point(0, 0), new Point(1, 1));
        brush.Freeze();
        return brush;
    }


    // === Inline rename ===
    private void BeginRename()
    {
        TitleEditBox.Text = TitleText.Text;
        TitleEditBox.Visibility = Visibility.Visible;
        TitleText.Visibility = Visibility.Collapsed;
        TitleEditBox.Focus();
        TitleEditBox.SelectAll();
    }

    private void CommitRename()
    {
        if (TitleEditBox.Visibility != Visibility.Visible) return;
        var newName = TitleEditBox.Text.Trim();
        if (!string.IsNullOrEmpty(newName)) TitleText.Text = newName;
        UpdateAccessibleTitle();
        HideRenameBox();
        RaiseLayoutChanged();
    }

    private void CancelRename() => HideRenameBox();

    private void HideRenameBox()
    {
        TitleEditBox.Visibility = Visibility.Collapsed;
        TitleText.Visibility = Visibility.Visible;
        // Keep focus on a visible control after editing, for both keyboard navigation and OLE drops.
        TitleToggle.Focus();
    }

    private void TitleEditBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { CommitRename(); e.Handled = true; }
        else if (e.Key == Key.Escape) { CancelRename(); e.Handled = true; }
    }

    private void TitleEditBox_LostFocus(object sender, RoutedEventArgs e) => CommitRename();

    // === Roll-up ===
    private const double CollapsedHeight = 32;
    private static readonly TimeSpan RollAnimationDuration = TimeSpan.FromMilliseconds(220);

    private void ToggleCollapse()
    {
        SetExpanded(IntendedCollapsed);
    }

    private void ApplyCollapseState(bool collapsed, bool animate = false)
    {
        if (collapsed == IntendedCollapsed) return;

        if (animate)
        {
            AnimateCollapseState(collapsed);
            return;
        }

        SettleStack();

        if (collapsed)
        {
            BodyScroll.Visibility = Visibility.Collapsed;
            Height = CollapsedHeight;
            _isCollapsed = true;
        }
        else
        {
            BodyScroll.Visibility = Visibility.Visible;
            Height = _expandedHeight;
            _isCollapsed = false;
        }
        UpdateExpansionControls();
        RaiseLayoutChanged();
    }

    /// <summary>
    /// Rolls this picket open/closed while keeping the title bars beneath it attached to its bottom
    /// edge. Previously only this window changed height, so its body covered the rest of a stacked
    /// group. The whole transition is driven together so no overlap appears between animation frames.
    /// </summary>
    private void AnimateCollapseState(bool collapsed) => AnimateStack(collapsed);

    private static bool HorizontallyOverlaps(PicketWindow a, PicketWindow b)
        => a.Left < b.Left + b.Width && b.Left < a.Left + a.Width;

    private bool TryGetWorkAreaVertical(out double top, out double bottom)
    {
        top = bottom = 0;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !WindowInterop.GetWindowRect(hwnd, out var rect)) return false;

        var monitor = WindowInterop.MonitorFromRect(ref rect, WindowInterop.MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!WindowInterop.GetMonitorInfo(monitor, ref info)) return false;

        // PointFromScreen performs the physical-pixel to WPF-DIP conversion for this monitor.
        var localTop = PointFromScreen(new Point(info.rcWork.left, info.rcWork.top));
        var localBottom = PointFromScreen(new Point(info.rcWork.right, info.rcWork.bottom));
        top = Top + localTop.Y;
        bottom = Top + localBottom.Y;
        return bottom > top;
    }

    // === Resize thumbs ===
    // A connected cluster resizes as a unit. Reflowing after every delta keeps
    // adjacent members flush instead of allowing a height/width change to create gaps or overlaps.
    private List<PicketWindow>? _resizeCluster;
    private GroupOrientation _resizeOrientation;

    private void Resize_DragStarted(object sender, DragStartedEventArgs e)
    {
        if (_isRollAnimating) return;
        NormalizeConnectedGroup();
        _resizeCluster = ComputeTouchingCluster();
        _resizeOrientation = _groupHorizontal ? GroupOrientation.Row : GroupOrientation.Column;
    }

    private void Resize_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        _resizeCluster = null;
        RaiseLayoutChanged();
        QueueContentSizing();
    }

    private void ResizeCluster(double horizontalChange, double verticalChange)
    {
        if (_resizeCluster == null || _resizeCluster.Count == 0) return;
        var widthDivisor = _resizeOrientation == GroupOrientation.Row
            ? Math.Max(1, _resizeCluster.Count(p => p.IsOnStackPage)) : 1;
        var heightDivisor = _resizeOrientation == GroupOrientation.Column
            ? Math.Max(1, _resizeCluster.Count(p => p.IsOnStackPage && !p._isCollapsed)) : 1;
        var width = Math.Max(Width + horizontalChange / widthDivisor, _resizeCluster.Max(p => p.MinWidth));
        var height = Math.Max(_expandedHeight + verticalChange / heightDivisor, 96);
        foreach (var p in _resizeCluster)
        {
            if (verticalChange != 0) p._autoSizeRows = false;
            p._expandedHeight = p._autoSizeRows ? p.MeasureContentHeight(width) : height;
        }
        ApplyGroupBounds(OrderResizeCluster(), width, height);
    }

    private List<PicketWindow> OrderResizeCluster()
        => _resizeCluster == null
            ? new List<PicketWindow>()
            : _resizeCluster.OrderBy(p => p.GroupOrder).ToList();

    private void ResizeRight_DragDelta(object sender, DragDeltaEventArgs e)
        => ResizeCluster(e.HorizontalChange, 0);

    private void ResizeBottom_DragDelta(object sender, DragDeltaEventArgs e)
    {
        ResizeCluster(0, e.VerticalChange);
    }

    private void ResizeBottomRight_DragDelta(object sender, DragDeltaEventArgs e)
    {
        ResizeCluster(e.HorizontalChange, e.VerticalChange);
    }

    // === Drag-drop into the picket body ===
    private const string PicketItemDragFormat = "Pickets.PicketItemDrag";
    private sealed record PicketItemDragPayload(PicketWindow Source, PicketItem Item)
    {
        public PicketItem[] Items { get; init; } = [Item];
    }

    private readonly ShellDragImage _dropImage = new();
    private string? _lastDropDecision;

    private DragDropEffects GetDropEffect(DragEventArgs e)
    {
        var position = e.GetPosition(BodyScroll);
        var overBody = BodyScroll.IsVisible && position.X >= 0 && position.Y >= 0 &&
            position.X < BodyScroll.ActualWidth && position.Y < BodyScroll.ActualHeight;
        if (e.Data.GetDataPresent(PicketItemDragFormat) && e.Data.GetData(PicketItemDragFormat) is PicketItemDragPayload payload &&
            (payload.Source == this || !payload.Items.Any(item => payload.Source.Items.Contains(item) &&
                (item.Kind == ItemKind.Label || !Items.Any(i => i.Kind == ItemKind.File &&
                    string.Equals(i.Path, item.Path, StringComparison.OrdinalIgnoreCase)))))) return DragDropEffects.None;
        return DragPreview.DropEffect(overBody, e.Data.GetDataPresent(PicketItemDragFormat),
            e.Data.GetDataPresent(DataFormats.FileDrop), e.AllowedEffects);
    }

    private void Picket_DragOver(object sender, DragEventArgs e)
    {
        if (e.RoutedEvent == DragDrop.PreviewDragEnterEvent) _dropImage.Leave();
        if (Application.Current is App app)
        {
            foreach (var picket in app.Pickets)
                if (picket != this) picket.ClearDropFeedback();
            app.Frames.AddAnimation(CheckDropFeedback);
        }
        e.Effects = GetDropEffect(e);
        DropTargetOutline.Visibility = e.Effects == DragDropEffects.None ? Visibility.Collapsed : Visibility.Visible;
        LogDropDecision(e, "hover");
        if (WindowInterop.GetCursorPos(out var cursor))
            _dropImage.Over(new WindowInteropHelper(this).Handle, e.Data, cursor, e.Effects);
        e.Handled = true;
    }

    private void Picket_DragLeave(object sender, DragEventArgs e)
    {
        ClearDropFeedback();
    }

    private void ClearDropFeedback()
    {
        DropTargetOutline.Visibility = Visibility.Collapsed;
        _dropImage.Leave();
        _lastDropDecision = null;
        if (Application.Current is App app) app.Frames.RemoveAnimation(CheckDropFeedback);
    }

    private void CheckDropFeedback(TimeSpan time)
    {
        // OLE can send DragLeave to an item removed during a transfer. Its old window no
        // longer receives that routed event, so retire the visual/native session explicitly.
        if ((WindowInterop.GetAsyncKeyState(0x01) & 0x8000) == 0 ||
            !WindowInterop.GetCursorPos(out var cursor)) { ClearDropFeedback(); return; }
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !WindowInterop.GetWindowRect(hwnd, out var bounds) ||
            cursor.X < bounds.left || cursor.Y < bounds.top || cursor.X >= bounds.right || cursor.Y >= bounds.bottom)
            ClearDropFeedback();
    }

    private void Picket_PreviewDrop(object sender, DragEventArgs e)
    {
        e.Effects = GetDropEffect(e);
        LogDropDecision(e, "drop");
        if (WindowInterop.GetCursorPos(out var cursor)) _dropImage.Drop(e.Data, cursor, e.Effects);
        else _dropImage.Leave();
        ClearDropFeedback();
        // Complete a valid drop here, before item controls or a reflow can change the route.
        if (e.Effects != DragDropEffects.None) ItemsHost_Drop(sender, e);
        e.Handled = true;
    }

    private void LogDropDecision(DragEventArgs e, string phase)
    {
        var position = e.GetPosition(BodyScroll);
        var hit = (e.OriginalSource as DependencyObject)?.GetType().Name ?? "unknown";
        var internalItem = e.Data.GetDataPresent(PicketItemDragFormat);
        var files = e.Data.GetDataPresent(DataFormats.FileDrop);
        var decision = $"{phase}/{e.Effects}/{e.AllowedEffects}/{internalItem}/{files}/{hit}/{BodyScroll.IsVisible}";
        if (_lastDropDecision == decision) return;
        _lastDropDecision = decision;
        Logger.Log($"Drop decision: picket={PicketId}, phase={phase}, effect={e.Effects}, allowed={e.AllowedEffects}, " +
            $"internal={internalItem}, files={files}, hit={hit}, bodyVisible={BodyScroll.IsVisible}, " +
            $"point=({position.X:F1},{position.Y:F1}), body=({BodyScroll.ActualWidth:F1},{BodyScroll.ActualHeight:F1}).");
    }

    private void ItemsHost_Drop(object sender, DragEventArgs e)
    {
        Logger.Log($"ItemsHost_Drop fired. blur={_blurEnabled} " +
                   $"hasFileDrop={e.Data.GetDataPresent(DataFormats.FileDrop)}");
        // An in-process transfer keeps the captured desktop position with the item. Treating it as
        // a new FileDrop would lose that ownership metadata and leave a duplicate in the source.
        var internalPayload = e.Data.GetDataPresent(PicketItemDragFormat)
            ? e.Data.GetData(PicketItemDragFormat) as PicketItemDragPayload
            : null;
        if (internalPayload is PicketItemDragPayload payload)
        {
            var moved = payload.Source.TransferReferences(payload.Items, this);
            foreach (var item in moved) item.IsSelected = true;
            e.Effects = moved.Length > 0 ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var paths = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? (string[])e.Data.GetData(DataFormats.FileDrop)!
            : internalPayload != null
                ? new[] { internalPayload.Item.Path }
                : Array.Empty<string>();
        if (paths.Length == 0) { e.Handled = true; return; }

        AddReferences(paths, captureDesktop: true);
        e.Handled = true;
    }

    // === Item interactions ===
    private Point? _itemDragStart;
    private FrameworkElement? _itemDragSource;
    private PicketItem? _itemDragItem;
    private bool _deferItemSelection;

    private void Item_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not PicketItem item) return;

        if (e.ClickCount == 2)
        {
            ResetItemDragCandidate();
            LaunchItem(item);
            e.Handled = true;
            return;
        }

        _itemDragStart = e.GetPosition(this);
        _itemDragSource = fe;
        _itemDragItem = item;
        _deferItemSelection = item.IsSelected && ItemsHost.SelectedItems.Count > 1 && Keyboard.Modifiers == ModifierKeys.None;
        if (_deferItemSelection) e.Handled = true; // Keep the whole selection until click versus drag is known.
    }

    private void Item_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _itemDragStart == null ||
            _itemDragSource == null || _itemDragItem == null) return;

        var current = e.GetPosition(this);
        var start = _itemDragStart.Value;
        if (Math.Abs(current.X - start.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var source = _itemDragSource;
        var item = _itemDragItem;
        var dragged = ActionItems(item);
        ResetItemDragCandidate();
        // ListBox captures the mouse for selection. OLE owns it during a reference drag.
        if (IsMouseCaptureWithin) Mouse.Capture(null);

        var data = new DataObject();
        data.SetData(PicketItemDragFormat, new PicketItemDragPayload(this, item) { Items = dragged });
        // Captured desktop items already live in the Desktop folder. Advertising FileDrop makes
        // Explorer attempt a same-folder move and display "source and destination are the same".
        // Keep all reference drags private: never ask Explorer to copy or move the original.

        var canceled = false;
        var dropResult = DragDropEffects.None;
        QueryContinueDragEventHandler cancelTracker = (_, args) =>
        {
            if (args.EscapePressed) canceled = true;
        };

        source.QueryContinueDrag += cancelTracker;
        try
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            var preview = DragPreview.Render(item, dpi, dragged.Length);
            using var dragImage = ShellDragImage.CreateSource(data, preview,
                new POINT(preview.PixelWidth / 2, (int)(item.IconSize * dpi.DpiScaleY / 2)));
            dropResult = DragDrop.DoDragDrop(source, data,
                DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
        }
        finally
        {
            source.QueryContinueDrag -= cancelTracker;
            if (Application.Current is App app)
                foreach (var picket in app.Pickets) picket.ClearDropFeedback();
        }

        // A drop onto the desktop can report None because the filesystem item already lives there.
        // Cursor location, plus explicit Escape tracking, tells that valid gesture from cancellation.
        // An accepted partial transfer may leave duplicate references in the source. Never
        // release those to the desktop if automatic sizing moved the target away from the cursor.
        if (canceled || dropResult != DragDropEffects.None ||
            !WindowInterop.GetCursorPos(out var cursor) || IsPointInsideAnyPicket(cursor)) return;
        var released = new List<RemovedReference>();
        var candidates = SnapshotReferences(dragged.Where(i => i.OriginalDesktopPos.HasValue));
        foreach (var saved in candidates)
        {
            // Restore a batch to its known desktop positions rather than scattering later items
            // beyond a monitor edge. A single icon still follows the drop point.
            var dropPosition = candidates.Length > 1 ? saved.Position!.Value : new POINT(cursor.X - 32, cursor.Y - 32);
            if (!DesktopIconHider.Restore(saved.Item.Path, dropPosition) &&
                !DesktopIconHider.Restore(saved.Item.Path, saved.Position!.Value)) continue;
            saved.Item.OriginalDesktopPos = null;
            Items.Remove(saved.Item);
            released.Add(saved);
        }
        RecordReferenceRemoval(released.ToArray());
        e.Handled = true;
    }

    private void Item_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_deferItemSelection && _itemDragItem != null) ItemsHost.SelectedItem = _itemDragItem;
        ResetItemDragCandidate();
    }

    private void ResetItemDragCandidate()
    {
        _itemDragStart = null;
        _itemDragSource = null;
        _itemDragItem = null;
        _deferItemSelection = false;
    }

    private static bool IsPointInsideAnyPicket(POINT point)
    {
        if (Application.Current is not App app) return false;
        foreach (var picket in app.Pickets)
        {
            if (!picket.IsVisible || !picket.IsOnStackPage) continue;
            var hwnd = new WindowInteropHelper(picket).Handle;
            if (hwnd == IntPtr.Zero || !WindowInterop.GetWindowRect(hwnd, out var rect)) continue;
            if (point.X >= rect.left && point.X < rect.right &&
                point.Y >= rect.top && point.Y < rect.bottom) return true;
        }
        return false;
    }

    private void ClearSelection(PicketItem? except = null)
    {
        foreach (var i in Items)
            if (i.IsSelected && !ReferenceEquals(i, except))
                i.IsSelected = false;
    }

    private void Item_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.ContextMenu is ContextMenu cm
            && fe.DataContext is PicketItem item)
        {
            if (!item.IsSelected) ItemsHost.SelectedItem = item;
            var selected = ActionItems(item);
            var files = selected.Where(i => i.Kind == ItemKind.File).ToArray();
            foreach (var entry in cm.Items.OfType<MenuItem>())
            {
                entry.Visibility = entry.Tag switch
                {
                    "Open" or "Check" or "LargeToggle" => files.Length > 0 ? Visibility.Visible : Visibility.Collapsed,
                    "Locate" or "Location" => selected.Length == 1 && files.Length == 1 ? Visibility.Visible : Visibility.Collapsed,
                    "InsertLabel" => selected.Length == 1 ? Visibility.Visible : Visibility.Collapsed,
                    "RenameLabel" => selected.Length == 1 && files.Length == 0 ? Visibility.Visible : Visibility.Collapsed,
                    _ => Visibility.Visible
                };
                if (Equals(entry.Tag, "Open")) entry.Header = files.Length > 1 ? $"Open {files.Length} references" : "Open";
                if (Equals(entry.Tag, "Remove")) entry.Header = selected.Length > 1 ? $"Remove {selected.Length} items from picket" : "Remove from picket";
                if (Equals(entry.Tag, "MoveReferenceTo")) entry.Header = selected.Length > 1 ? $"Move {selected.Length} items to" : "Move reference to";
            }
            var largeItem = cm.Items.OfType<MenuItem>()
                .FirstOrDefault(mi => "LargeToggle".Equals(mi.Tag));
            if (largeItem != null) largeItem.IsChecked = files.Length > 0 && files.All(i => i.IsLarge);
            var destinations = cm.Items.OfType<MenuItem>().FirstOrDefault(mi => "MoveReferenceTo".Equals(mi.Tag));
            if (destinations != null && Application.Current is App app)
            {
                destinations.Items.Clear();
                foreach (var target in app.Pickets.Where(p => p != this))
                {
                    var entry = new MenuItem { Header = target.ToState().Title,
                        IsEnabled = selected.Any(source => source.Kind == ItemKind.Label || !target.Items.Any(i => i.Kind == ItemKind.File &&
                            string.Equals(i.Path, source.Path, StringComparison.OrdinalIgnoreCase))) };
                    entry.Click += (_, _) => MoveReferencesTo(selected, target);
                    destinations.Items.Add(entry);
                }
                destinations.IsEnabled = destinations.Items.Count > 0;
            }
        }
    }

    private void ItemMenu_Open_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is PicketItem item)
            foreach (var selected in ActionItems(item)) LaunchItem(selected);
    }

    private void ItemMenu_OpenLocation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is PicketItem item)
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{item.Path}\"")
                { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Pickets", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void ItemMenu_ToggleLarge_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is PicketItem item)
        {
            SetIconSizes(ActionItems(item), mi.IsChecked);
        }
    }

    private void ItemMenu_Remove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is PicketItem item)
        {
            RemoveReferences(ActionItems(item));
        }
    }


    // File.Exists returns false for directories, which would flag every dropped folder as missing.
    // === Section labels ===
    private void TitleMenu_AddLabel_Click(object sender, RoutedEventArgs e)
    {
        var text = InputDialog.Show(this, "Section label", "Label text:", "Section");
        if (string.IsNullOrWhiteSpace(text)) return;
        var label = PicketItem.CreateLabel(text.Trim());
        Items.Add(label);
        RecordReferenceAddition([label]);
    }

    private void ItemMenu_InsertLabelAbove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi || mi.DataContext is not PicketItem clicked) return;
        var text = InputDialog.Show(this, "Section label", "Label text:", "Section");
        if (string.IsNullOrWhiteSpace(text)) return;
        var idx = Items.IndexOf(clicked);
        if (idx < 0) idx = Items.Count;
        var label = PicketItem.CreateLabel(text.Trim());
        Items.Insert(idx, label);
        RecordReferenceAddition([label]);
    }

    private void LabelMenu_Rename_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi || mi.DataContext is not PicketItem label) return;
        if (label.Kind != ItemKind.Label) return;
        var text = InputDialog.Show(this, "Rename label", "Label text:", label.LabelText);
        if (string.IsNullOrWhiteSpace(text)) return;
        label.LabelText = text.Trim();
        RaiseLayoutChanged();
    }

    private void LabelMenu_Remove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi || mi.DataContext is not PicketItem label) return;
        if (label.Kind != ItemKind.Label) return;
        Items.Remove(label);
    }

    // === Blur toggle ===
    private void TitleMenu_ToggleBlur_Click(object sender, RoutedEventArgs e)
    {
        _blurEnabled = !_blurEnabled;
        ApplyVisuals(); // re-applies brushes and calls ApplyBlur
        RaiseLayoutChanged();
    }

    private void RefreshBlurCheck(ContextMenu cm)
    {
        var item = FindMenuItemByTag(cm.Items, "BlurToggle");
        if (item != null) item.IsChecked = _blurEnabled;
    }

    // === Launch at login ===
    private void TitleMenu_ToggleLaunchAtLogin_Click(object sender, RoutedEventArgs e)
    {
        if (StartupEntry.IsEnabled) StartupEntry.Disable();
        else                        StartupEntry.Enable();
        // No need to mark layout dirty -- the registry entry is system state, not picket state.
    }

    private static void RefreshLaunchAtLoginCheck(ContextMenu cm)
    {
        var item = FindMenuItemByTag(cm.Items, "LaunchAtLogin");
        if (item != null) item.IsChecked = StartupEntry.IsEnabled;
    }
}
