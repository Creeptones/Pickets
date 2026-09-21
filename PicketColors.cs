using System.Collections.Generic;
using System.Windows.Media;

namespace Pickets;

public record PicketColorScheme(
    string Key,
    string DisplayName,
    Color Background,
    Color TitleBackground,
    Color Border,
    Color Accent)
{
    public Color TitleForeground => PicketColors.ContrastForeground(TitleBackground);
    public Color ItemForeground => PicketColors.ContrastForeground(Background);
    public Color TitleShadow => PicketColors.HaloFor(TitleForeground);
    public Color ItemShadow => PicketColors.HaloFor(ItemForeground);
}

/// <summary>
/// A compact set of coordinated surface systems. Each theme owns the canvas, navigation chrome,
/// outline, and interaction accent; readable foregrounds and wallpaper halos are derived.
/// </summary>
public static class PicketColors
{
    private static readonly Color Ink       = Color.FromRgb(0x25, 0x27, 0x2B);
    private static readonly Color Paper     = Color.FromRgb(0xF4, 0xF5, 0xF7);
    private static readonly Color LightHalo = Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF);
    private static readonly Color DarkHalo  = Color.FromArgb(0xCC, 0x00, 0x00, 0x00);

    public static readonly IReadOnlyList<PicketColorScheme> All = new[]
    {
        new PicketColorScheme("porcelain", "Porcelain",
            Color.FromRgb(0xF5,0xF2,0xEC), Color.FromRgb(0xE5,0xDF,0xD5),
            Color.FromRgb(0xA7,0x9F,0x94), Color.FromRgb(0x6F,0x7F,0x91)),
        new PicketColorScheme("sandstone", "Sandstone",
            Color.FromRgb(0xF3,0xEA,0xDC), Color.FromRgb(0xDD,0xC8,0xA8),
            Color.FromRgb(0xA0,0x80,0x55), Color.FromRgb(0xB8,0x70,0x3D)),
        new PicketColorScheme("sage", "Sage",
            Color.FromRgb(0xE7,0xEE,0xE7), Color.FromRgb(0xC5,0xD5,0xC6),
            Color.FromRgb(0x7B,0x96,0x80), Color.FromRgb(0x4F,0x7D,0x64)),
        new PicketColorScheme("ocean", "Ocean",
            Color.FromRgb(0xE5,0xEF,0xF4), Color.FromRgb(0xB9,0xD1,0xDE),
            Color.FromRgb(0x6E,0x91,0xA4), Color.FromRgb(0x2E,0x75,0x9C)),
        new PicketColorScheme("lavender", "Lavender",
            Color.FromRgb(0xEF,0xEB,0xF5), Color.FromRgb(0xD3,0xC8,0xE0),
            Color.FromRgb(0x8E,0x7B,0xA5), Color.FromRgb(0x75,0x5A,0x99)),
        new PicketColorScheme("rose", "Rose",
            Color.FromRgb(0xF4,0xE8,0xE9), Color.FromRgb(0xDF,0xC4,0xC8),
            Color.FromRgb(0xA6,0x7B,0x84), Color.FromRgb(0xA8,0x4F,0x64)),
        new PicketColorScheme("midnight", "Midnight",
            Color.FromRgb(0x13,0x1B,0x29), Color.FromRgb(0x1D,0x35,0x55),
            Color.FromRgb(0x49,0x6B,0x91), Color.FromRgb(0x58,0x9B,0xD5)),
        new PicketColorScheme("graphite", "Graphite",
            Color.FromRgb(0x18,0x1B,0x20), Color.FromRgb(0x29,0x2E,0x36),
            Color.FromRgb(0x58,0x61,0x6E), Color.FromRgb(0x8A,0xA0,0xB8)),
    };

    private static readonly Dictionary<string, string> LegacyAliases =
        new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
        {
            ["white"] = "porcelain", ["stone"] = "porcelain", ["cloud"] = "porcelain",
            ["frost"] = "porcelain",
            ["sand"] = "sandstone", ["butter"] = "sandstone", ["dune"] = "sandstone",
            ["peach"] = "sandstone", ["apricot"] = "sandstone",
            ["mint"] = "sage", ["grove"] = "sage",
            ["sky"] = "ocean", ["teal"] = "ocean",
            ["lilac"] = "lavender", ["periwinkle"] = "lavender",
            ["bloom"] = "rose", ["blush"] = "rose", ["coral"] = "rose",
            ["slate"] = "midnight", ["nightfall"] = "midnight",
            ["charcoal"] = "graphite", ["black"] = "graphite",
        };

    public static Color ContrastForeground(Color surface)
        => ContrastRatio(surface, Ink) >= ContrastRatio(surface, Paper) ? Ink : Paper;

    public static Color HaloFor(Color foreground)
        => RelativeLuminance(foreground) > 0.5 ? DarkHalo : LightHalo;

    private static double ContrastRatio(Color a, Color b)
    {
        var lighter = System.Math.Max(RelativeLuminance(a), RelativeLuminance(b));
        var darker = System.Math.Min(RelativeLuminance(a), RelativeLuminance(b));
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(Color color)
    {
        static double Linear(byte channel)
        {
            var value = channel / 255.0;
            return value <= 0.04045 ? value / 12.92 : System.Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
    }

    public static PicketColorScheme Get(string? key)
    {
        if (!string.IsNullOrEmpty(key))
        {
            if (LegacyAliases.TryGetValue(key, out var replacement)) key = replacement;
            foreach (var scheme in All)
                if (scheme.Key.Equals(key, System.StringComparison.OrdinalIgnoreCase)) return scheme;
        }
        return All[0];
    }
}
