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

    public static void Reset()
    {
        try
        {
            lock (Gate)
            {
                if (File.Exists(LogPath)) File.Delete(LogPath);
            }
        }
        catch { }
    }
}
