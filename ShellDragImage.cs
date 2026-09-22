using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace Pickets;

/// <summary>Bridges WPF drag events to the Shell's drag-image manager. Owns no file operations.</summary>
internal sealed class ShellDragImage : IDisposable
{
    private object? _helper;
    private IDropTargetHelper? _target;
    private bool _entered;
    private bool _unavailable;
    internal bool IsActive => _entered;

    internal void Over(IntPtr window, System.Windows.IDataObject data, POINT point, DragDropEffects effect)
    {
        if (_unavailable || data is not ComDataObject native) return;
        try
        {
            _helper ??= new DragDropHelper();
            _target ??= (IDropTargetHelper)_helper;
            var result = _entered ? _target.DragOver(ref point, (uint)effect)
                : _target.DragEnter(window, native, ref point, (uint)effect);
            Marshal.ThrowExceptionForHR(result);
            _entered = true;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            Logger.Log($"Drag image target unavailable: 0x{ex.HResult:X8}");
            Dispose();
            _unavailable = true;
        }
    }

    internal void Drop(System.Windows.IDataObject data, POINT point, DragDropEffects effect)
    {
        try
        {
            if (_entered && data is ComDataObject native) _target?.Drop(native, ref point, (uint)effect);
        }
        finally { Leave(); }
    }

    internal void Leave()
    {
        if (!_entered) return;
        _entered = false;
        _target?.DragLeave();
    }

    public void Dispose()
    {
        Leave();
        _target = null;
        if (_helper != null) Marshal.FinalReleaseComObject(_helper);
        _helper = null;
    }

    internal static IDisposable? CreateSource(DataObject data, BitmapSource image, POINT cursorOffset)
    {
        object? helper = null;
        var bitmapHandle = IntPtr.Zero;
        try
        {
            helper = new DragDropHelper();
            // .NET's data object needs the native flag to preserve the Shell's private formats.
            SetDragLoop(data, true);
            var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            var stride = converted.PixelWidth * 4;
            var pixels = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(pixels, stride, 0);
            using var bitmap = new System.Drawing.Bitmap(converted.PixelWidth, converted.PixelHeight,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var locked = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
                System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                for (var y = 0; y < bitmap.Height; y++)
                    Marshal.Copy(pixels, y * stride, IntPtr.Add(locked.Scan0, y * locked.Stride), stride);
            }
            finally { bitmap.UnlockBits(locked); }
            bitmapHandle = bitmap.GetHbitmap(System.Drawing.Color.FromArgb(0));
            var dragImage = new SHDRAGIMAGE
            {
                Width = bitmap.Width, Height = bitmap.Height, Offset = cursorOffset,
                Bitmap = bitmapHandle, ColorKey = -1
            };
            Marshal.ThrowExceptionForHR(((IDragSourceHelper)helper).InitializeFromBitmap(ref dragImage, data));
            bitmapHandle = IntPtr.Zero; // Ownership passed to the Shell on success.
            return new SourceLifetime(data, helper);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            Logger.Log($"Drag image source unavailable: 0x{ex.HResult:X8}");
            if (bitmapHandle != IntPtr.Zero) DeleteObject(bitmapHandle);
            if (helper != null) Marshal.FinalReleaseComObject(helper);
            ReleaseSourceData(data);
            return null;
        }
    }

    private sealed class SourceLifetime(DataObject data, object helper) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { ReleaseSourceData(data); }
            finally { Marshal.FinalReleaseComObject(helper); }
        }
    }

    private static void ReleaseSourceData(DataObject data)
    {
        // Release only formats created by the drag-image manager, never the reference payload.
        foreach (var format in new[] { "DragImageBits", "DragContext", "DragSourceHelperFlags", "DragWindow",
            "IsShowingLayered", "IsShowingText", "UsingDefaultDragImage", "DropDescription", "InShellDragLoop" })
            if (data.GetDataPresent(format, false) && data.GetData(format, false) is IDisposable value) value.Dispose();
    }

    private static void SetDragLoop(ComDataObject data, bool active)
    {
        var format = new FORMATETC
        {
            cfFormat = unchecked((short)DataFormats.GetDataFormat("InShellDragLoop").Id),
            dwAspect = DVASPECT.DVASPECT_CONTENT, lindex = -1, tymed = TYMED.TYMED_HGLOBAL
        };
        var handle = GlobalAlloc(0x42, 4); // movable, zero-initialized
        if (handle == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        var medium = new STGMEDIUM { tymed = TYMED.TYMED_HGLOBAL, unionmember = handle };
        try
        {
            var pointer = GlobalLock(handle);
            if (pointer == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            try { Marshal.WriteInt32(pointer, active ? 1 : 0); }
            finally { GlobalUnlock(handle); }
            data.SetData(ref format, ref medium, true);
            medium.unionmember = IntPtr.Zero;
        }
        finally { if (medium.unionmember != IntPtr.Zero) ReleaseStgMedium(ref medium); }
    }

    [ComImport, Guid("4657278A-411B-11D2-839A-00C04FD918D0")]
    private class DragDropHelper { }

    [ComImport, Guid("4657278B-411B-11D2-839A-00C04FD918D0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDropTargetHelper
    {
        [PreserveSig] int DragEnter(IntPtr window, ComDataObject data, ref POINT point, uint effect);
        [PreserveSig] int DragLeave();
        [PreserveSig] int DragOver(ref POINT point, uint effect);
        [PreserveSig] int Drop(ComDataObject data, ref POINT point, uint effect);
        [PreserveSig] int Show([MarshalAs(UnmanagedType.Bool)] bool show);
    }

    [ComImport, Guid("DE5BF786-477A-11D2-839D-00C04FD918D0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDragSourceHelper
    {
        [PreserveSig] int InitializeFromBitmap(ref SHDRAGIMAGE image, ComDataObject data);
        [PreserveSig] int InitializeFromWindow(IntPtr window, ref POINT point, ComDataObject data);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SHDRAGIMAGE
    {
        public int Width, Height;
        public POINT Offset;
        public IntPtr Bitmap;
        public int ColorKey;
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalAlloc(uint flags, nuint bytes);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalLock(IntPtr handle);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalUnlock(IntPtr handle);
    [DllImport("ole32.dll")] private static extern void ReleaseStgMedium(ref STGMEDIUM medium);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(IntPtr handle);
}
