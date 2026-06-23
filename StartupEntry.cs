using System;
using Microsoft.Win32;

namespace Pickets;

/// <summary>
/// Manages the HKCU "run-at-login" registry entry for this app. Using HKCU (not HKLM) means no
/// admin prompt and per-user setting. The path written is whatever the current process's exe
/// actually lives at, so moving the binary just requires toggling off/on in the menu.
/// </summary>
internal static class StartupEntry
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName  = "Pickets";

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
                return key?.GetValue(ValueName) is string s && !string.IsNullOrWhiteSpace(s);
            }
            catch (Exception ex)
            {
                Logger.Log($"StartupEntry.IsEnabled failed: {ex.Message}");
                return false;
            }
        }
    }

    public static void Enable()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe))
            {
                Logger.Log("StartupEntry.Enable: Environment.ProcessPath is empty");
                return;
            }

            // Quote the path so spaces in the install location don't split the command line.
            var value = $"\"{exe}\"";
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            key?.SetValue(ValueName, value, RegistryValueKind.String);
        }
        catch (Exception ex)
        {
            Logger.Log($"StartupEntry.Enable failed: {ex}");
        }
    }

    public static void Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            // DeleteValue throws if the value is already absent -- guard with the bool overload.
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            Logger.Log($"StartupEntry.Disable failed: {ex}");
        }
    }
}
