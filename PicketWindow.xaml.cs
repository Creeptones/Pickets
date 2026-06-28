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

public partial class PicketWindow : Window
{
    public string PicketId { get; }
    public ObservableCollection<PicketItem> Items { get; } = new();

    /// <summary>Raised whenever the picket's persistent state changes (move, resize, item add/remove).</summary>
    public event EventHandler? LayoutChanged;

    // When set, Items mirrors a live folder. Manual drag-drop is disabled; desktop-icon
    // hiding/restoration is bypassed since portal items live in a real folder, not on the desktop.
    private FolderPortal? _portal;
    public bool IsPortal => _portal != null;
    public string? PortalPath => _portal?.FolderPath;

    private bool _isLoading;
    private bool _isCollapsed;
    private double _expandedHeight;
    private string _colorKey = "stone";
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

        _colorKey = state.ColorKey;
        _transparencyKey = state.TransparencyKey;
        _transparencyCustomPercent = Math.Clamp(state.TransparencyCustomPercent, 0, 100);
        _blurEnabled = state.BlurEnabled;
        ApplyVisuals();

        _isLoading = true;
        if (!string.IsNullOrEmpty(state.PortalPath))
        {
            // Portal picket: skip saved items (they're rebuilt from the folder) and start watching.
            StartPortal(state.PortalPath);
        }
        else
        {
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
        }
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
        PortalPath = _portal?.FolderPath,
        BlurEnabled = _blurEnabled,
        // Portal pickets rebuild Items from the folder on each launch -- don't persist them.
        Items = IsPortal
            ? new List<ItemState>()
            : Items.Select(i => i.Kind == ItemKind.Label
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
        foreach (var member in cluster)
        {
            if (member == this) continue;
            var h = new WindowInteropHelper(member).Handle;
            if (h != IntPtr.Zero)
                WindowInterop.SetWindowPos(h, WindowInterop.HWND_TOP, 0, 0, 0, 0, flags);
        }
        // Re-raise self LAST so it ends up on top of the freshly-raised followers.
        var selfHwnd = new WindowInteropHelper(this).Handle;
        if (selfHwnd != IntPtr.Zero)
            WindowInterop.SetWindowPos(selfHwnd, WindowInterop.HWND_TOP, 0, 0, 0, 0, flags);
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
        if (e.ClickCount == 2)
        {
            ToggleCollapse();
            e.Handled = true;
            return;
        }
        if (e.ClickCount == 1 && e.LeftButton == MouseButtonState.Pressed)
        {
            ClearSelection();
            BeginGroupDrag();
            try { DragMove(); }                   // blocks until the user releases
            finally { _dragCluster = null; }
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
        const double TOL = 4.0;  // snap leaves edges flush, but allow a few px for float drift

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
                if (Touches(f, g, TOL))
                {
                    cluster.Add(g);
                    queue.Enqueue(g);
                }
            }
        }
        return cluster;
    }

    private static bool Touches(PicketWindow a, PicketWindow b, double tol)
    {
        double aL = a.Left, aR = a.Left + a.Width, aT = a.Top, aB = a.Top + a.Height;
        double bL = b.Left, bR = b.Left + b.Width, bT = b.Top, bB = b.Top + b.Height;

        bool xRangesOverlap = aL < bR + tol && bL < aR + tol;
        bool yRangesOverlap = aT < bB + tol && bT < aB + tol;
        bool sharedVerticalEdge   = Math.Abs(aR - bL) <= tol || Math.Abs(aL - bR) <= tol;
        bool sharedHorizontalEdge = Math.Abs(aB - bT) <= tol || Math.Abs(aT - bB) <= tol;

        return (sharedVerticalEdge && yRangesOverlap)
            || (sharedHorizontalEdge && xRangesOverlap);
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
    }

    private bool HasTouchingNeighbor()
    {
        if (Application.Current is not App app) return false;
        foreach (var other in app.Pickets)
        {
            if (other == this) continue;
            if (Touches(this, other, 4.0)) return true;
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
        var msg = IsPortal
            ? "Delete this folder portal? The underlying folder is not affected."
            : "Delete this picket? Captured icons will be restored to the desktop.";
        var result = MessageBox.Show(msg, "Pickets",
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
        if (Application.Current is App app) app.RestoreAllAndQuit();
    }

    private void TitleMenu_Quit_Click(object sender, RoutedEventArgs e)
    {
        // Plain quit: layout auto-saves via the debounce timer and OnExit, and hidden desktop
        // icons stay hidden (they'll reappear in their pickets on next launch). No confirmation --
        // this is a reversible action since relaunching restores everything.
        if (Application.Current is App app) app.Shutdown();
    }

    private void TitleMenu_HideAll_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current is App app) app.ToggleAllPicketsVisibility();
    }

    private void TitleMenu_SetColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is string key)
        {
            _colorKey = key;
            ApplyVisuals();
            RaiseLayoutChanged();
        }
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
        SetPortalMenuVisibility(cm);
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
        var parent = cm.Items.OfType<MenuItem>().FirstOrDefault(mi => parentTag.Equals(mi.Tag));
        if (parent == null) return;
        foreach (var item in parent.Items.OfType<MenuItem>())
            item.IsChecked = item.Tag is string key && key == selectedKey;
    }

    private static (Slider? slider, TextBlock? label) FindCustomSliderAndLabel(ContextMenu cm)
    {
        var transparencyMenu = cm.Items.OfType<MenuItem>().FirstOrDefault(mi => "TransparencyMenu".Equals(mi.Tag));
        var customItem = transparencyMenu?.Items.OfType<MenuItem>().FirstOrDefault(mi => "custom".Equals(mi.Tag));
        if (customItem?.Header is not StackPanel sp) return (null, null);
        var slider = sp.Children.OfType<Slider>().FirstOrDefault(s => "CustomSlider".Equals(s.Tag));
        var label  = sp.Children.OfType<TextBlock>().FirstOrDefault(t => "CustomLabel".Equals(t.Tag));
        return (slider, label);
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
        Color bgColor = _blurEnabled ? Color.FromArgb(1, 0, 0, 0) : ScaleAlpha(scheme.Background, factor);
        var bg     = new SolidColorBrush(bgColor);
        var titleB = new SolidColorBrush(ScaleAlpha(scheme.TitleBackground, factor));
        var border = new SolidColorBrush(ScaleAlpha(scheme.Border,          factor));
        var fg     = new SolidColorBrush(scheme.Foreground); // text always opaque
        bg.Freeze(); titleB.Freeze(); border.Freeze(); fg.Freeze();

        OuterShell.Background = bg;
        OuterShell.BorderBrush = border;
        TitleShell.Background = titleB;
        TitleText.Foreground = fg;
        TitleEditBox.Foreground = fg;
        TitleEditBox.CaretBrush = fg;
        Resources["PicketItemForeground"] = fg;
        // Label halo is a Color (not a Brush) because DropShadowEffect.Color takes a Color DP.
        Resources["PicketItemShadowColor"] = scheme.Shadow;

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
    private void ToggleCollapse() => ApplyCollapseState(!_isCollapsed);

    private void ApplyCollapseState(bool collapsed)
    {
        if (collapsed)
        {
            if (!_isCollapsed) _expandedHeight = Height;
            BodyScroll.Visibility = Visibility.Collapsed;
            Height = 32;
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

    // === Resize thumbs ===
    private void ResizeRight_DragDelta(object sender, DragDeltaEventArgs e)
    {
        var newW = Width + e.HorizontalChange;
        if (newW >= MinWidth) Width = newW;
    }

    private void ResizeBottom_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (_isCollapsed) return;
        var newH = Height + e.VerticalChange;
        if (newH >= MinHeight) Height = newH;
    }

    private void ResizeBottomRight_DragDelta(object sender, DragDeltaEventArgs e)
    {
        var newW = Width + e.HorizontalChange;
        if (newW >= MinWidth) Width = newW;
        if (_isCollapsed) return;
        var newH = Height + e.VerticalChange;
        if (newH >= MinHeight) Height = newH;
    }

    // === Drag-drop into the picket body ===
    private bool _loggedFirstDragOver;
    private void ItemsHost_DragOver(object sender, DragEventArgs e)
    {
        if (!_loggedFirstDragOver)
        {
            _loggedFirstDragOver = true;
            Logger.Log($"ItemsHost_DragOver first hit. blur={_blurEnabled} isPortal={IsPortal} " +
                       $"hasFileDrop={e.Data.GetDataPresent(DataFormats.FileDrop)}");
        }
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        if (IsPortal)
        {
            // Portals mirror their folder on disk. Dropping moves (or copies with Ctrl) the
            // dragged items into that folder; the watcher surfaces them as picket items.
            bool copy = (e.KeyStates & DragDropKeyStates.ControlKey) == DragDropKeyStates.ControlKey;
            e.Effects = copy ? DragDropEffects.Copy : DragDropEffects.Move;
        }
        else
        {
            e.Effects = DragDropEffects.Link;
        }
        e.Handled = true;
    }

    private void ItemsHost_Drop(object sender, DragEventArgs e)
    {
        Logger.Log($"ItemsHost_Drop fired. blur={_blurEnabled} isPortal={IsPortal} " +
                   $"hasFileDrop={e.Data.GetDataPresent(DataFormats.FileDrop)}");
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Handled = true; return; }
        var paths = (string[])e.Data.GetData(DataFormats.FileDrop)!;

        if (IsPortal)
        {
            bool copy = (e.KeyStates & DragDropKeyStates.ControlKey) == DragDropKeyStates.ControlKey;
            var destFolder = PortalPath!;
            foreach (var src in paths)
            {
                try { MoveOrCopyIntoFolder(src, destFolder, copy); }
                catch (Exception ex)
                {
                    Logger.Log($"Portal drop failed for '{src}' -> '{destFolder}': {ex.Message}");
                    MessageBox.Show(
                        $"Could not {(copy ? "copy" : "move")} '{Path.GetFileName(src)}':\n{ex.Message}",
                        "Pickets", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            // We performed the file operation ourselves; report the effect so the source app
            // doesn't attempt its own follow-up delete.
            e.Effects = copy ? DragDropEffects.Copy : DragDropEffects.Move;
            e.Handled = true;
            return;
        }

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

    private static void MoveOrCopyIntoFolder(string src, string destFolder, bool copy)
    {
        var trimmed = src.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        if (string.IsNullOrEmpty(name)) throw new IOException($"Invalid source path '{src}'");

        var dest = Path.Combine(destFolder, name);

        // Dropping an item back into its own folder is a no-op.
        var srcFull = Path.GetFullPath(src);
        var destFull = Path.GetFullPath(dest);
        if (string.Equals(srcFull, destFull, StringComparison.OrdinalIgnoreCase)) return;

        dest = MakeUniquePath(dest);

        var attr = File.GetAttributes(src);
        bool isDir = (attr & FileAttributes.Directory) != 0;

        if (copy)
        {
            if (isDir) CopyDirectoryRecursive(src, dest);
            else File.Copy(src, dest);
        }
        else
        {
            if (isDir)
            {
                try
                {
                    Directory.Move(src, dest);
                }
                catch (IOException)
                {
                    // Cross-volume Directory.Move fails -- fall back to copy + delete.
                    CopyDirectoryRecursive(src, dest);
                    Directory.Delete(src, recursive: true);
                }
            }
            else
            {
                File.Move(src, dest);
            }
        }
    }

    private static string MakeUniquePath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (int i = 2; i < 1000; i++)
        {
            var candidate = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
        throw new IOException($"Could not find a unique name for '{path}'");
    }

    private static void CopyDirectoryRecursive(string src, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.EnumerateFiles(src))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)));
        foreach (var sub in Directory.EnumerateDirectories(src))
            CopyDirectoryRecursive(sub, Path.Combine(dest, Path.GetFileName(sub)));
    }

    // === Item interactions ===
    private void Item_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not PicketItem item) return;

        if (e.ClickCount == 2)
        {
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
        e.Handled = true;
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

            // Labels don't belong inside portal pickets (they're derived from the folder), so
            // hide "Insert label above..." on portal items.
            var insertLabel = cm.Items.OfType<MenuItem>()
                .FirstOrDefault(mi => mi.Header is string s && s.StartsWith("Insert label"));
            if (insertLabel != null)
                insertLabel.Visibility = IsPortal ? Visibility.Collapsed : Visibility.Visible;
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

    private void LaunchItem(PicketItem item)
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

    // === Folder portal ===
    private void TitleMenu_ConvertToPortal_Click(object sender, RoutedEventArgs e)
    {
        if (IsPortal) return;

        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Pick a folder to mirror in this picket",
        };
        if (dialog.ShowDialog(this) != true) return;

        var folderPath = dialog.FolderName;
        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath)) return;

        // Warn before wiping a non-empty picket so an accidental convert doesn't quietly
        // discard manually curated items. Captured desktop icons are restored either way.
        if (Items.Count > 0)
        {
            var result = MessageBox.Show(
                "Converting this picket to a folder portal will remove its current items. " +
                "Any captured desktop icons will be restored to the desktop.\n\nContinue?",
                "Pickets", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (result != MessageBoxResult.OK) return;

            foreach (var item in Items.ToList())
            {
                if (item.OriginalDesktopPos.HasValue && !item.IsMissing)
                    DesktopIconHider.Restore(item.Path, item.OriginalDesktopPos.Value);
            }
        }
        Items.Clear();

        StartPortal(folderPath);

        // Default title to the folder name (TrimEnd handles drive roots like "C:\").
        var folderName = System.IO.Path.GetFileName(folderPath.TrimEnd(
            System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
        if (!string.IsNullOrEmpty(folderName)) TitleText.Text = folderName;

        RaiseLayoutChanged();
    }

    private void TitleMenu_OpenPortalFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_portal == null) return;
        try
        {
            Process.Start(new ProcessStartInfo(_portal.FolderPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Pickets", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void TitleMenu_DisconnectPortal_Click(object sender, RoutedEventArgs e)
    {
        if (_portal == null) return;
        StopPortal();
        Items.Clear();
        RaiseLayoutChanged();
    }

    private void StartPortal(string folderPath)
    {
        _portal = new FolderPortal(folderPath, Dispatcher);
        _portal.ItemAdded   += OnPortalItemAdded;
        _portal.ItemRemoved += OnPortalItemRemoved;
        _portal.ItemRenamed += OnPortalItemRenamed;
        _portal.Start();
    }

    private void StopPortal()
    {
        if (_portal == null) return;
        _portal.ItemAdded   -= OnPortalItemAdded;
        _portal.ItemRemoved -= OnPortalItemRemoved;
        _portal.ItemRenamed -= OnPortalItemRenamed;
        _portal.Dispose();
        _portal = null;
    }

    private void OnPortalItemAdded(string path)
    {
        if (Items.Any(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase)))
            return; // dedupe: initial scan + Created events can race on the same path
        var item = PicketItem.FromPath(path);
        Items.Insert(FindPortalInsertIndex(item), item);
    }

    private void OnPortalItemRemoved(string path)
    {
        var match = Items.FirstOrDefault(i =>
            string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase));
        if (match != null) Items.Remove(match);
    }

    private void OnPortalItemRenamed(string oldPath, string newPath)
    {
        OnPortalItemRemoved(oldPath);
        OnPortalItemAdded(newPath);
    }

    /// <summary>Returns the index at which `item` should be inserted to maintain
    /// folders-first, case-insensitive alphabetical ordering.</summary>
    private int FindPortalInsertIndex(PicketItem item)
    {
        for (int i = 0; i < Items.Count; i++)
        {
            var existing = Items[i];
            if (item.IsFolder && !existing.IsFolder) return i;
            if (!item.IsFolder && existing.IsFolder) continue;
            if (string.Compare(item.DisplayName, existing.DisplayName,
                               StringComparison.OrdinalIgnoreCase) < 0) return i;
        }
        return Items.Count;
    }

    private void SetPortalMenuVisibility(ContextMenu cm)
    {
        bool isPortal = IsPortal;
        foreach (var obj in cm.Items)
        {
            if (obj is not FrameworkElement fe || fe.Tag is not string tag) continue;
            switch (tag)
            {
                case "ConvertToPortal":
                    fe.Visibility = isPortal ? Visibility.Collapsed : Visibility.Visible;
                    break;
                case "OpenPortalFolder":
                case "DisconnectPortal":
                    fe.Visibility = isPortal ? Visibility.Visible : Visibility.Collapsed;
                    break;
            }
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        StopPortal();
        base.OnClosed(e);
    }

    // File.Exists returns false for directories, which would flag every dropped folder as missing.
    private static bool PathExists(string p) => File.Exists(p) || Directory.Exists(p);

    // === Section labels ===
    private void TitleMenu_AddLabel_Click(object sender, RoutedEventArgs e)
    {
        if (IsPortal)
        {
            MessageBox.Show("Labels can't be added to folder portals -- disconnect the portal first.",
                "Pickets", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var text = InputDialog.Show(this, "Section label", "Label text:", "Section");
        if (string.IsNullOrWhiteSpace(text)) return;
        Items.Add(PicketItem.CreateLabel(text.Trim()));
    }

    private void ItemMenu_InsertLabelAbove_Click(object sender, RoutedEventArgs e)
    {
        if (IsPortal) return;
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
        var item = cm.Items.OfType<MenuItem>()
            .FirstOrDefault(mi => "BlurToggle".Equals(mi.Tag));
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
        var item = cm.Items.OfType<MenuItem>()
            .FirstOrDefault(mi => "LaunchAtLogin".Equals(mi.Tag));
        if (item != null) item.IsChecked = StartupEntry.IsEnabled;
    }
}
