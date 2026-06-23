using System;
using System.IO;
using Microsoft.Win32;

namespace Pickets;

/// <summary>
/// One-time, non-destructive migration from the project's former name ("DesktopFences") to "Pickets".
/// Carries the user's saved fence layout and run-at-login preference across the rename so an in-place
/// upgrade doesn't look like a fresh, empty install. Safe to run on every launch: each step becomes a
/// no-op once the Pickets-side state exists. Best-effort -- never throws into startup.
/// </summary>
internal static class LegacyMigration
{
    private const string LegacyName  = "DesktopFences";
    private const string CurrentName = "Pickets";
    private const string RunKeyPath  = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static void Run()
    {
        try { MigrateLayout(); }
        catch (Exception ex) { Logger.Log($"LegacyMigration.MigrateLayout failed: {ex.Message}"); }

        try { MigrateStartupEntry(); }
        catch (Exception ex) { Logger.Log($"LegacyMigration.MigrateStartupEntry failed: {ex.Message}"); }
    }

    /// <summary>Seeds %APPDATA%\Pickets\layout.json from the old %APPDATA%\DesktopFences\layout.json
    /// the first time Pickets runs. Copies (never moves) so the old install stays intact as a backup.</summary>
    private static void MigrateLayout()
    {
        var appData       = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var legacyLayout  = Path.Combine(appData, LegacyName,  "layout.json");
        var currentDir    = Path.Combine(appData, CurrentName);
        var currentLayout = Path.Combine(currentDir, "layout.json");

        // Never clobber existing Pickets state; only seed when there's nothing on the Pickets side.
        if (File.Exists(currentLayout) || !File.Exists(legacyLayout)) return;

        Directory.CreateDirectory(currentDir);
        File.Copy(legacyLayout, currentLayout);
        Logger.Log($"Migrated layout from '{legacyLayout}' to '{currentLayout}'.");
    }

    /// <summary>If run-at-login was enabled under the old name, re-point it at the new exe and drop
    /// the stale "DesktopFences" Run entry so the user keeps their preference without a duplicate.</summary>
    private static void MigrateStartupEntry()
    {
        using var run = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (run == null) return;

        bool hadLegacy  = run.GetValue(LegacyName)  is string s && !string.IsNullOrWhiteSpace(s);
        if (!hadLegacy) return;

        bool hasCurrent = run.GetValue(CurrentName) is string c && !string.IsNullOrWhiteSpace(c);
        if (!hasCurrent)
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
                run.SetValue(CurrentName, $"\"{exe}\"", RegistryValueKind.String);
        }
        run.DeleteValue(LegacyName, throwOnMissingValue: false);
        Logger.Log("Migrated run-at-login registry entry from DesktopFences to Pickets.");
    }
}
