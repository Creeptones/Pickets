using System;
using System.Linq;
using System.Windows;

namespace Pickets;

public partial class App
{
    internal string CurrentFocusShortcut => _layout.FocusShortcut;
    private void MenuAccessibilityChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.HighContrast)) Dispatcher.Invoke(ApplyMenuPalette);
    }

    internal void ApplyMenuPalette()
    {
        var hc = SystemParameters.HighContrast;
        var names = new[] { "Background", "Border", "Foreground", "Hover", "Separator", "DisabledForeground" };
        System.Windows.Media.Brush[] contrast = [SystemColors.MenuBrush, SystemColors.MenuTextBrush, SystemColors.MenuTextBrush,
            SystemColors.HighlightBrush, SystemColors.MenuTextBrush, SystemColors.GrayTextBrush];
        for (var i = 0; i < names.Length; i++)
            Resources["Menu" + names[i] + "Brush"] = hc ? contrast[i]
                : new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)Resources["Menu" + names[i] + "Color"]);
        Resources["MenuHighlightedForegroundBrush"] = hc ? SystemColors.HighlightTextBrush : System.Windows.Media.Brushes.White;
    }

    private void RegisterFocusShortcut()
    {
        if (!FocusShortcut.TryParse(_layout.FocusShortcut, out var shortcut))
            shortcut = new FocusShortcut(WindowInterop.MOD_CONTROL | WindowInterop.MOD_ALT, WindowInterop.VK_D, "Ctrl+Alt+D");
        _layout.FocusShortcut = shortcut.Text;
        if (shortcut.VirtualKey == 0) return;
        _quickHide = new GlobalHotKey(shortcut.Modifiers | 0x4000, shortcut.VirtualKey, FocusOrHidePickets);
        if (!_quickHide.Registered)
            MessageBox.Show("Pickets could not register " + shortcut.Text +
                ". Another app may use it. Use Focus Pickets in the tray, then choose Change focus shortcut.",
                "Pickets shortcut unavailable", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void FocusOrHidePickets()
    {
        if (_pickets.Any(p => p.IsKeyboardFocusWithin))
            HidePickets();
        else FocusPickets();
    }

    internal void FocusPickets()
    {
        ShowPickets();
        _pickets.FirstOrDefault(p => p.IsOnStackPage)?.FocusForKeyboard();
    }

    internal void FocusNextPicket(PicketWindow current, int direction)
    {
        var ordered = _pickets.OrderBy(p => p.GroupId, StringComparer.Ordinal).ThenBy(p => p.GroupOrder).ToList();
        if (ordered.Count == 0) return;
        var index = ordered.IndexOf(current);
        ordered[(index + direction + ordered.Count) % ordered.Count].FocusForKeyboard();
    }

    internal void ConfigureFocusShortcut(Window? owner)
    {
        var input = InputDialog.Show(owner, "Focus shortcut",
            "Enter a shortcut with Ctrl or Alt (for example Ctrl+Alt+D), or None:", _layout.FocusShortcut);
        if (input == null) return;
        if (!FocusShortcut.TryParse(input, out var shortcut))
        {
            MessageBox.Show("Use a letter or function key with Ctrl or Alt. Windows-key and navigation shortcuts are not supported.",
                "Pickets", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (shortcut.Text == _layout.FocusShortcut && (_quickHide?.Registered == true || shortcut.VirtualKey == 0)) return;
        GlobalHotKey? replacement = null;
        if (shortcut.VirtualKey != 0)
        {
            replacement = new GlobalHotKey(shortcut.Modifiers | 0x4000, shortcut.VirtualKey, FocusOrHidePickets);
            if (!replacement.Registered)
            {
                replacement.Dispose();
                MessageBox.Show("That shortcut is unavailable. Your previous setting has been kept.", "Pickets");
                return;
            }
        }
        _quickHide?.Dispose();
        _quickHide = replacement;
        _layout.FocusShortcut = shortcut.Text;
        SaveLayout();
    }
}
