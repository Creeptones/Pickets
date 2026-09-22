using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Interop;

namespace Pickets;

public partial class WelcomeWindow : Window
{
    private static readonly Brush ReadyBrush = new SolidColorBrush(Color.FromRgb(0x63, 0xC1, 0x78));
    private static readonly Brush ActionBrush = new SolidColorBrush(Color.FromRgb(0xF0, 0xB3, 0x5A));

    public bool CreateDesktopShortcut => CreateShortcutCheck.IsChecked == true && CreateShortcutCheck.IsEnabled;
    public bool RunAtLogin => RunAtLoginCheck.IsChecked == true;

    public WelcomeWindow(bool shortcutExists, bool runAtLogin)
    {
        InitializeComponent();
        CreateShortcutCheck.IsChecked = OnboardingPreferences.DefaultCreateShortcut(shortcutExists);
        if (shortcutExists)
        {
            CreateShortcutCheck.Content = new TextBlock
            {
                Text = "Pickets desktop shortcut is ready", TextWrapping = TextWrapping.Wrap,
            };
            CreateShortcutCheck.IsEnabled = false;
        }
        RunAtLoginCheck.IsChecked = runAtLogin;
        RefreshDesktopStatus();
        Activated += (_, _) => RefreshDesktopStatus();
        Loaded += (_, _) => FitWorkArea();
        SourceInitialized += (_, _) =>
        {
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowMessages);
            FitWorkArea();
        };
    }

    private void Recheck_Click(object sender, RoutedEventArgs e) => RefreshDesktopStatus();

    private DesktopReadiness RefreshDesktopStatus()
    {
        var readiness = DesktopReadiness.Read();
        SetStatus(AutoArrangeStatus, AutoArrangeIndicator, "Auto arrange", readiness.AutoArrange);
        SetStatus(SnapToGridStatus, SnapToGridIndicator, "Align to grid", readiness.AlignToGrid);
        ReadinessHint.Text = readiness.IsReady ? "Your desktop is ready."
            : readiness.IsUnknown ? "Unable to check Windows Explorer. Wait for your desktop to finish loading, then choose Check again."
            : "Turn both options off in Desktop → View before starting. Choose Check again when you are done.";
        return readiness;
    }

    private static void SetStatus(TextBlock text, Ellipse indicator,
                                  string label, bool? enabled)
    {
        text.Text = enabled == null ? $"{label}: unable to check"
            : enabled == true ? $"{label}: turn off" : $"{label}: ready";
        indicator.Fill = enabled == false ? ReadyBrush : ActionBrush;
    }

    private void GetStarted_Click(object sender, RoutedEventArgs e)
    {
        if (!RefreshDesktopStatus().IsReady)
        {
            ReadinessHint.BringIntoView();
            return;
        }
        DialogResult = true;
        Close();
    }

    private IntPtr WindowMessages(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message is 0x02E0 or 0x007E) // DPI or display configuration changed.
            Dispatcher.BeginInvoke(FitWorkArea);
        return IntPtr.Zero;
    }

    private void FitWorkArea()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var work = System.Windows.Forms.Screen.FromHandle(hwnd).WorkingArea;
        var dpi = VisualTreeHelper.GetDpi(this);
        var available = WelcomeSizing.Available(work.Width, work.Height, dpi.DpiScaleX, dpi.DpiScaleY);
        MinWidth = Math.Min(360, available.Width);
        MinHeight = Math.Min(300, available.Height);
        MaxWidth = available.Width;
        MaxHeight = available.Height;
        Width = Math.Min(Width, MaxWidth);
        Height = Math.Min(Height, MaxHeight);
        if (IsLoaded && WindowInterop.GetWindowRect(hwnd, out var bounds))
        {
            var width = (int)Math.Ceiling(Width * dpi.DpiScaleX);
            var height = (int)Math.Ceiling(Height * dpi.DpiScaleY);
            var x = Math.Clamp(bounds.left, work.Left, Math.Max(work.Left, work.Right - width));
            var y = Math.Clamp(bounds.top, work.Top, Math.Max(work.Top, work.Bottom - height));
            const uint noZOrder = 0x0004;
            WindowInterop.SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0,
                WindowInterop.SWP_NOSIZE | WindowInterop.SWP_NOACTIVATE | noZOrder);
        }
    }
}
