using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Pickets;

public class LayoutFile
{
    /// <summary>Bumped to 2 when per-display profiles were introduced.</summary>
    public int Version { get; set; } = 2;

    /// <summary>Keyed by DisplayProfile.CurrentKey(). A missing key means "never saved for this
    /// display arrangement" -- the app seeds it from the most recently used profile.</summary>
    public Dictionary<string, List<PicketState>> Profiles { get; set; } = new();

    /// <summary>Fallback seed used when a brand-new profile is first seen. Holds the pickets that
    /// were last saved under the previous active profile, so the laptop layout starts as a
    /// sensible copy of the desktop layout rather than an empty canvas.</summary>
    public List<PicketState>? LastProfileSeed { get; set; }

    /// <summary>Per-picket look (color, transparency, blur) shared across ALL display profiles,
    /// keyed by picket Title. Positions stay per-display, but a picket looks the same everywhere.
    /// Absent in older files; seeded from the first profile loaded after the upgrade.</summary>
    public Dictionary<string, PicketAppearance> Appearances { get; set; } = new();

    /// <summary>Theme inherited by newly created Pickets. Updated by the app-wide color action.</summary>
    public string DefaultColorKey { get; set; } = "porcelain";

    /// <summary>Prevents the compact first-run guide from reappearing after it is acknowledged.</summary>
    public bool HasCompletedOnboarding { get; set; }

    public string FocusShortcut { get; set; } = "Ctrl+Alt+D";
}

/// <summary>A picket's visual style, kept global (shared by every display profile).</summary>
public class PicketAppearance
{
    public string ColorKey { get; set; } = "porcelain";
    public string TransparencyKey { get; set; } = "solid";
    public int TransparencyCustomPercent { get; set; } = 50;
    public bool BlurEnabled { get; set; }
}

public class PicketState
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "Picket";
    public double X { get; set; } = 200;
    public double Y { get; set; } = 200;
    public double Width { get; set; } = 420;
    public double Height { get; set; } = 320;
    // Older layouts adopt compact rows; a subsequent manual height resize persists false.
    public bool AutoSizeRows { get; set; } = true;
    public bool IsCollapsed { get; set; }
    // Null means an older layout whose touching groups still need a one-time migration.
    public string? GroupId { get; set; }
    public int GroupOrder { get; set; }
    public bool GroupHorizontal { get; set; }
    public bool AccordionMode { get; set; }
    public string ColorKey { get; set; } = "porcelain";
    public string TransparencyKey { get; set; } = "solid";
    public int TransparencyCustomPercent { get; set; } = 50;

    /// <summary>Legacy migration field. Current builds convert an old portal into a normal folder
    /// item on load and omit this value on the next save.</summary>
    public string? PortalPath { get; set; }

    public bool BlurEnabled { get; set; }

    public List<ItemState> Items { get; set; } = new();
}

public class ItemState
{
    public string Path { get; set; } = "";
    public int? OriginalX { get; set; }
    public int? OriginalY { get; set; }
    public bool IsLarge { get; set; }
    public bool IsFolder { get; set; }
    public ItemKind Kind { get; set; } = ItemKind.File;
    // Labels use this; files ignore it and fall back to Path-derived name.
    public string? LabelText { get; set; }

    [JsonIgnore]
    public bool HasOriginalPos => OriginalX.HasValue && OriginalY.HasValue;
}

