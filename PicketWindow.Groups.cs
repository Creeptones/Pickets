using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Pickets;

public partial class PicketWindow
{
    internal string GroupId { get; private set; } = "";
    internal int GroupOrder { get; private set; }
    private bool _groupHorizontal;
    private bool _accordionMode;
    private Action? _finishRollAnimation;
    internal bool NeedsGroupMigration { get; private set; }

    private List<PicketWindow> ComputeTouchingCluster()
        => Application.Current is App app
            ? app.Pickets.Where(p => p.GroupId == GroupId).OrderBy(p => p.GroupOrder).ThenBy(p => p.PicketId).ToList()
            : new List<PicketWindow> { this };

    internal void JoinTouchingGroups(bool migrateOnly = false)
    {
        var cluster = ComputeTouchingCluster();
        if (Application.Current is not App app) return;
        var originalCount = cluster.Count;
        bool added;
        do
        {
            added = false;
            foreach (var candidate in app.Pickets)
            {
                if (cluster.Contains(candidate) || (migrateOnly && !candidate.NeedsGroupMigration)) continue;
                if (!cluster.Any(p => AreGrouped(p, candidate))) continue;
                foreach (var member in candidate.ComputeTouchingCluster())
                    if (!cluster.Contains(member)) cluster.Add(member);
                added = true;
            }
        } while (added);
        if (cluster.Count == originalCount && !migrateOnly) { NormalizeConnectedGroup(); return; }
        var horizontal = GetSimpleGroupOrientation(cluster) == GroupOrientation.Row;
        var accordion = cluster.Any(p => p._accordionMode);
        if (accordion) horizontal = false;
        var ordered = horizontal ? cluster.OrderBy(p => p.Left).ThenBy(p => p.Top).ToList()
            : cluster.OrderBy(p => p.Top).ThenBy(p => p.Left).ToList();
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
        var group = ComputeTouchingCluster();
        if (group.Count == 0) return;
        var first = group[0];
        var width = Math.Max(group.Max(p => p.MinWidth), first.Width);
        var expanded = Math.Max(96, first._expandedHeight);
        var open = group.FirstOrDefault(p => !p._isCollapsed);
        foreach (var p in group)
        {
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
        if (first._groupHorizontal && group.Count * group.Max(p => p.MinWidth) > work.Width)
            foreach (var p in group) p._groupHorizontal = false;
        var dpi = VisualTreeHelper.GetDpi(first);
        var bounds = StackLayout.Arrange(new Point(first.Left, first.Top), width, expandedHeight,
            group.Select(p => p._isCollapsed).ToList(), first._groupHorizontal, work, dpi.DpiScaleX, dpi.DpiScaleY);
        for (var i = 0; i < group.Count; i++)
        {
            group[i].Left = bounds[i].Left;
            group[i].Top = bounds[i].Top;
            group[i].Width = bounds[i].Width;
            group[i].Height = bounds[i].Height;
        }
    }

    private void AnimateStack(bool collapsed)
    {
        var group = ComputeTouchingCluster();
        if (group.Any(p => p._isRollAnimating)) return;
        var states = StackLayout.CollapseStates(group.Select(p => p._isCollapsed).ToArray(),
            group.IndexOf(this), collapsed, _accordionMode);
        var start = group.Select(p => new Rect(p.Left, p.Top, p.Width, p.Height)).ToArray();
        var first = group[0];
        var dpi = VisualTreeHelper.GetDpi(first);
        var target = StackLayout.Arrange(new Point(first.Left, first.Top), first.Width, first._expandedHeight,
            states, _groupHorizontal, first.GroupWorkArea(), dpi.DpiScaleX, dpi.DpiScaleY);
        foreach (var p in group) p._isRollAnimating = true;
        for (var i = 0; i < group.Count; i++)
            if (!states[i]) group[i].BodyScroll.Visibility = Visibility.Visible;
        var clock = Stopwatch.StartNew();
        _rollAnimationTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        void Finish()
        {
            _rollAnimationTimer?.Stop();
            _rollAnimationTimer = null;
            _finishRollAnimation = null;
            for (var i = 0; i < group.Count; i++)
            {
                var p = group[i];
                p.Left = target[i].Left;
                p.Top = target[i].Top;
                p.Width = target[i].Width;
                p.Height = target[i].Height;
                p._isCollapsed = states[i];
                p._isRollAnimating = false;
                p.UpdateExpansionControls();
                p.RaiseLayoutChanged();
            }
        }
        _finishRollAnimation = Finish;
        // Hidden sections and reduced-motion users do not need a render timer.
        if (!IsVisible || !SystemParameters.ClientAreaAnimation) { Finish(); return; }
        _rollAnimationTimer.Tick += (_, _) =>
        {
            var progress = Math.Clamp(clock.Elapsed.TotalMilliseconds / RollAnimationDuration.TotalMilliseconds, 0, 1);
            if (!SystemParameters.ClientAreaAnimation) progress = 1;
            var eased = 1 - Math.Pow(1 - progress, 3);
            for (var i = 0; i < group.Count; i++)
            {
                var p = group[i];
                p.Left = start[i].Left + (target[i].Left - start[i].Left) * eased;
                p.Top = start[i].Top + (target[i].Top - start[i].Top) * eased;
                p.Height = start[i].Height + (target[i].Height - start[i].Height) * eased;
            }
            if (progress >= 1) Finish();
        };
        _rollAnimationTimer.Start();
    }

    private void UpdateExpansionControls()
    {
        BodyScroll.Visibility = _isCollapsed ? Visibility.Collapsed : Visibility.Visible;
        TitleToggle.IsExpanded = !_isCollapsed;
    }

    internal void SetExpanded(bool expanded)
    {
        if (_isCollapsed == !expanded || _isRollAnimating) return;
        NormalizeConnectedGroup();
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
    }
}
