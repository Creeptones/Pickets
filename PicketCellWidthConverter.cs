using System;
using System.Globalization;
using System.Windows.Data;

namespace Pickets;

// Keep cells legible when Windows text size grows; label rows reserve their container chrome.
public sealed class PicketCellWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => values.Length == 2 && values[0] is double width && values[1] is double fontSize
            ? Math.Max(96, width) * Math.Max(1, fontSize / 12) : 96.0;
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class PicketLabelWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is double width ? Math.Max(0, width - 6) : 0.0;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class PicketTextHeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => PicketContentSizing.LineHeight(value is double size ? size : 12) *
            (Equals(parameter, "2") || Equals(parameter, "Label") ? 2 : 1) + (Equals(parameter, "Label") ? 10 : 0);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
