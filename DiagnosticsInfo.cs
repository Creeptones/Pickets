using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Pickets;

internal static partial class DiagnosticsInfo
{
    public static string Version
    {
        get
        {
            var assembly = Assembly.GetExecutingAssembly();
            return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                       .InformationalVersion
                   ?? assembly.GetName().Version?.ToString(3)
                   ?? "unknown";
        }
    }

    public static string BuildSanitizedSummary()
    {
        var text = new StringBuilder()
            .Append("Pickets: ").AppendLine(Version)
            .Append("Windows: ").AppendLine(RuntimeInformation.OSDescription)
            .Append("Architecture: ").AppendLine(RuntimeInformation.ProcessArchitecture.ToString())
            .Append("Display profile: ").AppendLine(DisplayProfile.CurrentKey())
            .Append("Executable: ").AppendLine(Environment.ProcessPath)
            .Append("Layout: ").AppendLine(LayoutStore.LayoutPath)
            .AppendLine()
            .AppendLine("Recent log:");

        try
        {
            if (File.Exists(Logger.LogPath))
                foreach (var line in File.ReadLines(Logger.LogPath).TakeLast(40)) text.AppendLine(line);
            else
                text.AppendLine("(No log file found.)");
        }
        catch (Exception ex)
        {
            text.Append("(Could not read log: ").Append(ex.Message).AppendLine(")");
        }

        return Sanitize(text.ToString());
    }

    internal static string Sanitize(string value)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(profile))
            value = value.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        var user = Environment.UserName;
        if (!string.IsNullOrEmpty(user))
            value = value.Replace(user, "%USERNAME%", StringComparison.OrdinalIgnoreCase);
        return WindowsPathRegex().Replace(value, "%PATH%");
    }

    [GeneratedRegex(@"(?i)(?:[a-z]:\\|\\\\)[^'\r\n]+")]
    private static partial Regex WindowsPathRegex();
}
