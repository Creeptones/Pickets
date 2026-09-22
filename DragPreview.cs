using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Pickets;

internal static class DragPreview
{
    internal static BitmapSource Render(PicketItem item, DpiScale dpi)
    {
        var iconSize = item.IconSize;
        const double width = 160;
        var height = iconSize + 44;
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            var bounds = new Rect((width - iconSize) / 2, 0, iconSize, iconSize);
            if (item.Icon != null) drawing.DrawImage(item.Icon, bounds);
            else
            {
                drawing.DrawRoundedRectangle(Brushes.DimGray, new Pen(Brushes.White, 2),
                    new Rect(bounds.Left + iconSize * 0.2, 2, iconSize * 0.6, iconSize - 4), 2, 2);
            }
            var text = new FormattedText(item.DisplayName, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(SystemFonts.MessageFontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                12, Brushes.White, dpi.PixelsPerDip)
            {
                MaxTextWidth = width - 8, MaxLineCount = 2, Trimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Center
            };
            text.SetForegroundBrush(Brushes.Black);
            drawing.DrawText(text, new Point(5, iconSize + 5));
            text.SetForegroundBrush(Brushes.White);
            drawing.DrawText(text, new Point(4, iconSize + 4));
        }
        var result = new RenderTargetBitmap((int)Math.Ceiling(width * dpi.DpiScaleX),
            (int)Math.Ceiling(height * dpi.DpiScaleY), 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
        result.Render(visual);
        result.Freeze();
        return result;
    }

    internal static DragDropEffects DropEffect(bool overBody, bool internalItem, bool files, DragDropEffects allowed)
    {
        if (!overBody) return DragDropEffects.None;
        if (internalItem) return allowed & DragDropEffects.Move;
        if (!files) return DragDropEffects.None;
        // External drags create references; never advertise a filesystem Move.
        if ((allowed & DragDropEffects.Link) != 0) return DragDropEffects.Link;
        return allowed & DragDropEffects.Copy;
    }
}
