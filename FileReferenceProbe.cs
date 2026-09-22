using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Pickets;

public enum ReferenceStatus { Checking, Available, Missing, Unavailable }

internal readonly record struct ReferenceCheck(ReferenceStatus Status, bool IsFolder);

internal static class FileReferenceProbe
{
    private static readonly SemaphoreSlim Workers = new(4);

    internal static async Task<ReferenceCheck> CheckAsync(string path)
    {
        // Offline shares must not block the dispatcher or grow an unbounded pool of probes.
        if (!await Workers.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false))
            return new(ReferenceStatus.Unavailable, false);
        var probe = Task.Run(() =>
        {
            try { return Check(path); }
            finally { Workers.Release(); }
        });
        try { return await probe.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); }
        catch (TimeoutException) { return new(ReferenceStatus.Unavailable, false); }
    }

    internal static ReferenceCheck Check(string path)
    {
        try { return new(ReferenceStatus.Available, (File.GetAttributes(path) & FileAttributes.Directory) != 0); }
        catch (FileNotFoundException) { return new(MissingStatus(path), false); }
        catch (DirectoryNotFoundException) { return new(MissingStatus(path), false); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return new(ReferenceStatus.Unavailable, false); }
    }

    private static ReferenceStatus MissingStatus(string path)
        => path.StartsWith(@"\\", StringComparison.Ordinal) || !Directory.Exists(Path.GetPathRoot(path))
            ? ReferenceStatus.Unavailable : ReferenceStatus.Missing;
}
