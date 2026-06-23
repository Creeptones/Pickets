using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Pickets;

/// <summary>Minimal single-line text prompt. Code-built so we don't add another XAML pair for a
/// dialog that only exists to name a section label.</summary>
public static class InputDialog
{
    public static string? Show(Window? owner, string title, string prompt, string initialText = "")
    {
        string? result = null;

        var textBox = new TextBox
        {
            Text = initialText,
            Margin = new Thickness(0, 4, 0, 8),
            MinWidth = 280,
        };

        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 72, Margin = new Thickness(4, 0, 0, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 72, Margin = new Thickness(4, 0, 0, 0) };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var root = new StackPanel { Margin = new Thickness(12) };
        root.Children.Add(new TextBlock { Text = prompt });
        root.Children.Add(textBox);
        root.Children.Add(buttons);

        var dialog = new Window
        {
            Title = title,
            Content = root,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = owner,
            ShowInTaskbar = false,
        };

        ok.Click += (_, _) => { result = textBox.Text; dialog.DialogResult = true; };
        textBox.Loaded += (_, _) => { textBox.Focus(); textBox.SelectAll(); };
        textBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { result = textBox.Text; dialog.DialogResult = true; e.Handled = true; }
        };

        return dialog.ShowDialog() == true ? result : null;
    }
}
