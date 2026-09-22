using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Pickets;

public partial class PicketWindow
{
    internal void FocusForKeyboard()
    {
        if (Application.Current is App { PicketsHidden: true } app) app.ShowPickets();
        RevealOnStackPage();
        Show();
        Activate();
        var hwnd = new WindowInteropHelper(this).Handle;
        WindowInterop.SetForegroundWindow(hwnd);
        WindowInterop.SetFocus(hwnd);
        Keyboard.Focus(TitleToggle);
    }

    private void UpdateAccessibleTitle()
    {
        Title = TitleText.Text + " — Pickets";
        AutomationProperties.SetName(TitleToggle, TitleText.Text);
        AutomationProperties.SetName(ItemsHost, TitleText.Text + " references");
    }

    private void FocusItem(int index)
    {
        if (Items.Count == 0) { TitleToggle.Focus(); return; }
        var item = Items[Math.Clamp(index, 0, Items.Count - 1)];
        ItemsHost.SelectedItem = item;
        ItemsHost.ScrollIntoView(item);
        ItemsHost.UpdateLayout();
        if (ItemsHost.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem container)
        {
            container.Focus();
            container.BringIntoView();
        }
    }

    private void TitleToggle_Click(object sender, RoutedEventArgs e) => ToggleCollapse();
    private void MoveSectionEarlier_Click(object sender, RoutedEventArgs e) => MoveSection(-1);
    private void MoveSectionLater_Click(object sender, RoutedEventArgs e) => MoveSection(1);
    private void KeyboardHelp_Click(object sender, RoutedEventArgs e) => ShowKeyboardHelp();
    private void ShortcutSettings_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current is App app) app.ConfigureFocusShortcut(this);
    }

    internal static void ShowKeyboardHelp()
        => new KeyboardHelpWindow(Application.Current is App app ? app.CurrentFocusShortcut : "Ctrl+Alt+D").Show();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;
        var ctrl = mods.HasFlag(ModifierKeys.Control);
        var shift = mods.HasFlag(ModifierKeys.Shift);
        var alt = mods.HasFlag(ModifierKeys.Alt);
        if (key == Key.Escape && _titleGesture.IsPending)
        {
            CancelTitleGesture();
            e.Handled = true;
            return;
        }
        if (key == Key.F1) ShowKeyboardHelp();
        else if (ctrl && key is Key.PageUp or Key.PageDown) ChangeStackPage(key == Key.PageUp ? -1 : 1);
        else if (key == Key.Tab && ctrl && Application.Current is App app) app.FocusNextPicket(this, shift ? -1 : 1);
        else if (key == Key.O && ctrl && !alt) { if (shift) AddFolder(); else AddFiles(); }
        else if (key == Key.N && ctrl && Application.Current is App createApp) createApp.CreatePicket(Left + 30, Top + 30, this);
        else if (key == Key.F2) BeginRename();
        else if (key == Key.Escape) { SetExpanded(false); TitleToggle.Focus(); }
        else if (key == Key.Delete && ItemsHost.IsKeyboardFocusWithin) RemoveReferences(ItemsHost.SelectedItems.Cast<PicketItem>());
        else if (key == Key.Enter && ItemsHost.IsKeyboardFocusWithin && ItemsHost.SelectedItem is PicketItem item) LaunchItem(item);
        else if (key is Key.Up or Key.Down && ctrl && shift && !alt)
        {
            var delta = key == Key.Up ? -1 : 1;
            if (ItemsHost.IsKeyboardFocusWithin && ItemsHost.SelectedIndex >= 0)
            {
                var from = ItemsHost.SelectedIndex;
                var to = Math.Clamp(from + delta, 0, Items.Count - 1);
                Items.Move(from, to);
                FocusItem(to);
            }
            else MoveSection(delta);
        }
        else if (key is Key.Left or Key.Right or Key.Up or Key.Down && alt)
        {
            if (_isRollAnimating) return;
            var dx = key == Key.Left ? -10 : key == Key.Right ? 10 : 0;
            var dy = key == Key.Up ? -10 : key == Key.Down ? 10 : 0;
            var group = ComputeTouchingCluster();
            if (ctrl)
            {
                var width = Math.Max(MinWidth, group[0].Width + dx);
                foreach (var member in group) member._expandedHeight = Math.Max(96, member._expandedHeight + dy);
                ApplyGroupBounds(group, width, group[0]._expandedHeight);
            }
            else
            {
                foreach (var member in group) { member.Left += dx; member.Top += dy; }
                NormalizeConnectedGroup();
            }
            RaiseLayoutChanged();
        }
        else if (key == Key.Down && TitleToggle.IsKeyboardFocused && mods == ModifierKeys.None)
        {
            SetExpanded(true);
            Dispatcher.BeginInvoke(() => FocusItem(0), DispatcherPriority.Input);
        }
        else return;
        e.Handled = true;
    }

    private void AccessibilitySettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.HighContrast)) Dispatcher.Invoke(ApplyVisuals);
    }

    internal void ApplyHighContrastVisuals()
    {
        OuterShell.Background = TitleShell.Background = SystemColors.WindowBrush;
        OuterShell.BorderBrush = SystemColors.WindowTextBrush;
        TitleText.Foreground = TitleEditBox.Foreground = TitleEditBox.CaretBrush = SystemColors.WindowTextBrush;
        Resources["PicketTitleForeground"] = Resources["PicketItemForeground"] = SystemColors.WindowTextBrush;
        Resources["PicketControlHoverBrush"] = SystemColors.ControlBrush;
        Resources["PicketSelectionBrush"] = SystemColors.HighlightBrush;
        Resources["PicketSelectionForeground"] = SystemColors.HighlightTextBrush;
        Resources["PicketFocusBorder"] = SystemColors.WindowTextBrush;
        Resources["PicketEditorBackground"] = SystemColors.WindowBrush;
        Resources["PicketEditorBorder"] = SystemColors.WindowTextBrush;
        Resources["PicketTitleShadowColor"] = Resources["PicketItemShadowColor"] = Colors.Transparent;
        WindowBlur.Apply(new WindowInteropHelper(this).Handle, false, Colors.Transparent);
    }
}
