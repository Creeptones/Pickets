using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Pickets;

public partial class PicketWindow
{
    internal PicketItem[] TransferReferences(IEnumerable<PicketItem> items, PicketWindow target)
    {
        if (target == this) return [];
        var moved = new List<PicketItem>();
        foreach (var item in items.ToArray())
        {
            if (!Items.Contains(item) || (item.Kind == ItemKind.File && target.Items.Any(i => i.Kind == ItemKind.File &&
                string.Equals(i.Path, item.Path, StringComparison.OrdinalIgnoreCase)))) continue;
            Items.Remove(item);
            item.IsSelected = false;
            target.Items.Add(item);
            moved.Add(item);
        }
        return moved.ToArray();
    }

    private void MoveReferencesTo(IEnumerable<PicketItem> items, PicketWindow target)
    {
        var moved = TransferReferences(items, target);
        if (moved.Length == 0) return;
        target.SetExpanded(true);
        target.FocusForKeyboard();
        target.FocusItem(target.Items.IndexOf(moved[0]));
        foreach (var item in moved) item.IsSelected = true;
    }

    private void AddFiles_Click(object sender, RoutedEventArgs e) => AddFiles();
    private void AddFolder_Click(object sender, RoutedEventArgs e) => AddFolder();

    private void AddFiles()
    {
        var dialog = new OpenFileDialog { Title = "Add file references to Pickets", Multiselect = true, CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) AddReferences(dialog.FileNames, captureDesktop: false);
    }

    private void AddFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Add folder references to Pickets", Multiselect = true };
        if (dialog.ShowDialog(this) == true) AddReferences(dialog.FolderNames, captureDesktop: false, folders: true);
    }

    internal void AddReferences(IEnumerable<string> paths, bool captureDesktop, bool folders = false)
    {
        foreach (var path in paths)
        {
            if (Items.Any(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase))) continue;
            var item = PicketItem.FromPath(path, folders);
            // References from the Add dialogs leave even desktop icons alone. A desktop drag keeps
            // the existing capture behavior, but never takes a second ownership of an already hidden icon.
            var alreadyCaptured = Application.Current is App app && app.Pickets.SelectMany(p => p.Items)
                .Any(i => i.OriginalDesktopPos.HasValue && string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase));
            if (captureDesktop && !alreadyCaptured) item.OriginalDesktopPos = DesktopIconHider.Hide(path);
            Items.Add(item);
            _ = item.RefreshAsync();
        }
        SetExpanded(true);
    }

    private async void CheckReferences_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in Items.Where(i => i.Kind == ItemKind.File).ToList())
            await item.RefreshAsync();
    }

    private async void ItemMenu_Check_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: PicketItem item })
            foreach (var reference in ActionItems(item).Where(i => i.Kind == ItemKind.File)) await reference.RefreshAsync();
    }

    private void ItemMenu_Locate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: PicketItem item }) return;
        if (item.IsFolder) ItemMenu_LocateFolder_Click(sender, e);
        else ItemMenu_LocateFile_Click(sender, e);
    }

    private void ItemMenu_LocateFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: PicketItem item }) return;
        var dialog = new OpenFileDialog { Title = "Locate this reference's file", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) ReplaceReference(item, dialog.FileName, false);
    }

    private void ItemMenu_LocateFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: PicketItem item }) return;
        var dialog = new OpenFolderDialog { Title = "Locate this reference's folder" };
        if (dialog.ShowDialog(this) == true) ReplaceReference(item, dialog.FolderName, true);
    }

    internal void ReplaceReference(PicketItem item, string path, bool folder)
    {
        if (Items.Any(i => i != item && string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, "That reference is already in this picket.", "Pickets");
            return;
        }
        if (!ReleaseReferenceCapture(item)) return;
        var index = Items.IndexOf(item);
        if (index < 0) return;
        var replacement = PicketItem.FromPath(path, folder);
        replacement.IsLarge = item.IsLarge;
        Items[index] = replacement;
        _ = replacement.RefreshAsync();
        FocusItem(index);
    }

    private bool ReleaseReferenceCapture(PicketItem item)
    {
        if (!item.OriginalDesktopPos.HasValue) return true;
        if (item.Status == ReferenceStatus.Missing || DesktopIconHider.Restore(item.Path, item.OriginalDesktopPos.Value))
        {
            item.OriginalDesktopPos = null;
            return true;
        }
        MessageBox.Show(this, "Windows could not restore the original desktop icon. The reference has been kept so you can check its availability and try again.",
            "Pickets", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    internal void RemoveReferences(IEnumerable<PicketItem> items)
    {
        var index = ItemsHost.SelectedIndex;
        foreach (var item in items.ToList())
            if (ReleaseReferenceCapture(item)) Items.Remove(item);
        if (Items.Count > 0) FocusItem(Math.Clamp(index, 0, Items.Count - 1));
        else TitleToggle.Focus();
    }

    internal bool TryReleaseAllReferences()
    {
        foreach (var item in Items)
            if (!ReleaseReferenceCapture(item)) return false;
        return true;
    }

    private static async void LaunchItem(PicketItem item)
    {
        if (item.Kind != ItemKind.File) return;
        await item.RefreshAsync();
        if (item.IsMissing)
        {
            MessageBox.Show($"This reference is currently unavailable:\n{item.Path}\n\nReconnect its drive or network location and choose Check again. If it moved, use Locate…. The reference is still saved.",
                "Pickets", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try { Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show($"Could not open:\n{item.Path}\n\n{ex.Message}", "Pickets", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}
