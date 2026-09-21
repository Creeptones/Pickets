using System;
using System.IO;

namespace Pickets;

/// <summary>Lightweight file logger to %APPDATA%\Pickets\debug.log. Best-effort; never throws.</summary>
public static class Logger
{
    private static readonly object Gate = new();
    private static string? _path;

    public static string LogPath
    {
        get
        {
            if (_path != null) return _path;
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Pickets");
            Directory.CreateDirectory(dir);
            _path = Path.Combine(dir, "debug.log");
            return _path;
        }
    }

    public static void Log(string message)
    {
        try
        {
            lock (Gate)
            {
                File.AppendAllText(LogPath,
                    $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
        }
        catch { }
    }

    /// <summary>Starts a fresh log while retaining the immediately previous session. This keeps
    /// crash evidence available after the user relaunches the app to report a problem.</summary>
    public static void StartSession()
    {
        try
        {
            lock (Gate)
            {
                if (!File.Exists(LogPath)) return;
                File.Move(LogPath, LogPath + ".previous", overwrite: true);
            }
        }
        catch { }
    }
}
