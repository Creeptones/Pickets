using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace Pickets;

public partial class PicketWindow
{
    internal string GroupId { get; private set; } = "";
    internal int GroupOrder { get; private set; }
    private bool _groupHorizontal;
    private bool _accordionMode;
    private Action? _finishRollAnimation;
    private Action? _cancelRollAnimation;
    private bool? _rollTargetCollapsed;
    private Point? _rollAnchor;
    private bool IntendedCollapsed => _rollTargetCollapsed ?? _isCollapsed;
    internal bool NeedsGroupMigration { get; private set; }
    private int _stackPageIndex;
    private int _stackPageCount = 1;
    internal bool IsOnStackPage { get; private set; } = true;

    internal void ShowIfOnStackPage()
    {
        if (IsOnStackPage) Show();
    }

    private static StackPage GetStackPage(List<PicketWindow> group)
    {
        var first = group[0];
        var work = first.GroupWorkArea();
        return StackViewport.Page(group.Count, work.Height, first._stackPageIndex, work.Width, first._groupHorizontal);
    }

    internal void RevealOnStackPage()
    {
        var group = ComputeTouchingCluster();
        if (group.Count == 0) return;
        var page = GetStackPage(group);
        var required = group.IndexOf(this) / page.Capacity;
        if (required == page.Index) return;
        foreach (var p in group) p._stackPageIndex = required;
        NormalizeConnectedGroup();
    }

    internal void ChangeStackPage(int direction, bool focus = true)
    {
        var group = ComputeTouchingCluster();
        foreach (var p in group) p._finishRollAnimation?.Invoke();
        var page = GetStackPage(group);
        foreach (var p in group) p._stackPageIndex = Math.Clamp(page.Index + direction, 0, page.Count - 1);
        NormalizeConnectedGroup();
        if (focus) group.First(p => p.IsOnStackPage).FocusForKeyboard();
    }

    private void PreviousStackPage_Click(object sender, RoutedEventArgs e) => ChangeStackPage(-1);
    private void NextStackPage_Click(object sender, RoutedEventArgs e) => ChangeStackPage(1);

    private void UpdateStackPageControls(StackPage page, bool onPage)
    {
        var wasOnPage = IsOnStackPage;
        IsOnStackPage = onPage;
        _stackPageIndex = page.Index;
        _stackPageCount = page.Count;
        PreviousPageButton.Visibility = NextPageButton.Visibility = page.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        PreviousPageButton.IsEnabled = page.Index > 0;
        NextPageButton.IsEnabled = page.Index + 1 < page.Count;
        var position = $"Stack page {page.Index + 1} of {page.Count}";
        PreviousPageButton.ToolTip = position + " — previous page (Ctrl+PageUp)";
        NextPageButton.ToolTip = position + " — next page (Ctrl+PageDown)";
        System.Windows.Automation.AutomationProperties.SetHelpText(TitleToggle,
            "Expand or collapse this picket. Drag to move its stack. " + (page.Count > 1 ? position : ""));
        if (!onPage && IsVisible) Hide();
        else if (onPage && !wasOnPage && IsLoaded && Application.Current is App { PicketsHidden: false }) Show();
    }

    private List<PicketWindow> ComputeTouchingCluster()
        => Application.Current is App app
            ? app.Pickets.Where(p => p.GroupId == GroupId).OrderBy(p => p.GroupOrder).ThenBy(p => p.PicketId).ToList()
            : new List<PicketWindow> { this };

    internal List<PicketWindow> SettleStack()
    {
        var group = ComputeTouchingCluster();
        // The animation belongs to the section that initiated expansion, which need not be the
        // section being added to or removed. Finish it before changing membership or bounds.
        foreach (var member in group) member._finishRollAnimation?.Invoke();
        return group;
    }

