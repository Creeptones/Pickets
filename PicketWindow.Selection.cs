using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Pickets;

public partial class PicketWindow
{
    private Point? _boxStart;
    private Point _boxPointer;
    private bool _boxDragging;
    private ModifierKeys _boxModifiers;
    private HashSet<PicketItem> _boxOriginal = new();
    private TimeSpan? _boxFrame;

    private void Selection_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // Item drags, empty-state buttons, and scrollbar gestures keep their normal behavior.
        for (var node = e.OriginalSource as DependencyObject; node != null && node != BodyArea;
             node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
            if (node is ListBoxItem or ButtonBase or ScrollBar) return;
        BeginBoxSelection(e.GetPosition(BodyArea), Keyboard.Modifiers);
        if (_boxStart == null) return;
        if (!Mouse.Capture(BodyArea, CaptureMode.Element)) EndBoxSelection(cancel: true);
        e.Handled = true;
    }

    internal void BeginBoxSelection(Point point, ModifierKeys modifiers)
    {
        var viewport = SelectionViewport();
        if (!viewport.Contains(point)) return;
        ResetItemDragCandidate();
        _boxOriginal = ItemsHost.SelectedItems.Cast<PicketItem>().ToHashSet();
        _boxModifiers = modifiers;
        _boxPointer = point;
        _boxStart = new Point(point.X, point.Y + BodyScroll.VerticalOffset);
        _boxDragging = false;
        _boxFrame = null;
        ItemsHost.Focus();
        if ((modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == 0) ItemsHost.UnselectAll();
        if (Application.Current is App app) app.Frames.AddAnimation(SelectionFrame);
    }

    private Rect SelectionViewport()
    {
        if (BodyScroll.Template.FindName("PART_ScrollContentPresenter", BodyScroll) is FrameworkElement presenter)
            return new Rect(presenter.TranslatePoint(new Point(), BodyArea), presenter.RenderSize);
        return new Rect(0, 0, Math.Max(0, BodyArea.ActualWidth - SystemParameters.VerticalScrollBarWidth), BodyArea.ActualHeight);
    }

    private void Selection_MouseMove(object sender, MouseEventArgs e)
    {
        if (_boxStart == null) return;
        if (e.LeftButton != MouseButtonState.Pressed) EndBoxSelection();
        else UpdateBoxSelection(e.GetPosition(BodyArea));
        e.Handled = true;
    }

    internal void UpdateBoxSelection(Point point)
    {
        if (_boxStart is not Point start) return;
        _boxPointer = point;
        var viewport = SelectionViewport();
        point = new Point(Math.Clamp(point.X, viewport.Left, viewport.Right), Math.Clamp(point.Y, viewport.Top, viewport.Bottom));
        var contentPoint = new Point(point.X, point.Y + BodyScroll.VerticalOffset);
        if (!_boxDragging && Math.Abs(contentPoint.X - start.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(contentPoint.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _boxDragging = true;
        var rect = new Rect(start, contentPoint);
        foreach (var item in Items)
        {
            if (ItemsHost.ItemContainerGenerator.ContainerFromItem(item) is not FrameworkElement container) continue;
            var position = container.TranslatePoint(new Point(), BodyArea);
            position.Y += BodyScroll.VerticalOffset;
            var hit = rect.IntersectsWith(new Rect(position, container.RenderSize));
            item.IsSelected = _boxModifiers.HasFlag(ModifierKeys.Control) ? _boxOriginal.Contains(item) ^ hit
                : hit || (_boxModifiers.HasFlag(ModifierKeys.Shift) && _boxOriginal.Contains(item));
        }
        rect.Offset(0, -BodyScroll.VerticalOffset);
        rect.Intersect(viewport);
        SelectionBox.Visibility = rect.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
        if (rect.IsEmpty) return;
        Canvas.SetLeft(SelectionBox, rect.Left);
        Canvas.SetTop(SelectionBox, rect.Top);
        SelectionBox.Width = rect.Width;
        SelectionBox.Height = rect.Height;
    }

    private void SelectionFrame(TimeSpan time)
    {
        var elapsed = _boxFrame is TimeSpan last ? Math.Clamp((time - last).TotalSeconds, 0, 0.05) : 0;
        _boxFrame = time;
        if (!_boxDragging || _boxStart == null) return;
        var viewport = SelectionViewport();
        var direction = _boxPointer.Y < viewport.Top + 20 ? -1 : _boxPointer.Y > viewport.Bottom - 20 ? 1 : 0;
        if (direction == 0) return;
        BodyScroll.ScrollToVerticalOffset(BodyScroll.VerticalOffset + direction * elapsed * 480);
        BodyScroll.UpdateLayout();
        UpdateBoxSelection(_boxPointer);
    }

    private void Selection_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_boxStart == null) return;
        UpdateBoxSelection(e.GetPosition(BodyArea));
        EndBoxSelection();
        e.Handled = true;
    }

    private void Selection_LostCapture(object sender, MouseEventArgs e)
    {
        if (_boxStart != null && Mouse.Captured != BodyArea) EndBoxSelection();
    }

    internal void EndBoxSelection(bool cancel = false)
    {
        if (_boxStart == null) return;
        _boxStart = null;
        _boxDragging = false;
        if (Application.Current is App app) app.Frames.RemoveAnimation(SelectionFrame);
        SelectionBox.Visibility = Visibility.Collapsed;
        if (cancel) foreach (var item in Items) item.IsSelected = _boxOriginal.Contains(item);
        _boxOriginal.Clear();
        if (Mouse.Captured == BodyArea) Mouse.Capture(null);
    }

    private void Item_RightMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PicketItem item }) return;
        if (!item.IsSelected) ItemsHost.SelectedItem = item;
        // Preserve a selected group while the context menu opens.
        e.Handled = true;
    }

    internal PicketItem[] ActionItems(PicketItem clicked)
        => clicked.IsSelected ? Items.Where(i => i.IsSelected).ToArray() : [clicked];
}
