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
}

public class PicketState
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "Picket";
    public double X { get; set; } = 200;
    public double Y { get; set; } = 200;
    public double Width { get; set; } = 420;
    public double Height { get; set; } = 320;
    public bool IsCollapsed { get; set; }
    public string ColorKey { get; set; } = "stone";
    public string TransparencyKey { get; set; } = "solid";
    public int TransparencyCustomPercent { get; set; } = 50;

    /// <summary>When set, this picket mirrors the named folder instead of holding manual items.
    /// Items are rebuilt from the folder on each launch -- we persist only the folder path.</summary>
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

    public static LayoutFile Load()
    {
        try
        {
            if (!File.Exists(LayoutPath)) return DefaultLayout();
            var json = File.ReadAllText(LayoutPath);
            return ParseWithMigration(json) ?? DefaultLayout();
        }
        catch (Exception ex)
        {
            // Returning the default here means the user silently loses their layout. Log first so
            // we at least know why the next launch looks empty (bad JSON, permission denied, etc.).
            Logger.Log($"LayoutStore.Load failed: {ex}");
            return DefaultLayout();
        }
    }

    /// <summary>V1 had a flat top-level "Fences" array; V2 keys every picket list under a
    /// display-profile string. Detect the version and migrate in memory so the user's existing
    /// layout.json keeps working. The migrated list is stored under "_legacy" and used as the
    /// seed for whichever profile is active on the first V2 launch.</summary>
    private static LayoutFile? ParseWithMigration(string json)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch { return null; }
        if (root is not JsonObject obj) return null;

        int version = obj["Version"]?.GetValue<int>() ?? 1;

        if (version >= 2)
        {
            return JsonSerializer.Deserialize<LayoutFile>(json, Options);
        }

        // V1 migration: wrap the legacy Pickets list under a sentinel profile key.
        var legacyPickets = obj["Fences"]?.Deserialize<List<PicketState>>(Options) ?? new();
        return new LayoutFile
        {
            Version = 2,
            Profiles = new Dictionary<string, List<PicketState>> { ["_legacy"] = legacyPickets },
            LastProfileSeed = legacyPickets,
        };
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
            : new List<PicketState> { new PicketState { Title = "Picket", X = 200, Y = 200, Width = 420, Height = 320 } };

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
        IsCollapsed = src.IsCollapsed,
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
            Kind = i.Kind,
            LabelText = i.LabelText,
        }).ToList(),
    };

    public static void Save(LayoutFile layout)
    {
        try
        {
            var json = JsonSerializer.Serialize(layout, Options);
            File.WriteAllText(LayoutPath, json);
        }
        catch (Exception ex)
        {
            // Persistence failures shouldn't crash the app, but silent swallows have masked bugs
            // before (disk full, antivirus holding the handle, AppData redirected). Log and move on.
            Logger.Log($"LayoutStore.Save failed: {ex}");
        }
    }

    private static LayoutFile DefaultLayout()
    {
        var seed = new List<PicketState>
        {
            new PicketState { Title = "Picket", X = 200, Y = 200, Width = 420, Height = 320 }
        };
        return new LayoutFile { LastProfileSeed = seed };
    }
}