    internal void AttachAfter(PicketWindow previous)
    {
        SettleStack();
        var group = previous.SettleStack();
        if (group.Contains(this)) return;
        var first = group[0];
        GroupId = first.GroupId;
        _groupHorizontal = first._groupHorizontal;
        _accordionMode = first._accordionMode;
        _expandedHeight = first._expandedHeight;
        _stackPageIndex = first._stackPageIndex;
        Width = first.Width;
        NeedsGroupMigration = false;
        group.Insert(group.IndexOf(previous) + 1, this);
        for (var i = 0; i < group.Count; i++) group[i].GroupOrder = i;
        if (_accordionMode)
            foreach (var member in group) member._isCollapsed = member != this;
        NormalizeConnectedGroup();
        RevealOnStackPage();
    }

    internal void JoinTouchingGroups(bool migrateOnly = false)
    {
        var cluster = SettleStack();
        if (Application.Current is not App app) return;
        foreach (var candidate in app.Pickets) candidate._finishRollAnimation?.Invoke();
        var originalCount = cluster.Count;
        bool added;
        do
        {
            added = false;
            foreach (var candidate in app.Pickets)
            {
                if (cluster.Contains(candidate) || !candidate.IsOnStackPage || (migrateOnly && !candidate.NeedsGroupMigration)) continue;
                if (!cluster.Any(p => p.IsOnStackPage && AreGrouped(p, candidate))) continue;
                foreach (var member in candidate.ComputeTouchingCluster())
                    if (!cluster.Contains(member)) cluster.Add(member);
                added = true;
            }
        } while (added);
        if (cluster.Count == originalCount && !migrateOnly) { NormalizeConnectedGroup(); return; }
        var horizontal = GetSimpleGroupOrientation(cluster.Where(p => p.IsOnStackPage).ToList()) == GroupOrientation.Row;
        var accordion = cluster.Any(p => p._accordionMode);
        if (accordion) horizontal = false;
        // Hidden pages share an anchor, not a physical order. Keep each existing stack intact
        // when joining another stack; use visible geometry only to order the stacks themselves.
        var groups = cluster.GroupBy(p => p.GroupId);
        var orderedGroups = horizontal
            ? groups.OrderBy(g => g.Where(p => p.IsOnStackPage).Min(p => p.Left))
            : groups.OrderBy(g => g.Where(p => p.IsOnStackPage).Min(p => p.Top));
        var ordered = orderedGroups.SelectMany(g => g.OrderBy(p => p.GroupOrder).ThenBy(p => p.PicketId)).ToList();
        var id = ordered[0].GroupId;
        for (var i = 0; i < ordered.Count; i++)
        {
            var p = ordered[i];
            p.GroupId = id;
            p.GroupOrder = i;
            p._groupHorizontal = horizontal;
            p._accordionMode = accordion;
            p.NeedsGroupMigration = false;
        }
        NormalizeConnectedGroup();
    }

    public void NormalizeConnectedGroup()
    {
        var group = SettleStack();
        if (group.Count == 0) return;
        var first = group[0];
        var width = Math.Max(group.Max(p => p.MinWidth), first.Width);
        var expanded = Math.Max(96, first._expandedHeight);
        var open = group.FirstOrDefault(p => !p._isCollapsed);
        for (var i = 0; i < group.Count; i++)
        {
            var p = group[i];
            p.GroupOrder = i;
            p._expandedHeight = expanded;
            p._groupHorizontal = first._groupHorizontal;
            p._accordionMode = first._accordionMode;
            if (first._accordionMode && p != open) p._isCollapsed = true;
            p.UpdateExpansionControls();
        }
        ApplyGroupBounds(group, width, expanded);
        RaiseLayoutChanged();
    }

