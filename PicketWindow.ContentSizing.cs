using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;

namespace Pickets;

public partial class PicketWindow
{
    private bool _autoSizeRows;
    private double? _contentRowsHeight;
    private readonly HashSet<PicketItem> _sizingItems = new();

    private double MeasureContentHeight(double width)
        => (SelectionActions?.Visibility == Visibility.Visible ? SelectionActions.ActualHeight : 0) + (Items.Count == 0 || _contentRowsHeight == null
            ? Math.Max(PicketContentSizing.EmptyHeight, 40 + 9 * PicketContentSizing.LineHeight(FontSize))
            : Math.Ceiling(StackLayout.TitleHeight + 10 + _contentRowsHeight.Value));

    internal void SetAutomaticHeight(bool automatic)
    {
        var group = SettleStack();
        foreach (var picket in group)
        {
            picket._autoSizeRows = automatic;
            if (!automatic && !picket._isCollapsed) picket._expandedHeight = picket.Height;
        }
        NormalizeConnectedGroup();
        foreach (var picket in group) picket.QueueContentSizing();
    }

    private void AutoHeight_Click(object sender, RoutedEventArgs e) => SetAutomaticHeight(true);
    private void ManualHeight_Click(object sender, RoutedEventArgs e) => SetAutomaticHeight(false);

    private void ContentRowsMeasured(object? sender, EventArgs e)
    {
        if (sender is not PicketItemsPanel panel) return;
        _contentRowsHeight = panel.TwoRowHeight;
        QueueContentSizing();
    }

    private void WatchContentItems()
    {
        foreach (var removed in _sizingItems.Where(item => !Items.Contains(item)).ToArray())
        {
            removed.PropertyChanged -= ContentItemChanged;
            _sizingItems.Remove(removed);
        }
        foreach (var item in Items)
            if (_sizingItems.Add(item)) item.PropertyChanged += ContentItemChanged;
    }

    private void StopWatchingContentItems()
    {
        foreach (var item in _sizingItems) item.PropertyChanged -= ContentItemChanged;
        _sizingItems.Clear();
    }

    private void ContentItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PicketItem.StatusText) or nameof(PicketItem.IsLarge) or nameof(PicketItem.LabelText))
            QueueContentSizing();
    }

    private void QueueContentSizing()
    {
        if (_autoSizeRows && BodyScroll != null && Application.Current is App { IsShuttingDown: false } app)
            app.Frames.Request(RefreshContentSizing);
    }

    private void RefreshContentSizing()
    {
        if (!_autoSizeRows || _isLoading || _isRollAnimating || _resizeCluster != null || _allowClose) return;
        var group = ComputeTouchingCluster();
        if (group.Count == 0) return;
        if (group.Any(p => Math.Abs(p._expandedHeight - p.MeasureContentHeight(group[0].Width)) > 0.5))
            NormalizeConnectedGroup();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == FontSizeProperty || e.Property == FontFamilyProperty) QueueContentSizing();
    }
}
