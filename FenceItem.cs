using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace Pickets;

public enum ItemKind
{
    File,   // real filesystem entry (file, folder, shortcut)
    Label,  // inline section header -- no path, no icon, just user text
}

public class FenceItem : INotifyPropertyChanged
{
    public string Path { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public bool IsFolder { get; init; }
    public ItemKind Kind { get; init; } = ItemKind.File;
    public BitmapSource? Icon { get; init; }

    /// <summary>
    /// The icon's position on the real desktop before we hid it. Null if we never
    /// captured it (e.g. the file came from somewhere other than the desktop).
    /// Used to put the icon back in place when the user removes it from the fence.
    /// </summary>
    public POINT? OriginalDesktopPos { get; set; }

    private bool _isLarge;
    public bool IsLarge
    {
        get => _isLarge;
        set
        {
            if (_isLarge == value) return;
            _isLarge = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CellWidth));
            OnPropertyChanged(nameof(CellHeight));
            OnPropertyChanged(nameof(IconSize));
        }
    }

    private bool _isMissing;
    public bool IsMissing
    {
        get => _isMissing;
        set
        {
            if (_isMissing == value) return;
            _isMissing = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CellOpacity));
        }
    }

    // Transient selection state (not persisted). Drives the hover/selected highlight in the
    // item template so clicking an icon behaves the way users expect from Explorer.
    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    private string _labelText = "";
    /// <summary>Mutable label text. Files ignore this and use DisplayName instead.</summary>
    public string LabelText
    {
        get => _labelText;
        set
        {
            if (_labelText == value) return;
            _labelText = value;
            OnPropertyChanged();
        }
    }

    public double CellWidth  => IsLarge ? 168 : 84;
    public double CellHeight => IsLarge ? 168 : 84;
    public double IconSize   => IsLarge ? 96 : 40;
    public double CellOpacity => IsMissing ? 0.4 : 1.0;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public static FenceItem FromPath(string path)
    {
        var name = System.IO.Path.GetFileName(path);
        if (string.IsNullOrEmpty(name)) name = path;
        var isFolder = Directory.Exists(path);
        var ext = System.IO.Path.GetExtension(path);
        var display = (!isFolder && ext.Equals(".lnk", System.StringComparison.OrdinalIgnoreCase))
            ? System.IO.Path.GetFileNameWithoutExtension(path)
            : name;

        return new FenceItem
        {
            Path = path,
            DisplayName = display,
            IsFolder = isFolder,
            Kind = ItemKind.File,
            Icon = ShellIconExtractor.GetIcon(path)
        };
    }

    public static FenceItem CreateLabel(string text)
    {
        return new FenceItem
        {
            Kind = ItemKind.Label,
            DisplayName = text,
            LabelText = text,
        };
    }
}
