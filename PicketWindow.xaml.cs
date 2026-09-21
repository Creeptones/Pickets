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
using System.Windows.Threading;

namespace Pickets;

public partial class PicketWindow : Window
{
    public string PicketId { get; }
    public ObservableCollection<PicketItem> Items { get; } = new();

    /// <summary>Raised whenever the picket's persistent state changes (move, resize, item add/remove).</summary>
    public event EventHandler? LayoutChanged;

    private bool _isLoading;
    private bool _isCollapsed;
    private double _expandedHeight;
    private bool _isRollAnimating;
    private DispatcherTimer? _rollAnimationTimer;
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
        TitleText.Text = state.Title;
        Left = state.X; Top = state.Y; Width = state.Width; Height = state.Height;
        ItemsHost.ItemsSource = Items;
        _expandedHeight = state.Height;

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
            var fi = PicketItem.FromPath(i.Path);
            if (i.HasOriginalPos)
                fi.OriginalDesktopPos = new POINT(i.OriginalX!.Value, i.OriginalY!.Value);
            fi.IsLarge = i.IsLarge;
            fi.IsMissing = !PathExists(i.Path);
            Items.Add(fi);

            // Re-apply offscreen position on launch (Explorer may have rearranged).
            if (i.HasOriginalPos && !fi.IsMissing)
                DesktopIconHider.ReapplyHidden(i.Path);
        }

        // One-time compatibility path: old folder portals did not persist their mirrored children.
        // Preserve the user's intent as a normal folder icon, then ToState drops PortalPath forever.
        if (!string.IsNullOrEmpty(state.PortalPath) && Directory.Exists(state.PortalPath) &&
            !Items.Any(i => string.Equals(i.Path, state.PortalPath, StringComparison.OrdinalIgnoreCase)))
            Items.Add(PicketItem.FromPath(state.PortalPath));
        _isLoading = false;

        if (state.IsCollapsed)
            ApplyCollapseState(true);

        Items.CollectionChanged += (_, _) => RaiseLayoutChanged();
        _suppressSliderEvent = false;
    }

    public PicketState ToState() => new()
    {
        Id = PicketId,
        Title = TitleText.Text,
        X = Left, Y = Top,
        Width = Width,
        Height = _isCollapsed ? _expandedHeight : Height,
        IsCollapsed = _isCollapsed,
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
    }

    private void RaiseLayoutChanged()
    {
        if (_isLoading) return;
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    // === Title bar drag + double-click to roll up ===
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // The first click already toggles on release. Ignore the second half of a double-click so
        // the fence does not immediately toggle back to its starting state.
        if (e.ClickCount > 1) { e.Handled = true; return; }
        if (e.ClickCount == 1 && e.LeftButton == MouseButtonState.Pressed)
        {
            ClearSelection();
            var startLeft = Left;
            var startTop = Top;
            BeginGroupDrag();
            try { DragMove(); }                   // blocks until the user releases
            finally { _dragCluster = null; }

            // A title is both the drag handle and the accordion trigger. Treat a release without
            // meaningful movement as a click; actual drags retain the existing group-move behavior.
            if (Math.Abs(Left - startLeft) < 2 && Math.Abs(Top - startTop) < 2)
                ToggleCollapse();
            e.Handled = true;
        }
    }

    private void BeginGroupDrag()
    {
        _dragLastLeft = Left;
        _dragLastTop  = Top;
        _dragCluster  = ComputeTouchingCluster();
        WindowInterop.GetWindowRect(new WindowInteropHelper(this).Handle, out _dragStartScreenRect);
    }

    private List<PicketWindow> ComputeTouchingCluster()
    {
        var cluster = new List<PicketWindow> { this };
        if (Application.Current is not App app) return cluster;
        var pool = app.Pickets.ToList();

        var queue = new Queue<PicketWindow>();
        queue.Enqueue(this);
        while (queue.Count > 0)
        {
            var f = queue.Dequeue();
            foreach (var g in pool)
            {
                if (cluster.Contains(g)) continue;
                if (AreGrouped(f, g))
                {
                    cluster.Add(g);
                    queue.Enqueue(g);
                }
            }
        }
        return cluster;
    }

    // Pickets within this many px of each other (or overlapping) count as one group and drag
    // together. Looser than requiring flush edges, so a stacked column or an overlapping pile still
    // moves as a unit. Kept below the unlink push (SHIFT) so "unlink" can still break a group apart.
    private const double GROUP_GAP = 20.0;

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

    private void TitleMenu_ToggleCollapse_Click(object sender, RoutedEventArgs e) => ToggleCollapse();

    private void TitleMenu_NewPicket_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current is App app)
            app.CreatePicket(Left + 30, Top + 30);
    }

    private void AddPicketBtn_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Mark handled so the title bar's drag-move doesn't pick this click up.
        e.Handled = true;
        if (Application.Current is App app)
            app.CreatePicket(Left + 30, Top + 30);
    }

    private void UnlinkBtn_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var neighbors = ComputeTouchingCluster().Where(f => f != this).ToList();
        if (neighbors.Count == 0) return;

        // Push this picket in the direction away from the cluster's centroid -- that breaks contact
        // regardless of whether neighbors are above, below, or surrounding us.
        double cx = neighbors.Average(f => f.Left + f.Width  / 2);
        double cy = neighbors.Average(f => f.Top  + f.Height / 2);
        double dx = (Left + Width  / 2) - cx;
        double dy = (Top  + Height / 2) - cy;
        double mag = Math.Sqrt(dx * dx + dy * dy);
        if (mag < 0.001) { dx = 1; dy = 1; mag = Math.Sqrt(2); }

        const double SHIFT = 24;  // > SNAP_PIXELS so we don't immediately re-snap
        Left += dx / mag * SHIFT;
        Top  += dy / mag * SHIFT;
    }

    /// <summary>Updates the unlink button's visibility based on whether this picket touches any neighbor.
    /// Called by App after any picket in the system moves.</summary>
    public void RefreshLinkState()
    {
        if (UnlinkBtn == null) return;  // pre-XAML-init guard
        UnlinkBtn.Visibility = HasTouchingNeighbor() ? Visibility.Visible : Visibility.Collapsed;
        RefreshStackChrome();
        RefreshResizeHandles();
    }

    /// <summary>Only expose resize handles on the outside of a connected group. Without this,
    /// the transparent bottom thumb of an upper picket can be dragged through the title of the
    /// picket below it, resizing several independent windows in place and making them overlap.</summary>
    private void RefreshResizeHandles()
    {
        if (ResizeRight == null || ResizeBottom == null || ResizeBottomRight == null) return;

        var cluster = ComputeTouchingCluster();
        var groupRight = cluster.Max(p => p.Left + p.Width);
        var groupBottom = cluster.Max(p => p.Top + p.Height);
        var onRightEdge = Math.Abs(Left + Width - groupRight) <= GROUP_GAP;
        var onBottomEdge = Math.Abs(Top + Height - groupBottom) <= GROUP_GAP;

        ResizeRight.IsHitTestVisible = onRightEdge;
        ResizeBottom.IsHitTestVisible = onBottomEdge && !_isCollapsed;
        ResizeBottomRight.IsHitTestVisible = onRightEdge && onBottomEdge && !_isCollapsed;
    }

    /// <summary>Makes a vertical run of separate native windows read as one continuous component.
    /// Only the outside of the group is rounded; internal top borders become consistent dividers.</summary>
    private void RefreshStackChrome()
    {
        if (Application.Current is not App app || OuterShell == null || TitleShell == null) return;

        var aligned = app.Pickets.Where(other => other != this && HorizontallyOverlaps(other, this));
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

    private bool HasTouchingNeighbor()
    {
        if (Application.Current is not App app) return false;
        foreach (var other in app.Pickets)
        {
            if (other == this) continue;
            if (AreGrouped(this, other)) return true;
        }
        return false;
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
        var missing = Items.Where(i => i.IsMissing).ToList();
        foreach (var m in missing) Items.Remove(m);
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
        RefreshSubmenuChecks(cm, "ColorMenu", _colorKey);
        RefreshSubmenuChecks(cm, "TransparencyMenu", _transparencyKey);
        RefreshBlurCheck(cm);
        RefreshLaunchAtLoginCheck(cm);

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
        if (OuterShell == null) return; // can fire from XAML-parse-time slider events before fields are wired
        var scheme = PicketColors.Get(_colorKey);
        var factor = CurrentTransparencyPercent / 100.0;

        // When blur is on, the system acrylic provides the background color via its gradient tint,
        // so we intentionally paint the shell nearly transparent -- otherwise a second opaque layer
        // sits on top of the blur and defeats it. We use alpha=1 (not 0) because this window is
        // layered (AllowsTransparency=True): fully transparent pixels are click-through at the OS
        // level, so Explorer drops would fall past the picket onto the desktop behind it.
        Color bgColor = _blurEnabled
            ? Color.FromArgb(1, 0, 0, 0)
            : ScaleAlpha(scheme.Background, factor);
        var bg = FrozenBrush(bgColor);
        // Transparency applies fully to the content canvas, but navigation chrome keeps enough
        // surface behind its contrast-selected text to remain readable on any wallpaper.
        var chromeFactor = Math.Max(0.72, factor);
        var titleB   = FrozenBrush(ScaleAlpha(scheme.TitleBackground, chromeFactor));
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
        WindowBlur.Apply(hwnd, _blurEnabled, tint);
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
        HideRenameBox();
        RaiseLayoutChanged();
    }

    private void CancelRename() => HideRenameBox();

    private void HideRenameBox()
    {
        TitleEditBox.Visibility = Visibility.Collapsed;
        TitleText.Visibility = Visibility.Visible;
        // Leaving keyboard focus on the now-hidden TextBox breaks OLE drop routing on this
        // WorkerW-parented child window: subsequent Explorer drags fall through to the
        // desktop behind the picket instead of hitting the ScrollViewer drop target.
        Keyboard.ClearFocus();
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
        if (_isRollAnimating) return;
        ApplyCollapseState(!_isCollapsed, animate: true);
    }

    private void ApplyCollapseState(bool collapsed, bool animate = false)
    {
        if (collapsed == _isCollapsed || _isRollAnimating) return;

        if (animate)
        {
            AnimateCollapseState(collapsed);
            return;
        }

        if (collapsed)
        {
            if (!_isCollapsed) _expandedHeight = Height;
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
        RaiseLayoutChanged();
    }

    /// <summary>
    /// Rolls this picket open/closed while keeping the title bars beneath it attached to its bottom
    /// edge. Previously only this window changed height, so its body covered the rest of a stacked
    /// group. The whole transition is driven together so no overlap appears between animation frames.
    /// </summary>
    private void AnimateCollapseState(bool collapsed)
    {
        var cluster = ComputeTouchingCluster()
            .Where(p => HorizontallyOverlaps(p, this))
            .OrderBy(p => p.Top)
            .ToList();
        var startHeight = Height;
        if (collapsed) _expandedHeight = startHeight;

        var targetHeight = collapsed ? CollapsedHeight : Math.Max(CollapsedHeight, _expandedHeight);

        // The target is rebuilt from the top down rather than offset from today's positions. This
        // repairs old overlaps, fractional DPI drift, and uneven gaps every time a tab is toggled.
        var targetHeights = cluster.ToDictionary(p => p, p => p == this ? targetHeight : p.Height);
        if (!collapsed && TryGetWorkAreaVertical(out var workTop, out var workBottom))
        {
            var excess = Math.Max(0, targetHeights.Values.Sum() - (workBottom - workTop));
            if (excess > 0)
            {
                targetHeight = Math.Max(CollapsedHeight, targetHeight - excess);
                targetHeights[this] = targetHeight;
            }
        }

        var startTops = cluster.ToDictionary(p => p, p => p.Top);
        var dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleY;
        double AlignToPixel(double value) => Math.Round(value * dpiScale) / dpiScale;
        var targetTops = new Dictionary<PicketWindow, double>();
        var cursor = AlignToPixel(cluster[0].Top);
        foreach (var p in cluster)
        {
            targetTops[p] = cursor;
            cursor = AlignToPixel(cursor + targetHeights[p]);
        }

        if (TryGetWorkAreaVertical(out var visibleTop, out var visibleBottom))
        {
            var finalMinTop = targetTops.Values.Min();
            var finalMaxBottom = cluster.Max(p => targetTops[p] + targetHeights[p]);
            var groupShift = finalMaxBottom > visibleBottom
                ? visibleBottom - finalMaxBottom
                : finalMinTop < visibleTop
                    ? visibleTop - finalMinTop
                    : 0;

            if (groupShift != 0)
                foreach (var p in cluster) targetTops[p] = AlignToPixel(targetTops[p] + groupShift);
        }

        foreach (var p in cluster) p._isRollAnimating = true;
        if (!collapsed)
        {
            _isCollapsed = false;
            BodyScroll.Visibility = Visibility.Visible;
        }

        var clock = Stopwatch.StartNew();
        _rollAnimationTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16),
        };
        _rollAnimationTimer.Tick += (_, _) =>
        {
            var progress = Math.Clamp(clock.Elapsed.TotalMilliseconds /
                                      RollAnimationDuration.TotalMilliseconds, 0, 1);
            // Cubic ease-in/out gives the expansion a soft start and a decisive, non-bouncy finish.
            var eased = progress < 0.5
                ? 4 * progress * progress * progress
                : 1 - Math.Pow(-2 * progress + 2, 3) / 2;

            Height = startHeight + (targetHeight - startHeight) * eased;
            foreach (var p in cluster)
                p.Top = startTops[p] + (targetTops[p] - startTops[p]) * eased;

            if (progress < 1) return;

            _rollAnimationTimer.Stop();
            _rollAnimationTimer = null;
            Height = targetHeight;
            foreach (var p in cluster)
            {
                p.Top = targetTops[p];
                p._isRollAnimating = false;
            }

            if (collapsed)
            {
                BodyScroll.Visibility = Visibility.Collapsed;
                _isCollapsed = true;
            }
            RaiseLayoutChanged();
        };
        _rollAnimationTimer.Start();
    }

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
    // A connected cluster resizes as one virtual window. Only members that touch the dragged outer
    // boundary change size: a vertical stack widens together but grows only at its bottom panel;
    // a horizontal row grows taller together but widens only at its rightmost panel. The members
    // are captured once at drag-start so membership stays stable for the whole gesture.
    private List<PicketWindow>? _resizeRightEdge;
    private List<PicketWindow>? _resizeBottomEdge;

    private void Resize_DragStarted(object sender, DragStartedEventArgs e)
    {
        var cluster = ComputeTouchingCluster();
        var groupRight = cluster.Max(p => p.Left + p.Width);
        var groupBottom = cluster.Max(p => p.Top + p.Height);
        _resizeRightEdge = cluster
            .Where(p => Math.Abs(p.Left + p.Width - groupRight) <= GROUP_GAP)
            .ToList();
        _resizeBottomEdge = cluster
            .Where(p => !p._isCollapsed && Math.Abs(p.Top + p.Height - groupBottom) <= GROUP_GAP)
            .ToList();
    }

    private void Resize_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        _resizeRightEdge = null;
        _resizeBottomEdge = null;
        if (Application.Current is App app)
            foreach (var p in app.Pickets) p.RefreshLinkState();
    }

    private static void ResizeWidth(List<PicketWindow>? edge, double requestedChange)
    {
        if (edge == null || edge.Count == 0) return;
        var change = Math.Max(requestedChange, edge.Max(p => p.MinWidth - p.Width));
        foreach (var p in edge) p.Width += change;
    }

    private static void ResizeHeight(List<PicketWindow>? edge, double requestedChange)
    {
        if (edge == null || edge.Count == 0) return;
        var change = Math.Max(requestedChange, edge.Max(p => p.MinHeight - p.Height));
        foreach (var p in edge) p.Height += change;
    }

    private void ResizeRight_DragDelta(object sender, DragDeltaEventArgs e)
        => ResizeWidth(_resizeRightEdge, e.HorizontalChange);

    private void ResizeBottom_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (_isCollapsed) return;
        ResizeHeight(_resizeBottomEdge, e.VerticalChange);
    }

    private void ResizeBottomRight_DragDelta(object sender, DragDeltaEventArgs e)
    {
        ResizeWidth(_resizeRightEdge, e.HorizontalChange);
        if (_isCollapsed) return;
        ResizeHeight(_resizeBottomEdge, e.VerticalChange);
    }

    // === Drag-drop into the picket body ===
    private const string PicketItemDragFormat = "Pickets.PicketItemDrag";
    private sealed record PicketItemDragPayload(PicketWindow Source, PicketItem Item);

    private bool _loggedFirstDragOver;
    private void ItemsHost_DragOver(object sender, DragEventArgs e)
    {
        if (!_loggedFirstDragOver)
        {
            _loggedFirstDragOver = true;
            Logger.Log($"ItemsHost_DragOver first hit. blur={_blurEnabled} " +
                       $"hasFileDrop={e.Data.GetDataPresent(DataFormats.FileDrop)}");
        }
        if (e.Data.GetDataPresent(PicketItemDragFormat))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
            return;
        }
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        e.Effects = DragDropEffects.Link;
        e.Handled = true;
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
            if (payload.Source != this && !Items.Any(it =>
                    string.Equals(it.Path, payload.Item.Path, StringComparison.OrdinalIgnoreCase)))
            {
                payload.Source.Items.Remove(payload.Item);
                payload.Item.IsSelected = false;
                Items.Add(payload.Item);
            }
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
            return;
        }

        var paths = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? (string[])e.Data.GetData(DataFormats.FileDrop)!
            : internalPayload != null
                ? new[] { internalPayload.Item.Path }
                : Array.Empty<string>();
        if (paths.Length == 0) { e.Handled = true; return; }

        foreach (var p in paths)
        {
            if (Items.Any(it => string.Equals(it.Path, p, StringComparison.OrdinalIgnoreCase)))
                continue;

            var item = PicketItem.FromPath(p);
            item.OriginalDesktopPos = DesktopIconHider.Hide(p);
            item.IsMissing = !PathExists(p);
            Items.Add(item);
        }
        e.Handled = true;
    }

    // === Item interactions ===
    private Point? _itemDragStart;
    private FrameworkElement? _itemDragSource;
    private PicketItem? _itemDragItem;

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

        // Single left click: select. Ctrl toggles (multi-select), plain click clears others first.
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        if (ctrl)
        {
            item.IsSelected = !item.IsSelected;
        }
        else
        {
            ClearSelection(except: item);
            item.IsSelected = true;
        }

        _itemDragStart = e.GetPosition(this);
        _itemDragSource = fe;
        _itemDragItem = item;
        e.Handled = true;
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
        ResetItemDragCandidate();

        if (item.IsMissing || !PathExists(item.Path)) return;

        var data = new DataObject();
        data.SetData(PicketItemDragFormat, new PicketItemDragPayload(this, item));
        // Captured desktop items already live in the Desktop folder. Advertising FileDrop makes
        // Explorer attempt a same-folder move and display "source and destination are the same".
        // Keep those gestures private to Pickets; uncaptured items remain standard file drags.
        if (!item.OriginalDesktopPos.HasValue)
            data.SetData(DataFormats.FileDrop, new[] { item.Path });

        var canceled = false;
        QueryContinueDragEventHandler cancelTracker = (_, args) =>
        {
            if (args.EscapePressed) canceled = true;
        };

        source.QueryContinueDrag += cancelTracker;
        try
        {
            DragDrop.DoDragDrop(source, data,
                DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
        }
        finally
        {
            source.QueryContinueDrag -= cancelTracker;
        }

        // A drop onto the desktop can report None because the filesystem item already lives there.
        // Cursor location, plus explicit Escape tracking, tells that valid gesture from cancellation.
        if (canceled || !Items.Contains(item) ||
            !WindowInterop.GetCursorPos(out var cursor) || IsPointInsideAnyPicket(cursor)) return;

        if (item.OriginalDesktopPos.HasValue)
        {
            var dropPosition = new POINT(cursor.X - 32, cursor.Y - 32);
            if (!DesktopIconHider.Restore(item.Path, dropPosition) &&
                !dropPosition.Equals(item.OriginalDesktopPos.Value))
                DesktopIconHider.Restore(item.Path, item.OriginalDesktopPos.Value);
        }
        Items.Remove(item);
        e.Handled = true;
    }

    private void Item_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        => ResetItemDragCandidate();

    private void ResetItemDragCandidate()
    {
        _itemDragStart = null;
        _itemDragSource = null;
        _itemDragItem = null;
    }

    private static bool IsPointInsideAnyPicket(POINT point)
    {
        if (Application.Current is not App app) return false;
        foreach (var picket in app.Pickets)
        {
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

    // Click on empty picket body (below the last item) clears the current selection, matching
    // Explorer's behavior.
    private void BodyScroll_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        ClearSelection();
    }

    private void Item_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.ContextMenu is ContextMenu cm
            && fe.DataContext is PicketItem item)
        {
            var largeItem = cm.Items.OfType<MenuItem>()
                .FirstOrDefault(mi => "LargeToggle".Equals(mi.Tag));
            if (largeItem != null) largeItem.IsChecked = item.IsLarge;

        }
    }

    private void ItemMenu_Open_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is PicketItem item)
            LaunchItem(item);
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
            item.IsLarge = !item.IsLarge;
            RaiseLayoutChanged();
        }
    }

    private void ItemMenu_Remove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is PicketItem item)
        {
            if (item.OriginalDesktopPos.HasValue && !item.IsMissing)
                DesktopIconHider.Restore(item.Path, item.OriginalDesktopPos.Value);
            Items.Remove(item);
        }
    }

    private static void LaunchItem(PicketItem item)
    {
        if (item.IsMissing)
        {
            MessageBox.Show($"This file no longer exists:\n{item.Path}",
                "Pickets", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open:\n{item.Path}\n\n{ex.Message}",
                "Pickets", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // File.Exists returns false for directories, which would flag every dropped folder as missing.
    private static bool PathExists(string p) => File.Exists(p) || Directory.Exists(p);

    // === Section labels ===
    private void TitleMenu_AddLabel_Click(object sender, RoutedEventArgs e)
    {
        var text = InputDialog.Show(this, "Section label", "Label text:", "Section");
        if (string.IsNullOrWhiteSpace(text)) return;
        Items.Add(PicketItem.CreateLabel(text.Trim()));
    }

    private void ItemMenu_InsertLabelAbove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi || mi.DataContext is not PicketItem clicked) return;
        var text = InputDialog.Show(this, "Section label", "Label text:", "Section");
        if (string.IsNullOrWhiteSpace(text)) return;
        var idx = Items.IndexOf(clicked);
        if (idx < 0) idx = Items.Count;
        Items.Insert(idx, PicketItem.CreateLabel(text.Trim()));
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
