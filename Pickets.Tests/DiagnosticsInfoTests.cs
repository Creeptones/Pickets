namespace Pickets.Tests;

public sealed class DiagnosticsInfoTests
{
    [Fact]
    public void Sanitize_RemovesWindowsAndUncPaths()
    {
        const string input = "Hide('C:\\Users\\Person\\Desktop\\Secret Project.lnk')\n" +
                             "Share: \\\\server\\private\\document.txt";

        var sanitized = DiagnosticsInfo.Sanitize(input);

        Assert.DoesNotContain("Person", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Secret Project", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("server", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("%PATH%", sanitized, StringComparison.Ordinal);
    }
}
