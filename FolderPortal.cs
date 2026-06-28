using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Pickets;

/// <summary>
/// Live mirror of a folder's top-level contents. Owns a FileSystemWatcher and marshals every
/// change event to the UI thread. Hidden files are skipped. The underlying folder is never
/// modified by this class -- we only observe it.
/// </summary>
public sealed class FolderPortal : IDisposable
{
    public string FolderPath { get; }

    private readonly Dispatcher _uiDispatcher;
    private FileSystemWatcher? _watcher;
    private bool _disposed;

    public event Action<string>? ItemAdded;
    public event Action<string>? ItemRemoved;
    public event Action<string, string>? ItemRenamed;

    public FolderPortal(string folderPath, Dispatcher uiDispatcher)
    {
        FolderPath = folderPath;
        _uiDispatcher = uiDispatcher;
    }

    /// <summary>Starts the watcher, then enumerates existing entries on a background thread and
    /// dispatches ItemAdded for each. Watcher is armed before enumeration so files appearing
    /// mid-scan still surface via Created; the UI layer dedupes by path.</summary>
    public void Start()
    {
        if (!Directory.Exists(FolderPath))
        {
            Logger.Log($"FolderPortal: target folder does not exist: '{FolderPath}'");
            return;
        }

        try
        {
            _watcher = new FileSystemWatcher(FolderPath)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                IncludeSubdirectories = false,
                InternalBufferSize = 65536,
            };
            _watcher.Created += OnCreated;
            _watcher.Deleted += OnDeleted;
            _watcher.Renamed += OnRenamed;
            _watcher.Error   += OnError;
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex)
        {
            Logger.Log($"FolderPortal: failed to start watcher for '{FolderPath}': {ex.Message}");
            _watcher = null;
        }

        Task.Run(() =>
        {
            List<string> entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(FolderPath)
                    .Where(p => !IsHidden(p))
                    .ToList();
            }
            catch (Exception ex)
            {
                Logger.Log($"FolderPortal: initial scan failed for '{FolderPath}': {ex.Message}");
                return;
            }

            _uiDispatcher.BeginInvoke(() =>
            {
                foreach (var p in entries) ItemAdded?.Invoke(p);
            });
        });
    }

    private void OnCreated(object sender, FileSystemEventArgs e)
    {
        if (IsHidden(e.FullPath)) return;
        _uiDispatcher.BeginInvoke(() => ItemAdded?.Invoke(e.FullPath));
    }

    private void OnDeleted(object sender, FileSystemEventArgs e)
    {
        _uiDispatcher.BeginInvoke(() => ItemRemoved?.Invoke(e.FullPath));
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        _uiDispatcher.BeginInvoke(() =>
        {
            // If the new name is hidden (e.g. renamed with a leading dot, or attribute set
            // separately), treat it as a removal so the stale entry disappears from the picket.
            if (IsHidden(e.FullPath))
            {
                ItemRemoved?.Invoke(e.OldFullPath);
                return;
            }
            ItemRenamed?.Invoke(e.OldFullPath, e.FullPath);
        });
    }

    private void OnError(object sender, ErrorEventArgs e)
    {
        // v2 work: buffer-overflow recovery (full rescan + diff). For now we log and keep going.
        Logger.Log($"FolderPortal: watcher error on '{FolderPath}': {e.GetException().Message}");
    }

    private static bool IsHidden(string path)
    {
        try
        {
            var attr = File.GetAttributes(path);
            return (attr & FileAttributes.Hidden) != 0;
        }
        catch
        {
            // If we can't read attributes, assume not hidden rather than hide it from the user.
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_watcher != null)
        {
            try { _watcher.EnableRaisingEvents = false; } catch { }
            _watcher.Created -= OnCreated;
            _watcher.Deleted -= OnDeleted;
            _watcher.Renamed -= OnRenamed;
            _watcher.Error   -= OnError;
            _watcher.Dispose();
            _watcher = null;
        }
    }
}
