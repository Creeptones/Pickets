using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

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
        CreateShortcutCheck.IsChecked = !shortcutExists;
        if (shortcutExists)
        {
            CreateShortcutCheck.Content = "Pickets desktop shortcut is ready";
            CreateShortcutCheck.IsEnabled = false;
        }
        RunAtLoginCheck.IsChecked = runAtLogin;
        RefreshDesktopStatus();
    }

    private void Recheck_Click(object sender, RoutedEventArgs e) => RefreshDesktopStatus();

    private void RefreshDesktopStatus()
    {
        SetStatus(AutoArrangeStatus, AutoArrangeIndicator, "Auto arrange", DesktopIconHider.IsAutoArrangeOn());
        SetStatus(SnapToGridStatus, SnapToGridIndicator, "Align to grid", DesktopIconHider.IsSnapToGridOn());
    }

    private static void SetStatus(TextBlock text, Ellipse indicator,
                                  string label, bool enabled)
    {
        text.Text = enabled ? $"{label}: turn off" : $"{label}: ready";
        indicator.Fill = enabled ? ActionBrush : ReadyBrush;
    }

    private void GetStarted_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
