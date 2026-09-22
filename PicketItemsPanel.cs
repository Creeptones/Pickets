using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace Pickets;

/// <summary>Wraps measured tiles and reports the height of at most two icon rows.</summary>
public sealed class PicketItemsPanel : WrapPanel
{
    private readonly List<Rect> _bounds = new();
    public double TwoRowHeight { get; private set; }
    public event EventHandler? RowsMeasured;

    protected override Size MeasureOverride(Size constraint)
    {
        var width = double.IsInfinity(constraint.Width) ? 420 : Math.Max(1, constraint.Width);
        _bounds.Clear();
        var y = 0.0;
        var rowWidth = 0.0;
        var rowHeight = 0.0;
        var firstInRow = 0;
        var iconRows = 0;
        var labelRows = 0;
        var preferred = 0.0;
        var capped = false;
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            child.Measure(new Size(width, double.PositiveInfinity));
            var size = child.DesiredSize;
            var label = child is FrameworkElement { DataContext: PicketItem { Kind: ItemKind.Label } };
            if (label)
            {
                FinishRow(i);
                _bounds.Add(new Rect(0, y, width, size.Height));
                y += size.Height;
                if (!capped && iconRows < 2 && labelRows < 2) preferred = y;
                else capped = true;
                labelRows++;
                firstInRow = i + 1;
                continue;
            }
            if (rowWidth > 0 && rowWidth + size.Width > width + 0.000001) FinishRow(i);
            _bounds.Add(new Rect(rowWidth, y, size.Width, size.Height));
            rowWidth += size.Width;
            rowHeight = Math.Max(rowHeight, size.Height);
        }
        FinishRow(InternalChildren.Count);
        if (Math.Abs(TwoRowHeight - preferred) > 0.01)
        {
            TwoRowHeight = preferred;
            RowsMeasured?.Invoke(this, EventArgs.Empty);
        }
        return new Size(width, y);

        void FinishRow(int end)
        {
            if (rowWidth == 0) return;
            for (var j = firstInRow; j < end; j++)
                _bounds[j] = new Rect(_bounds[j].X, y, _bounds[j].Width, rowHeight);
            y += rowHeight;
            iconRows++;
            if (!capped && iconRows <= 2) preferred = y;
            else capped = true;
            firstInRow = end;
            rowWidth = rowHeight = 0;
        }
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        for (var i = 0; i < InternalChildren.Count && i < _bounds.Count; i++)
            InternalChildren[i].Arrange(_bounds[i]);
        return finalSize;
    }
}
