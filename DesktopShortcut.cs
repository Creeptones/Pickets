using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Pickets;

/// <summary>Creates the optional per-user desktop shortcut offered by first-run setup.</summary>
internal static class DesktopShortcut
{
    private const string ShortcutName = "Pickets.lnk";

    public static string Path => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ShortcutName);

    public static bool Exists => File.Exists(Path);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Shortcut creation is optional onboarding convenience and must never abort startup.")]
    public static bool TryCreate(out string error)
    {
        object? shell = null;
        object? shortcut = null;
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe))
                throw new InvalidOperationException("Pickets could not determine its executable path.");

            var shellType = Type.GetTypeFromProgID("WScript.Shell") ??
                throw new InvalidOperationException("Windows Script Host is unavailable.");
            shell = Activator.CreateInstance(shellType) ??
                throw new InvalidOperationException("Windows could not create the shortcut service.");

            dynamic shellObject = shell;
            shortcut = shellObject.CreateShortcut(Path);
            dynamic shortcutObject = shortcut;
            shortcutObject.TargetPath = exe;
            shortcutObject.WorkingDirectory = System.IO.Path.GetDirectoryName(exe) ?? "";
            shortcutObject.IconLocation = $"{exe},0";
            shortcutObject.Description = "Pickets desktop organizer";
            shortcutObject.Save();
            error = "";
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"DesktopShortcut.TryCreate failed: {ex}");
            error = ex.Message;
            return false;
        }
        finally
        {
            if (shortcut != null && Marshal.IsComObject(shortcut))
                Marshal.FinalReleaseComObject(shortcut);
            if (shell != null && Marshal.IsComObject(shell))
                Marshal.FinalReleaseComObject(shell);
        }
    }
}
