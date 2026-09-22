using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Pickets;

internal sealed record ReferenceMatch(PicketWindow Picket, PicketItem Item)
{
    public string Name => Item.Kind == ItemKind.Label ? Item.LabelText : Item.DisplayName;
    public string Location => Picket.DisplayTitle;
    public string Path => Item.Path;
    public override string ToString() => Name + " — " + Location;
}

public partial class QuickFindWindow : AccessibleDialogWindow
{
    private readonly App _app;
    internal QuickFindWindow(App app)
    {
        _app = app;
        InitializeComponent();
        Loaded += (_, _) => Query.Focus();
    }

    internal static ReferenceMatch[] Find(IEnumerable<PicketWindow> pickets, string query)
    {
        var terms = query.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length == 0) return [];
        return pickets.SelectMany(p => p.Items.Select(item => new ReferenceMatch(p, item)))
            .Where(match => terms.All(term => (match.Name + " " + match.Location + " " + match.Path)
                .Contains(term, StringComparison.OrdinalIgnoreCase))).ToArray();
    }

    private void Query_Changed(object sender, TextChangedEventArgs e)
    {
        if (Results == null) return;
        var matches = Find(_app.Pickets, Query.Text);
        Results.ItemsSource = matches.Take(200).ToArray();
        Results.SelectedIndex = matches.Length == 0 ? -1 : 0;
        SearchStatus.Text = string.IsNullOrWhiteSpace(Query.Text) ? "Type a name to find a reference."
            : matches.Length == 0 ? "No matching references."
            : matches.Length > 200 ? $"{matches.Length} matches — showing 200. Refine your search."
            : $"{matches.Length} match{(matches.Length == 1 ? "" : "es")}. Enter jumps to the selection.";
    }
    private void Results_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GoButton != null) GoButton.IsEnabled = Results.SelectedItem != null;
    }
    private void Search_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        else if (e.Key == Key.Enter) { Go(); e.Handled = true; }
        else if (e.Key == Key.Down && Query.IsKeyboardFocused && Results.SelectedItem != null)
        {
            Results.Focus();
            if (Results.ItemContainerGenerator.ContainerFromIndex(0) is ListBoxItem first) first.Focus();
            e.Handled = true;
        }
    }
    private void Go_Click(object sender, RoutedEventArgs e) => Go();
    private void Result_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(Results, e.OriginalSource as DependencyObject) is ListBoxItem) Go();
    }
    private void Go()
    {
        if (Results.SelectedItem is not ReferenceMatch match) return;
        if (!_app.Pickets.Contains(match.Picket) || !match.Picket.Items.Contains(match.Item)) { Query_Changed(this, null!); return; }
        Close();
        match.Picket.RevealReference(match.Item);
    }
}
