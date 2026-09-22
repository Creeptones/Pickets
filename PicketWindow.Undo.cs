using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace Pickets;

public partial class PicketWindow
{
    internal sealed record RemovedReference(PicketItem Item, int Index, POINT? Position);

    internal RemovedReference[] SnapshotReferences(IEnumerable<PicketItem> items)
        => items.Where(Items.Contains).Distinct().Select(i => new RemovedReference(i, Items.IndexOf(i), i.OriginalDesktopPos))
            .OrderBy(i => i.Index).ToArray();

    internal bool RestoreReferences(IEnumerable<RemovedReference> references, Func<string, POINT?>? hide = null)
    {
        hide ??= DesktopIconHider.Hide;
        var complete = true;
        foreach (var saved in references)
        {
            if (!Items.Contains(saved.Item))
            {
                // Do not replace a different reference added later with the same path.
                if (saved.Item.Kind == ItemKind.File && Items.Any(i => i.Kind == ItemKind.File &&
                    string.Equals(i.Path, saved.Item.Path, StringComparison.OrdinalIgnoreCase))) { complete = false; continue; }
                Items.Insert(Math.Clamp(saved.Index, 0, Items.Count), saved.Item);
            }
            if (saved.Position.HasValue && !saved.Item.OriginalDesktopPos.HasValue)
            {
                var ownedElsewhere = Application.Current is App app && app.Pickets.SelectMany(p => p.Items).Any(i =>
                    i != saved.Item && i.OriginalDesktopPos.HasValue && string.Equals(i.Path, saved.Item.Path, StringComparison.OrdinalIgnoreCase));
                if (ownedElsewhere) { complete = false; continue; }
                // A missing icon stays represented by the reference. Only claim a live desktop
                // icon after Windows confirms it was hidden again.
                if (saved.Item.Status == ReferenceStatus.Missing) continue;
                if (hide(saved.Item.Path).HasValue) saved.Item.OriginalDesktopPos = saved.Position;
                else complete = false;
            }
            saved.Item.IsSelected = true;
        }
        RaiseLayoutChanged();
        return complete;
    }

    private void RecordReferenceRemoval(RemovedReference[] removed)
    {
        if (removed.Length == 0 || Application.Current is not App app) return;
        var id = PicketId;
        app.RecordUndo($"{removed.Length} item{(removed.Length == 1 ? "" : "s")} removed", () =>
            app.Pickets.FirstOrDefault(p => p.PicketId == id)?.RestoreReferences(removed) == true);
    }

    private void RecordReferenceAddition(PicketItem[] added)
    {
        if (added.Length == 0 || Application.Current is not App app) return;
        var id = PicketId;
        app.RecordUndo($"{added.Length} item{(added.Length == 1 ? "" : "s")} added", () =>
        {
            var picket = app.Pickets.FirstOrDefault(p => p.PicketId == id);
            if (picket == null) return false;
            picket.RemoveReferences(added.Where(picket.Items.Contains));
            return !added.Any(picket.Items.Contains);
        });
    }

    internal void ReorderReference(int from, int to)
    {
        if (from == to) return;
        var item = Items[from];
        Items.Move(from, to);
        if (Application.Current is App app)
        {
            var id = PicketId;
            app.RecordUndo("Reference reordered", () =>
            {
                var picket = app.Pickets.FirstOrDefault(p => p.PicketId == id);
                if (picket == null || !picket.Items.Contains(item)) return false;
                picket.Items.Move(picket.Items.IndexOf(item), Math.Min(from, picket.Items.Count - 1));
                return true;
            });
        }
    }
}
