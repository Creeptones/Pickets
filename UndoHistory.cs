using System;
using System.Collections.Generic;

namespace Pickets;

internal sealed class UndoHistory
{
    private readonly List<(string Description, Func<bool> Restore)> _entries = new();
    internal bool IsRestoring { get; private set; }
    internal string? Description => _entries.Count == 0 ? null : _entries[^1].Description;
    internal void Record(string description, Func<bool> restore)
    {
        if (IsRestoring) return;
        if (_entries.Count == 30) _entries.RemoveAt(0);
        _entries.Add((description, restore));
    }
    internal bool Undo()
    {
        if (IsRestoring || _entries.Count == 0) return false;
        IsRestoring = true;
        try
        {
            if (!_entries[^1].Restore()) return false; // Keep a failed operation available for retry.
            _entries.RemoveAt(_entries.Count - 1);
            return true;
        }
        finally { IsRestoring = false; }
    }
    internal void Clear() => _entries.Clear();
}