public static class LayoutStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string LayoutPath
    {
        get
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appData, "Pickets");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "layout.json");
        }
    }

    public static string BackupPath => LayoutPath + ".bak";

    public static LayoutFile? Load() => Load(LayoutPath, BackupPath);

    internal static LayoutFile? Load(string primaryPath, string backupPath)
    {
        var primary = TryLoad(primaryPath, out var primaryError);
        if (primary != null)
            return primary;

        if (primaryError != null)
            Logger.Log($"Could not read primary layout '{primaryPath}': {primaryError}");

        var backup = TryLoad(backupPath, out var backupError);
        if (backup != null)
        {
            Logger.Log($"Recovered layout from backup '{backupPath}'.");
            return backup;
        }

        if (backupError != null)
            Logger.Log($"Could not read backup layout '{backupPath}': {backupError}");

        return primaryError == null && backupError == null ? DefaultLayout() : null;
    }

    // Recovery must not mistake an unreadable layout for an empty desktop.
    internal static LayoutFile? LoadForRecovery() => LoadForRecovery(LayoutPath, BackupPath);

    internal static LayoutFile? LoadForRecovery(string primaryPath, string backupPath)
    {
        var primary = TryLoad(primaryPath, out var primaryError);
        if (primary != null) return primary;
        var backup = TryLoad(backupPath, out var backupError);
        if (backup != null) return backup;
        if (primaryError == null && backupError == null) return new LayoutFile();
        Logger.Log($"Recovery cannot read layout: {primaryError}; backup: {backupError}");
        return null;
    }

    private static LayoutFile? TryLoad(string path, out string? error)
    {
        error = null;

        try
        {
            var layout = ParseWithMigration(File.ReadAllText(path));
            if (layout == null)
                error = "The file is not a recognized Pickets layout.";
            else
                Normalize(layout);
            return layout;
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        catch (Exception ex)
        {
            error = ex.ToString();
            return null;
        }
    }

    internal static void Normalize(LayoutFile layout)
    {
        layout.Profiles ??= new Dictionary<string, List<PicketState>>();
        layout.Appearances ??= new Dictionary<string, PicketAppearance>();
        layout.DefaultColorKey = PicketColors.Get(layout.DefaultColorKey).Key;

        foreach (var key in layout.Profiles.Keys.ToList())
            layout.Profiles[key] = NormalizeStates(layout.Profiles[key]);

        foreach (var key in layout.Appearances.Keys.ToList())
        {
            var appearance = layout.Appearances[key];
            if (appearance == null)
            {
                layout.Appearances.Remove(key);
                continue;
            }
            appearance.ColorKey = PicketColors.Get(appearance.ColorKey).Key;
            appearance.TransparencyKey = NormalizeTransparency(appearance.TransparencyKey);
            appearance.TransparencyCustomPercent = Math.Clamp(appearance.TransparencyCustomPercent, 0, 100);
        }

        if (layout.LastProfileSeed != null)
            layout.LastProfileSeed = NormalizeStates(layout.LastProfileSeed);
    }

    private static List<PicketState> NormalizeStates(List<PicketState>? states)
    {
        states ??= new List<PicketState>();
        states.RemoveAll(state => state == null);
        foreach (var state in states)
        {
            if (string.IsNullOrWhiteSpace(state.Id)) state.Id = Guid.NewGuid().ToString();
            if (string.IsNullOrWhiteSpace(state.Title)) state.Title = "Picket";
            state.ColorKey = PicketColors.Get(state.ColorKey).Key;
            state.TransparencyKey = NormalizeTransparency(state.TransparencyKey);
            state.TransparencyCustomPercent = Math.Clamp(state.TransparencyCustomPercent, 0, 100);
            state.Items ??= new List<ItemState>();
            state.Items.RemoveAll(item => item == null ||
                (item.Kind == ItemKind.File && string.IsNullOrWhiteSpace(item.Path)));
        }
        return states;
    }

    private static string NormalizeTransparency(string? key)
        => key is "solid" or "light" or "medium" or "heavy" or "custom" ? key : "solid";

    /// <summary>V1 had a flat top-level "Fences" array; V2 keys every picket list under a
    /// display-profile string. Detect the version and migrate in memory so the user's existing
    /// layout.json keeps working. The migrated list is stored under "_legacy" and used as the
    /// seed for whichever profile is active on the first V2 launch.</summary>
    internal static LayoutFile? ParseWithMigration(string json)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch { return null; }
        if (root is not JsonObject obj) return null;

        var version = 1;
        if (obj.ContainsKey("Version") &&
            (obj["Version"] is not JsonValue value || !value.TryGetValue(out version))) return null;

        if (version == 2)
        {
            // Validate ownership-bearing structures before Normalize can turn nulls into empty
            // collections. Otherwise an unreadable primary would mask a good recovery backup.
            if (obj["Profiles"] is not JsonObject profiles ||
                profiles.Any(profile => !IsStateArray(profile.Value)) ||
                (obj["LastProfileSeed"] != null && !IsStateArray(obj["LastProfileSeed"]))) return null;
            return JsonSerializer.Deserialize<LayoutFile>(json, Options);
        }

        if (version != 1 || !IsStateArray(obj["Fences"])) return null;
        // V1 migration: wrap the legacy Pickets list under a sentinel profile key.
        var legacyPickets = obj["Fences"]!.Deserialize<List<PicketState>>(Options)!;
        return new LayoutFile
        {
            Version = 2,
            Profiles = new Dictionary<string, List<PicketState>> { ["_legacy"] = legacyPickets },
            LastProfileSeed = legacyPickets,
        };
    }

    private static bool IsStateArray(JsonNode? node)
        => node is JsonArray states && states.All(state => state is JsonObject picket &&
            (!picket.ContainsKey("Items") ||
             picket["Items"] is JsonArray items && items.All(IsItem)));

    private static bool IsItem(JsonNode? node)
    {
        if (node is not JsonObject item) return false;
        var kind = (int)ItemKind.File;
        if (item.ContainsKey("Kind") &&
            (item["Kind"] is not JsonValue kindValue || !kindValue.TryGetValue(out kind))) return false;
        if (kind != (int)ItemKind.File && kind != (int)ItemKind.Label) return false;
        if (kind == (int)ItemKind.File &&
            (item["Path"] is not JsonValue path || !path.TryGetValue<string>(out var text) ||
             string.IsNullOrWhiteSpace(text))) return false;
        if (item["OriginalX"] == null && item["OriginalY"] == null) return true;
        return item["OriginalX"] is JsonValue x && x.TryGetValue<int>(out _) &&
               item["OriginalY"] is JsonValue y && y.TryGetValue<int>(out _);
    }

    /// <summary>Returns the picket list for the current profile, creating it from the last-used
    /// seed (or legacy V1 data) on first encounter. The returned list is stored back into
    /// layout.Profiles under profileKey so future reads stay cheap.</summary>
    public static List<PicketState> GetOrSeedProfile(LayoutFile layout, string profileKey)
    {
        if (layout.Profiles.TryGetValue(profileKey, out var existing))
            return existing;

        // Pick a seed: prefer LastProfileSeed, else fall back to "_legacy", else any profile, else default.
        var seed = layout.LastProfileSeed
                   ?? (layout.Profiles.TryGetValue("_legacy", out var legacy) ? legacy : null)
                   ?? layout.Profiles.Values.FirstOrDefault();

        var fresh = seed != null
            ? seed.Select(CloneWithNewId).ToList()
            : new List<PicketState> { new PicketState { Title = "Picket", X = 200, Y = 200, Width = 420, Height = PicketContentSizing.EmptyHeight, AutoSizeRows = true } };

        foreach (var s in fresh)
            DisplayProfile.ClampToVisibleWorkArea(s);

        layout.Profiles[profileKey] = fresh;
        return fresh;
    }

    /// <summary>Clone a PicketState while giving it a new Id -- two profiles must not share picket
    /// identities or independent edits on one profile will collide with the other through the
    /// Id field used as a dictionary key in-process.</summary>
    private static PicketState CloneWithNewId(PicketState src) => new()
    {
        Id = Guid.NewGuid().ToString(),
        Title = src.Title,
        X = src.X, Y = src.Y,
        Width = src.Width, Height = src.Height,
        AutoSizeRows = src.AutoSizeRows,
        IsCollapsed = src.IsCollapsed,
        GroupId = src.GroupId,
        GroupOrder = src.GroupOrder,
        GroupHorizontal = src.GroupHorizontal,
        AccordionMode = src.AccordionMode,
        ColorKey = src.ColorKey,
        TransparencyKey = src.TransparencyKey,
        TransparencyCustomPercent = src.TransparencyCustomPercent,
        PortalPath = src.PortalPath,
        BlurEnabled = src.BlurEnabled,
        Items = src.Items.Select(i => new ItemState
        {
            Path = i.Path,
            OriginalX = i.OriginalX,
            OriginalY = i.OriginalY,
            IsLarge = i.IsLarge,
            IsFolder = i.IsFolder,
            Kind = i.Kind,
            LabelText = i.LabelText,
        }).ToList(),
    };

    public static void Save(LayoutFile layout) => TrySave(layout);

    internal static bool TrySave(LayoutFile layout)
    {
        string? temporaryPath = null;
        try
        {
            var json = JsonSerializer.Serialize(layout, Options);
            var path = LayoutPath;
            temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";

            // Never write over the only good copy. Flush a complete temporary file first, then
            // atomically replace the primary while retaining the previous version as a backup.
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                       FileShare.None, 4096, FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(path))
                File.Replace(temporaryPath, path, BackupPath, ignoreMetadataErrors: true);
            else
                File.Move(temporaryPath, path);

            temporaryPath = null;
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"LayoutStore.Save failed: {ex}");
            return false;
        }
        finally
        {
            if (temporaryPath != null)
            {
                try { File.Delete(temporaryPath); }
                catch { }
            }
        }
    }

    private static LayoutFile DefaultLayout()
    {
        var seed = new List<PicketState>
        {
            new PicketState { Title = "Picket", X = 200, Y = 200, Width = 420, Height = PicketContentSizing.EmptyHeight, AutoSizeRows = true }
        };
        return new LayoutFile { LastProfileSeed = seed };
    }
}
