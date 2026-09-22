namespace Pickets;

internal enum RecoveryExitCode
{
    Success = 0,
    IconsRemainHidden = 1,
    OwnerDidNotExit = 2,
    LayoutUnavailable = 3,
    LayoutNotSaved = 4,
}

internal static class RecoveryOutcome
{
    internal static RecoveryExitCode From(int restored, int captured, bool saved)
        => !saved ? RecoveryExitCode.LayoutNotSaved
            : restored != captured ? RecoveryExitCode.IconsRemainHidden
            : RecoveryExitCode.Success;
}
