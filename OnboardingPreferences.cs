using System;
using System.IO;
using Microsoft.Win32;

namespace Pickets;

internal static class OnboardingPreferences
{
    internal static bool DefaultCreateShortcut(bool shortcutExists)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Pickets\Setup");
            return ResolveShortcutDefault(shortcutExists, Environment.ProcessPath,
                key?.GetValue("InstallPath") as string, key?.GetValue("DesktopShortcut") as int?);
        }
        catch (Exception ex)
        {
            Logger.Log($"Could not read setup preference: {ex.Message}");
            return false; // An unreadable preference is not consent to create a shortcut.
        }
    }

    internal static bool ResolveShortcutDefault(bool shortcutExists, string? executable,
        string? installPath, int? preference)
    {
        if (shortcutExists) return false;
        var directory = string.IsNullOrWhiteSpace(executable) ? null : Path.GetDirectoryName(executable);
        if (!string.IsNullOrWhiteSpace(directory) && !string.IsNullOrWhiteSpace(installPath) &&
            string.Equals(Path.TrimEndingDirectorySeparator(directory),
                Path.TrimEndingDirectorySeparator(installPath), StringComparison.OrdinalIgnoreCase))
            return preference == 1;
        return true; // Portable copies retain the original explicit first-run choice.
    }
}
