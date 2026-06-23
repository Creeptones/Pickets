using System.Collections.Generic;
using System.Windows.Media;

namespace Pickets;

public record FenceColorScheme(
    string Key,
    string DisplayName,
    Color SwatchSolid,    // shown in the menu icon (no alpha)
    Color Background,     // outer body fill (with alpha)
    Color TitleBackground,// title bar fill (with alpha)
    Color Border,         // outer border (with alpha)
    Color Foreground,     // text + caret (opaque)
    Color Shadow);        // soft halo behind item labels for legibility on any wallpaper

/// <summary>
/// Light, low-saturation palette plus two dark schemes. Values keep the fence airy on top
/// of a wallpaper while staying readable thanks to a per-scheme label halo (light schemes
/// use a white halo so dark text pops over dark wallpapers; dark schemes invert).
/// </summary>
public static class FenceColors
{
    private static readonly Color Ink       = Color.FromRgb(0x2C, 0x2C, 0x2C);
    private static readonly Color Paper     = Color.FromRgb(0xED, 0xED, 0xED);
    // Halo alphas sit around 80% so they read cleanly without drowning the text itself.
    private static readonly Color LightHalo = Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF);
    private static readonly Color DarkHalo  = Color.FromArgb(0xCC, 0x00, 0x00, 0x00);

    public static readonly IReadOnlyList<FenceColorScheme> All = new[]
    {
        new FenceColorScheme("white",    "White",    Color.FromRgb(0xFF,0xFF,0xFF), Color.FromRgb(0xFF,0xFF,0xFF), Color.FromRgb(0xF2,0xF2,0xF2), Color.FromRgb(0xB0,0xB0,0xB0), Paper, DarkHalo),
        new FenceColorScheme("stone",    "Stone",    Color.FromRgb(0xED,0xED,0xED), Color.FromRgb(0xED,0xED,0xED), Color.FromRgb(0xDD,0xDD,0xDD), Color.FromRgb(0x99,0x99,0x99), Paper, DarkHalo),
        new FenceColorScheme("cloud",    "Cloud",    Color.FromRgb(0xF2,0xF4,0xF7), Color.FromRgb(0xF2,0xF4,0xF7), Color.FromRgb(0xE0,0xE4,0xEA), Color.FromRgb(0x99,0xA0,0xAA), Paper, DarkHalo),
        new FenceColorScheme("sand",     "Sand",     Color.FromRgb(0xF0,0xEB,0xDF), Color.FromRgb(0xF0,0xEB,0xDF), Color.FromRgb(0xE2,0xDC,0xC8), Color.FromRgb(0xA8,0xA0,0x88), Paper, DarkHalo),
        new FenceColorScheme("sage",     "Sage",     Color.FromRgb(0xE2,0xEA,0xE0), Color.FromRgb(0xE2,0xEA,0xE0), Color.FromRgb(0xCF,0xD9,0xCD), Color.FromRgb(0x99,0xA8,0x99), Paper, DarkHalo),
        new FenceColorScheme("sky",      "Sky",      Color.FromRgb(0xE0,0xE8,0xF0), Color.FromRgb(0xE0,0xE8,0xF0), Color.FromRgb(0xCC,0xD7,0xE2), Color.FromRgb(0x99,0xA8,0xB8), Paper, DarkHalo),
        new FenceColorScheme("blush",    "Blush",    Color.FromRgb(0xF0,0xE2,0xE2), Color.FromRgb(0xF0,0xE2,0xE2), Color.FromRgb(0xE0,0xCE,0xCE), Color.FromRgb(0xB8,0x99,0x99), Paper, DarkHalo),
        new FenceColorScheme("lilac",    "Lilac",    Color.FromRgb(0xE5,0xE0,0xEC), Color.FromRgb(0xE5,0xE0,0xEC), Color.FromRgb(0xD0,0xC9,0xDA), Color.FromRgb(0xA8,0x99,0xB8), Paper, DarkHalo),
        new FenceColorScheme("mint",     "Mint",     Color.FromRgb(0xDE,0xEA,0xE3), Color.FromRgb(0xDE,0xEA,0xE3), Color.FromRgb(0xC8,0xD8,0xCD), Color.FromRgb(0x99,0xB0,0xA0), Paper, DarkHalo),
        new FenceColorScheme("peach",    "Peach",    Color.FromRgb(0xF5,0xE1,0xCE), Color.FromRgb(0xF5,0xE1,0xCE), Color.FromRgb(0xE6,0xCB,0xB1), Color.FromRgb(0xB8,0x99,0x7A), Paper, DarkHalo),
        new FenceColorScheme("apricot",  "Apricot",  Color.FromRgb(0xF5,0xD2,0xB0), Color.FromRgb(0xF5,0xD2,0xB0), Color.FromRgb(0xE6,0xBD,0x96), Color.FromRgb(0xC8,0x99,0x70), Paper, DarkHalo),
        new FenceColorScheme("butter",   "Butter",   Color.FromRgb(0xF5,0xEE,0xCE), Color.FromRgb(0xF5,0xEE,0xCE), Color.FromRgb(0xE6,0xDC,0xB1), Color.FromRgb(0xB8,0xAC,0x7A), Paper, DarkHalo),
        new FenceColorScheme("coral",    "Coral",    Color.FromRgb(0xF5,0xD6,0xCE), Color.FromRgb(0xF5,0xD6,0xCE), Color.FromRgb(0xE6,0xBF,0xB1), Color.FromRgb(0xC8,0x8E,0x7A), Paper, DarkHalo),
        new FenceColorScheme("rose",     "Rose",     Color.FromRgb(0xF2,0xCE,0xD8), Color.FromRgb(0xF2,0xCE,0xD8), Color.FromRgb(0xE0,0xB1,0xBF), Color.FromRgb(0xB8,0x7A,0x8E), Paper, DarkHalo),
        new FenceColorScheme("teal",     "Teal",     Color.FromRgb(0xD4,0xE6,0xE4), Color.FromRgb(0xD4,0xE6,0xE4), Color.FromRgb(0xBB,0xD2,0xCF), Color.FromRgb(0x82,0xA5,0xA2), Paper, DarkHalo),
        new FenceColorScheme("periwinkle","Periwinkle",Color.FromRgb(0xDC,0xDE,0xF0), Color.FromRgb(0xDC,0xDE,0xF0), Color.FromRgb(0xC4,0xC7,0xE0), Color.FromRgb(0x8A,0x8E,0xB8), Paper, DarkHalo),
        // Dark schemes designed for dark wallpapers. Light text + dark halo stays legible even
        // when the user cranks transparency way down.
        new FenceColorScheme("slate",    "Slate",    Color.FromRgb(0x2B,0x35,0x40), Color.FromRgb(0x2B,0x35,0x40), Color.FromRgb(0x22,0x2B,0x35), Color.FromRgb(0x49,0x56,0x6A), Paper, DarkHalo),
        new FenceColorScheme("charcoal", "Charcoal", Color.FromRgb(0x2E,0x2E,0x2E), Color.FromRgb(0x2E,0x2E,0x2E), Color.FromRgb(0x24,0x24,0x24), Color.FromRgb(0x55,0x55,0x55), Paper, DarkHalo),
        new FenceColorScheme("black",    "Black",    Color.FromRgb(0x00,0x00,0x00), Color.FromRgb(0x00,0x00,0x00), Color.FromRgb(0x0A,0x0A,0x0A), Color.FromRgb(0x33,0x33,0x33), Paper, DarkHalo),
    };

    public static FenceColorScheme Get(string? key)
    {
        if (!string.IsNullOrEmpty(key))
            foreach (var s in All) if (s.Key == key) return s;
        return All[0];
    }
}
