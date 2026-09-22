namespace Pickets;

internal readonly record struct DesktopReadiness(bool? AutoArrange, bool? AlignToGrid)
{
    internal bool IsReady => AutoArrange == false && AlignToGrid == false;
    internal bool IsUnknown => AutoArrange == null || AlignToGrid == null;

    internal static DesktopReadiness Read()
        => new(DesktopIconHider.IsAutoArrangeOn(), DesktopIconHider.IsSnapToGridOn());
}
