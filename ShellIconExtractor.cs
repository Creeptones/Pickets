using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Pickets;

/// <summary>
/// Fetches a visual for a path. Prefers IShellItemImageFactory so image files get real thumbnails
/// (same as Explorer) and folders/files get high-res icons; falls back to SHGetFileInfo on failure.
/// </summary>
public static class ShellIconExtractor
{
    public static BitmapSource? GetIcon(string path, int preferredSize = 96)
    {
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            int hr = SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var factory);
            if (hr != 0 || factory == null) return GetIconLegacy(path);
            try
            {
                var sz = new SIZE { cx = preferredSize, cy = preferredSize };
                // ResizeToFit + BiggerSizeOk tells the shell "give me up to preferredSize; a bigger
                // cached thumbnail is fine, I'll downscale". That's what Explorer does.
                hr = factory.GetImage(sz, SIIGBF.ResizeToFit | SIIGBF.BiggerSizeOk, out IntPtr hBitmap);
                if (hr != 0 || hBitmap == IntPtr.Zero) return GetIconLegacy(path);
                try
                {
                    return HBitmapToBitmapSource(hBitmap) ?? GetIconLegacy(path);
                }
                finally { DeleteObject(hBitmap); }
            }
            finally { Marshal.ReleaseComObject(factory); }
        }
        catch
        {
            return GetIconLegacy(path);
        }
    }

    // IShellItemImageFactory hands back a 32bpp premultiplied bitmap. CreateBitmapSourceFromHBitmap
    // drops the alpha on 32bpp DIBs (renders a black square where transparency should be), so we
    // copy the bits into a WriteableBitmap with PBGRA32 format to preserve it.
    private static BitmapSource? HBitmapToBitmapSource(IntPtr hBitmap)
    {
        var dib = new DIBSECTION();
        if (GetObjectDibSection(hBitmap, Marshal.SizeOf<DIBSECTION>(), ref dib) == 0
            || dib.dsBm.bmBits == IntPtr.Zero
            || dib.dsBm.bmBitsPixel != 32)
        {
            // Non-32bpp or not a DIB section: standard path works fine.
            var fallback = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            fallback.Freeze();
            return fallback;
        }

        int w = dib.dsBm.bmWidth, h = dib.dsBm.bmHeight;
        int stride = w * 4;
        int byteCount = stride * h;
        var pixels = new byte[byteCount];

        // biHeight > 0 = bottom-up DIB (origin at bottom-left); biHeight < 0 = top-down.
        // Win11's IShellItemImageFactory returns bottom-up for folder icons, which renders
        // upside-down if copied directly. Flip row order while copying when needed.
        if (dib.dsBmih.biHeight > 0)
        {
            for (int y = 0; y < h; y++)
            {
                IntPtr srcRow = dib.dsBm.bmBits + (h - 1 - y) * stride;
                Marshal.Copy(srcRow, pixels, y * stride, stride);
            }
        }
        else
        {
            Marshal.Copy(dib.dsBm.bmBits, pixels, 0, byteCount);
        }

        // Win11 sometimes returns folder icons where the region around the artwork is filled
        // with black instead of being transparent -- opaque black at the core, anti-aliased
        // dark pixels at the edges. If all four corners read as that filler (RGB=0, any alpha),
        // strip black: treat every pixel whose RGB is 0 as transparent regardless of alpha, so
        // anti-aliased halos don't leave a dark ring on light pickets.
        int tr = (w - 1) * 4;
        int bl = (h - 1) * stride;
        int br = bl + tr;
        if (IsFillerBlack(pixels, 0) && IsFillerBlack(pixels, tr) &&
            IsFillerBlack(pixels, bl) && IsFillerBlack(pixels, br))
        {
            for (int i = 0; i < pixels.Length; i += 4)
            {
                if (pixels[i] == 0 && pixels[i + 1] == 0 && pixels[i + 2] == 0)
                    pixels[i + 3] = 0;
            }
        }

        var wb = new WriteableBitmap(w, h, 96, 96, PixelFormats.Pbgra32, null);
        wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
        wb.Freeze();
        return wb;
    }

    // "Filler black": BGR all zero with any alpha. Corner pixels matching this pattern mean the
    // icon was delivered with a black-fill background rather than a transparent one.
    private static bool IsFillerBlack(byte[] pixels, int offset) =>
        pixels[offset] == 0 && pixels[offset + 1] == 0 && pixels[offset + 2] == 0;

    // === IShellItemImageFactory ===

    [ComImport, Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int cx; public int cy; }

    [Flags]
    private enum SIIGBF
    {
        ResizeToFit   = 0x00,
        BiggerSizeOk  = 0x01,
        MemoryOnly    = 0x02,
        IconOnly      = 0x04,
        ThumbnailOnly = 0x08,
        InCacheOnly   = 0x10,
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(
        string pszPath, IntPtr pbc, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory? ppv);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll", EntryPoint = "GetObject")]
    private static extern int GetObjectBitmap(IntPtr hObject, int nCount, ref BITMAP lpObject);

    [DllImport("gdi32.dll", EntryPoint = "GetObject")]
    private static extern int GetObjectDibSection(IntPtr hObject, int nCount, ref DIBSECTION lpObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DIBSECTION
    {
        public BITMAP dsBm;
        public BITMAPINFOHEADER dsBmih;
        public uint dsBitField0;
        public uint dsBitField1;
        public uint dsBitField2;
        public IntPtr dshSection;
        public uint dsOffset;
    }

    // === Legacy fallback: SHGetFileInfo (small icon only; used when IShellItemImageFactory fails) ===

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
        ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private static BitmapSource? GetIconLegacy(string path)
    {
        var info = new SHFILEINFO();
        uint flags = SHGFI_ICON | SHGFI_LARGEICON;
        var result = SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), flags);
        if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;
        try
        {
            var bmp = Imaging.CreateBitmapSourceFromHIcon(
                info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
        finally { DestroyIcon(info.hIcon); }
    }
}