    private Rect GroupWorkArea()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return SystemParameters.WorkArea;
        var area = System.Windows.Forms.Screen.FromHandle(hwnd).WorkingArea;
        var localTop = PointFromScreen(new Point(area.Left, area.Top));
        var localBottom = PointFromScreen(new Point(area.Right, area.Bottom));
        return new Rect(Left + localTop.X, Top + localTop.Y,
            localBottom.X - localTop.X, localBottom.Y - localTop.Y);
    }

    private static void ApplyGroupBounds(List<PicketWindow> group, double width, double expandedHeight)
    {
        if (group.Count == 0) return;
        var first = group[0];
        var work = first.GroupWorkArea();
        var page = GetStackPage(group);
        var visible = group.Skip(page.Start).Take(page.Length).ToList();
        var dpi = VisualTreeHelper.GetDpi(first);
        var bounds = StackLayout.Arrange(new Point(first.Left, first.Top), width, expandedHeight,
            visible.Select(p => p._isCollapsed).ToList(), first._groupHorizontal, work, dpi.DpiScaleX, dpi.DpiScaleY);
        for (var i = 0; i < group.Count; i++)
        {
            var onPage = page.Contains(i);
            var rect = onPage ? bounds[i - page.Start] : bounds[0];
            group[i].Left = rect.Left;
            group[i].Top = rect.Top;
            group[i].Width = rect.Width;
            group[i].Height = onPage ? rect.Height : group[i]._isCollapsed ? StackLayout.TitleHeight : expandedHeight;
            group[i].UpdateStackPageControls(page, onPage);
        }
    }

    private void AnimateStack(bool collapsed)
    {
        var update = CreateStackAnimation(collapsed);
        if (update == null) return;
        var finish = _finishRollAnimation!;
        var cancel = _cancelRollAnimation!;
        var frames = (Application.Current as App)?.Frames;
        if (!IsVisible || !SystemParameters.ClientAreaAnimation || frames == null) { finish(); return; }
        var clock = Stopwatch.StartNew();
        var sampleCount = 0;
        var firstSample = 0.0;
        var lastSample = 0.0;
        var longestGap = 0.0;
        Action<TimeSpan> render = _ =>
        {
            try
            {
                var elapsed = clock.Elapsed.TotalMilliseconds;
                if (sampleCount == 0) firstSample = elapsed;
                else longestGap = Math.Max(longestGap, elapsed - lastSample);
                lastSample = elapsed;
                sampleCount++;
                var progress = Math.Clamp(elapsed / RollAnimationDuration.TotalMilliseconds, 0, 1);
                if (!IsVisible || !SystemParameters.ClientAreaAnimation) progress = 1;
                update(progress);
            }
            catch { _finishRollAnimation?.Invoke(); throw; }
        };
        _cancelRollAnimation = () => { frames.RemoveAnimation(render); cancel(); };
        _finishRollAnimation = () =>
        {
            frames.RemoveAnimation(render);
            finish();
            if (sampleCount > 1)
            {
                var meanGap = (lastSample - firstSample) / (sampleCount - 1);
                // Callback cadence is diagnostic evidence, not presented monitor FPS.
                Logger.Log($"Stack render timing: pickets={ComputeTouchingCluster().Count}, callbacks={sampleCount}, mean={meanGap:F2}ms, max={longestGap:F2}ms.");
            }
        };
        frames.AddAnimation(render);
    }

    // Prepare independently of the render clock so retargeting always starts at the current
    // geometry. The returned sampler is inert after cancellation, even if a frame was queued.
    private Action<double>? CreateStackAnimation(bool collapsed)
    {
        var group = ComputeTouchingCluster();
        var anchor = group[0]._rollAnchor ?? new Point(group[0].Left, group[0].Top);
        var states = StackLayout.CollapseStates(group.Select(p => p.IntendedCollapsed).ToArray(),
            group.IndexOf(this), collapsed, _accordionMode);
        foreach (var p in group) p._cancelRollAnimation?.Invoke();
        if (GetStackPage(group).Count > 1)
        {
            for (var i = 0; i < group.Count; i++) group[i]._isCollapsed = states[i];
            NormalizeConnectedGroup();
            return null;
        }
        var start = group.Select(p => new Rect(p.Left, p.Top, p.Width, p.Height)).ToArray();
        var startAngles = group.Select(p => p.ChevronRotation.Angle).ToArray();
        var first = group[0];
        var dpi = VisualTreeHelper.GetDpi(first);
        var target = StackLayout.Arrange(anchor, first.Width, first._expandedHeight,
            states, _groupHorizontal, first.GroupWorkArea(), dpi.DpiScaleX, dpi.DpiScaleY);
        for (var i = 0; i < group.Count; i++)
        {
            group[i]._isRollAnimating = true;
            group[i]._rollTargetCollapsed = states[i];
            group[i]._rollAnchor = anchor;
            group[i].TitleToggle.IsExpanded = !states[i];
            group[i].TitleToggle.ToolTip = states[i] ? "Expand picket; drag to move stack" : "Collapse picket; drag to move stack";
            if (!states[i]) group[i].BodyScroll.Visibility = Visibility.Visible;
        }
        var finished = false;
        void Cancel()
        {
            if (finished) return;
            finished = true;
            _finishRollAnimation = _cancelRollAnimation = null;
            foreach (var p in group)
            {
                p._isRollAnimating = false;
                p._rollTargetCollapsed = null;
                p._rollAnchor = null;
            }
        }
        void Finish()
        {
            if (finished) return;
            for (var i = 0; i < group.Count; i++)
            {
                var p = group[i];
                p.Left = target[i].Left;
                p.Top = target[i].Top;
                p.Width = target[i].Width;
                p.Height = target[i].Height;
                p._isCollapsed = states[i];
                p.UpdateExpansionControls();
            }
            // No intermediate position/size notifications: persist and refresh the final stack once.
            Cancel();
            RaiseLayoutChanged();
        }
        _finishRollAnimation = Finish;
        _cancelRollAnimation = Cancel;
        return progress =>
        {
            if (finished) return;
            progress = Math.Clamp(progress, 0, 1);
            var eased = 1 - Math.Pow(1 - progress, 3);
            for (var i = 0; i < group.Count; i++)
            {
                var p = group[i];
                p.Left = start[i].Left + (target[i].Left - start[i].Left) * eased;
                p.Top = start[i].Top + (target[i].Top - start[i].Top) * eased;
                p.Height = start[i].Height + (target[i].Height - start[i].Height) * eased;
                var targetAngle = states[i] ? 0 : 90;
                p.ChevronRotation.Angle = startAngles[i] + (targetAngle - startAngles[i]) * eased;
            }
            if (progress >= 1) _finishRollAnimation?.Invoke();
        };
    }

    private void UpdateExpansionControls()
    {
        BodyScroll.Visibility = _isCollapsed ? Visibility.Collapsed : Visibility.Visible;
        TitleToggle.IsExpanded = !_isCollapsed;
        ChevronRotation.Angle = _isCollapsed ? 0 : 90;
        TitleToggle.ToolTip = _isCollapsed ? "Expand picket; drag to move stack" : "Collapse picket; drag to move stack";
    }

    internal void SetExpanded(bool expanded)
    {
        if (expanded) RevealOnStackPage();
        if (IntendedCollapsed == !expanded) return;
        if (!_isRollAnimating) NormalizeConnectedGroup();
        AnimateStack(!expanded);
    }

    private void TitleMenu_Accordion_Click(object sender, RoutedEventArgs e)
    {
        if (_isRollAnimating) return;
        var group = ComputeTouchingCluster();
        var enabled = !_accordionMode;
        foreach (var p in group)
        {
            p._accordionMode = enabled;
            if (enabled) { p._groupHorizontal = false; p._isCollapsed = p != this; }
            p.UpdateExpansionControls();
        }
        NormalizeConnectedGroup();
    }

    private void UnlinkGroup()
    {
        if (_isRollAnimating) return;
        var remaining = ComputeTouchingCluster().Where(p => p != this).ToList();
        if (remaining.Count == 0) return;
        GroupId = Guid.NewGuid().ToString();
        GroupOrder = 0;
        _accordionMode = false;
        _groupHorizontal = false;
        Left = remaining.Max(p => p.Left + p.Width) + 28;
        remaining[0].NormalizeConnectedGroup();
        NormalizeConnectedGroup();
    }

    private void MoveSection(int delta)
    {
        if (_isRollAnimating) return;
        var group = ComputeTouchingCluster();
        var index = group.IndexOf(this);
        var next = Math.Clamp(index + delta, 0, group.Count - 1);
        (group[index], group[next]) = (group[next], group[index]);
        var anchor = new Point(group.Min(p => p.Left), group.Min(p => p.Top));
        for (var i = 0; i < group.Count; i++) group[i].GroupOrder = i;
        group[0].Left = anchor.X;
        group[0].Top = anchor.Y;
        group[0].NormalizeConnectedGroup();
        RevealOnStackPage();
    }
}
