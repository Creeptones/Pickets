using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Pickets;

public sealed class KeyboardHelpWindow : AccessibleDialogWindow
{
    public KeyboardHelpWindow(string shortcut)
    {
        Title = "Pickets keyboard help";
        Width = 560;
        Height = 620;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var root = new DockPanel { Margin = new Thickness(20) };
        var close = new Button { Content = "Close", IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,12,0,0) };
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Bottom);
        root.Children.Add(close);
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = "Keyboard help", FontSize = 24, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(new TextBlock { Text = shortcut == "None" ? "Global shortcut is off. Use Focus Pickets from the tray." :
            "Focus Pickets: " + shortcut + ". Press again while focused to hide.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,8,0,16) });
        var entries = new (string Key, string Action)[]
        {
            ("Ctrl+Tab / Ctrl+Shift+Tab", "Next / previous picket, including other stack pages"),
            ("Tab / Shift+Tab", "Move between controls"),
            ("Enter / Space on a title", "Expand / collapse the picket"),
            ("Down on a title", "Open and enter its references"),
            ("Arrow keys", "Navigate references"),
            ("Space / Ctrl+Space", "Select / toggle selection"),
            ("Shift+Arrow", "Extend selection"),
            ("Enter on a reference", "Open it"),
            ("Delete", "Remove selected references; never deletes the files"),
            ("Shift+F10", "Context menu, including Move reference to"),
            ("F2", "Rename this picket"),
            ("Escape", "Collapse the picket and focus its title"),
            ("Ctrl+O / Ctrl+Shift+O", "Add files / folders"),
            ("Ctrl+N", "Create a new picket"),
            ("Ctrl+Shift+Up / Down", "Reorder the focused reference, or picket when its title is focused"),
            ("Alt+Arrow", "Move the entire stack"),
            ("Ctrl+Alt+Arrow", "Resize the entire stack"),
            ("Ctrl+PageUp / PageDown", "Previous / next page of a large stack"),
            ("Alt+F4", "Hide Pickets safely; use the tray or focus shortcut to return"),
            ("F1", "Open this help")
        };
        foreach (var (key, action) in entries)
        {
            content.Children.Add(new TextBlock { Text = key, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,9,0,2) });
            content.Children.Add(new TextBlock { Text = action, TextWrapping = TextWrapping.Wrap });
        }
        var scroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        System.Windows.Automation.AutomationProperties.SetName(scroll, "Keyboard shortcuts");
        root.Children.Add(scroll);
        Content = root;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
    }
}
