using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Pickets;

public class AccessibleDialogWindow : Window
{
    public AccessibleDialogWindow()
    {
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Pickets;component/DialogStyles.xaml", UriKind.Relative) });
        SetResourceReference(BackgroundProperty, "DialogBackground");
        SetResourceReference(ForegroundProperty, "DialogForeground");
        SetResourceReference(FontSizeProperty, SystemFonts.MessageFontSizeKey);
        ApplyDialogPalette(SystemParameters.HighContrast);
        SystemParameters.StaticPropertyChanged += OnSystemSettingsChanged;
        Closed += (_, _) => SystemParameters.StaticPropertyChanged -= OnSystemSettingsChanged;
        SourceInitialized += (_, _) =>
        {
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowMessages);
            FitDialogToWorkArea();
        };
        Loaded += (_, _) => FitDialogToWorkArea();
    }

    internal void ApplyDialogPalette(bool highContrast)
    {
        Brush BrushOf(string value) => (Brush)new BrushConverter().ConvertFromInvariantString(value)!;
        Resources["DialogBackground"] = highContrast ? SystemColors.WindowBrush : BrushOf("#17191D");
        Resources["DialogForeground"] = highContrast ? SystemColors.WindowTextBrush : BrushOf("#F4F5F7");
        Resources["DialogMuted"] = highContrast ? SystemColors.WindowTextBrush : BrushOf("#C8CFD8");
        Resources["DialogSurface"] = highContrast ? SystemColors.WindowBrush : BrushOf("#24282E");
        Resources["DialogBorder"] = highContrast ? SystemColors.WindowTextBrush : BrushOf("#718397");
        Resources["DialogButton"] = highContrast ? SystemColors.ControlBrush : BrushOf("#2E759C");
        Resources["DialogButtonText"] = highContrast ? SystemColors.ControlTextBrush : Brushes.White;
        Resources["DialogFocus"] = highContrast ? SystemColors.HighlightBrush : BrushOf("#B4DCFF");
    }

    private void OnSystemSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.HighContrast))
            Dispatcher.Invoke(() => ApplyDialogPalette(SystemParameters.HighContrast));
    }

    private IntPtr WindowMessages(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message is 0x02E0 or 0x007E) Dispatcher.BeginInvoke(FitDialogToWorkArea);
        return IntPtr.Zero;
    }

    private void FitDialogToWorkArea()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var work = System.Windows.Forms.Screen.FromHandle(hwnd).WorkingArea;
        var dpi = VisualTreeHelper.GetDpi(this);
        var available = WelcomeSizing.Available(work.Width, work.Height, dpi.DpiScaleX, dpi.DpiScaleY);
        MinWidth = Math.Min(320, available.Width);
        MinHeight = Math.Min(240, available.Height);
        MaxWidth = available.Width;
        MaxHeight = available.Height;
        if (!double.IsNaN(Width)) Width = Math.Min(Width, MaxWidth);
        if (!double.IsNaN(Height)) Height = Math.Min(Height, MaxHeight);
        if (IsLoaded && WindowInterop.GetWindowRect(hwnd, out var bounds))
        {
            var x = Math.Clamp(bounds.left, work.Left, Math.Max(work.Left, work.Right - (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX)));
            var y = Math.Clamp(bounds.top, work.Top, Math.Max(work.Top, work.Bottom - (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY)));
            WindowInterop.SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, 0x0004 | WindowInterop.SWP_NOSIZE | WindowInterop.SWP_NOACTIVATE);
        }
    }
}
