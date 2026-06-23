using System;
using System.Runtime.InteropServices;
using System.Windows.Media;

namespace Pickets;

/// <summary>
/// Per-window acrylic blur via the undocumented SetWindowCompositionAttribute API.
/// Works on Win10 1809+ and Win11 and is compatible with AllowsTransparency=True WPF windows --
/// the documented DWM_SYSTEMBACKDROP_TYPE path requires non-layered windows, which would force
/// us to rebuild the chromeless/rounded-corners setup from scratch.
/// </summary>
public static class WindowBlur
{
    private enum AccentState
    {
        Disabled = 0,
        BlurBehind = 3,
        AcrylicBlurBehind = 4,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public AccentState State;
        public int Flags;
        public uint GradientColor; // little-endian uint byte order is R,G,B,A
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttribData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    private const int WCA_ACCENT_POLICY = 19;
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttribData data);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    public static void Apply(IntPtr hwnd, bool enabled, Color tint)
    {
        if (hwnd == IntPtr.Zero) return;
        var policy = new AccentPolicy
        {
            State = enabled ? AccentState.AcrylicBlurBehind : AccentState.Disabled,
            GradientColor = enabled
                ? ((uint)tint.A << 24) | ((uint)tint.B << 16) | ((uint)tint.G << 8) | tint.R
                : 0,
        };
        var exBefore = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);

        var size = Marshal.SizeOf<AccentPolicy>();
        var ptr = Marshal.AllocHGlobal(size);
        int swcaResult;
        try
        {
            Marshal.StructureToPtr(policy, ptr, false);
            var data = new WindowCompositionAttribData
            {
                Attribute = WCA_ACCENT_POLICY,
                Data = ptr,
                SizeOfData = size,
            };
            swcaResult = SetWindowCompositionAttribute(hwnd, ref data);
        }
        finally { Marshal.FreeHGlobal(ptr); }

        var exAfter = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);

        // On some Win10/Win11 builds, AcrylicBlurBehind silently adds WS_EX_TRANSPARENT, which
        // makes the whole window click-through -- mouse and OLE drops pass straight through to
        // whatever is behind us (the desktop). Clear it so the fence stays interactive.
        long exFinal = exAfter;
        bool clearedTransparent = false;
        if ((exAfter & WS_EX_TRANSPARENT) != 0)
        {
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, (IntPtr)(exAfter & ~WS_EX_TRANSPARENT));
            exFinal = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
            clearedTransparent = true;
        }

        Logger.Log(
            $"WindowBlur.Apply hwnd=0x{hwnd.ToInt64():X} enabled={enabled} " +
            $"tint=#{tint.A:X2}{tint.R:X2}{tint.G:X2}{tint.B:X2} " +
            $"SWCA={swcaResult} " +
            $"exBefore=0x{exBefore:X8} exAfter=0x{exAfter:X8} exFinal=0x{exFinal:X8} " +
            $"transparentWasSet={(exAfter & WS_EX_TRANSPARENT) != 0} cleared={clearedTransparent}");
    }
}
