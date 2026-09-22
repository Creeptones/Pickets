using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace Pickets;

public enum ItemKind
{
    File,   // real filesystem entry (file, folder, shortcut)
    Label,  // inline section header -- no path, no icon, just user text
}

public class PicketItem : INotifyPropertyChanged
{
    public string Path { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public bool IsFolder { get; private set; }
    public ItemKind Kind { get; init; } = ItemKind.File;
    public BitmapSource? Icon { get; private set; }

    /// <summary>
    /// The icon's position on the real desktop before we hid it. Null if we never
    /// captured it (e.g. the file came from somewhere other than the desktop).
    /// Used to put the icon back in place when the user removes it from the picket.
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

    private ReferenceStatus _status = ReferenceStatus.Checking;
    public ReferenceStatus Status => _status;
    public string StatusText => _status switch
    {
        ReferenceStatus.Checking => "Checking availability…",
        ReferenceStatus.Missing => "Not found — locate or check again",
        ReferenceStatus.Unavailable => "Unavailable — reconnect or check again",
        _ => "",
    };
    public string AccessibleName => Kind == ItemKind.Label ? LabelText
        : string.IsNullOrEmpty(StatusText) ? DisplayName : $"{DisplayName}, {StatusText}";
    public bool IsMissing
    {
        get => _status != ReferenceStatus.Available;
        set
        {
            SetStatus(value ? ReferenceStatus.Unavailable : ReferenceStatus.Available);
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
            OnPropertyChanged(nameof(AccessibleName));
        }
    }

    public double CellWidth  => IsLarge ? 168 : 84;
    public double CellHeight => (IsLarge ? 168 : 100) + (string.IsNullOrEmpty(StatusText) ? 0 : 32);
    public double IconSize   => IsLarge ? 96 : 40;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public static PicketItem FromPath(string path, bool isFolder = false)
    {
        var name = System.IO.Path.GetFileName(path);
        if (string.IsNullOrEmpty(name)) name = path;
        var ext = System.IO.Path.GetExtension(path);
        var display = (!isFolder && ext.Equals(".lnk", System.StringComparison.OrdinalIgnoreCase))
            ? System.IO.Path.GetFileNameWithoutExtension(path)
            : name;

        return new PicketItem
        {
            Path = path,
            DisplayName = display,
            IsFolder = isFolder,
            Kind = ItemKind.File,
            Icon = null,
        };
    }

    private void SetStatus(ReferenceStatus status)
    {
        _status = status;
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(IsMissing));
        OnPropertyChanged(nameof(AccessibleName));
        OnPropertyChanged(nameof(CellHeight));
    }

    private Task? _refreshTask;
    internal Task RefreshAsync() => _refreshTask is { IsCompleted: false } ? _refreshTask : _refreshTask = RefreshCoreAsync();

    private async Task RefreshCoreAsync()
    {
        SetStatus(ReferenceStatus.Checking);
        var result = await FileReferenceProbe.CheckAsync(Path);
        if (result.Status == ReferenceStatus.Available)
        {
            IsFolder = result.IsFolder;
            OnPropertyChanged(nameof(IsFolder));
        }
        SetStatus(result.Status);
        if (result.Status == ReferenceStatus.Available && Icon == null)
        {
            Icon = await ShellIconExtractor.GetIconAsync(Path);
            OnPropertyChanged(nameof(Icon));
        }
    }

    public static PicketItem CreateLabel(string text)
    {
        return new PicketItem
        {
            Kind = ItemKind.Label,
            DisplayName = text,
            LabelText = text,
        };
    }
}
