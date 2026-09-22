using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Pickets.Tests;

public sealed class DragPreviewTests
{
    [Theory]
    [InlineData(true, true, false, DragDropEffects.All, DragDropEffects.Move)]
    [InlineData(true, false, true, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link, DragDropEffects.Link)]
    [InlineData(true, false, true, DragDropEffects.All, DragDropEffects.Copy)]
    [InlineData(true, false, true, DragDropEffects.Copy, DragDropEffects.Copy)]
    [InlineData(true, false, true, DragDropEffects.Move, DragDropEffects.None)]
    [InlineData(false, true, false, DragDropEffects.All, DragDropEffects.None)]
    [InlineData(false, false, true, DragDropEffects.All, DragDropEffects.None)]
    [InlineData(true, false, false, DragDropEffects.All, DragDropEffects.None)]
    [InlineData(true, true, false, DragDropEffects.Copy, DragDropEffects.None)]
    public void PreviewEffectMatchesReferenceSemantics(bool body, bool internalItem, bool files,
        DragDropEffects allowed, DragDropEffects expected)
        => Assert.Equal(expected, DragPreview.DropEffect(body, internalItem, files, allowed));

    [Fact]
    public void NativeDragImage_PreservesPrivatePayload_AndCleansUpEnterLeaveDrop()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                // A hidden, ordinary HWND: no desktop parenting, capture, hooks, or real files.
                using var window = new HwndSource(new HwndSourceParameters("Pickets drag preview test")
                { Width = 16, Height = 16, WindowStyle = unchecked((int)0x80000000), PositionX = -32000, PositionY = -32000 });
                foreach (var scale in new[] { 1.0, 1.25, 1.5, 2.0 })
                foreach (var large in new[] { false, true })
                foreach (var count in new[] { 1, 3 })
                {
                    var icon = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null,
                        new byte[] { 220, 150, 30, 255, 220, 150, 30, 255, 220, 150, 30, 255, 220, 150, 30, 255 }, 8);
                    icon.Freeze();
                    var item = new PicketItem
                    {
                        Path = @"Z:\Offline\Sample shortcut.lnk", DisplayName = "Sample shortcut", IsLarge = large,
                        Probe = _ => Task.FromResult(new ReferenceCheck(ReferenceStatus.Available, false)),
                        ThumbnailLoader = _ => Task.FromResult<BitmapSource?>(icon)
                    };
                    // Cover both cached thumbnails and the fallback for a missing/not-yet-loaded icon.
                    if (large) item.RefreshAsync().GetAwaiter().GetResult();
                    var preview = DragPreview.Render(item, new DpiScale(scale, scale), count);
                    var output = Environment.GetEnvironmentVariable("PICKETS_TEST_RENDER_DIR");
                    if (!string.IsNullOrEmpty(output))
                    {
                        Directory.CreateDirectory(output);
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(preview));
                        using var stream = File.Create(Path.Combine(output, $"drag-preview-{scale}-{large}-{count}.png"));
                        encoder.Save(stream);
                    }
                    Assert.Equal((int)Math.Ceiling(160 * scale), preview.PixelWidth);
                    Assert.Equal((int)Math.Ceiling((item.IconSize + 44) * scale), preview.PixelHeight);
                    Assert.True(preview.IsFrozen);
                    var pixels = new byte[preview.PixelWidth * preview.PixelHeight * 4];
                    preview.CopyPixels(pixels, preview.PixelWidth * 4, 0);
                    Assert.Contains(Enumerable.Range(0, pixels.Length / 4), i => pixels[i * 4 + 3] != 0);
                    Assert.Contains(Enumerable.Range(0, pixels.Length / 4), i => pixels[i * 4 + 3] == 0);
                    var data = new DataObject();
                    var privatePayload = new object();
                    data.SetData("Pickets.PreviewTestPayload", privatePayload, false);
                    using (var source = ShellDragImage.CreateSource(data, preview, new POINT(20, 20)))
                    {
                        Assert.NotNull(source);
                        Assert.Same(privatePayload, data.GetData("Pickets.PreviewTestPayload", false));
                        Assert.False(data.GetDataPresent(DataFormats.FileDrop));
                        Assert.True(data.GetDataPresent("DragImageBits", false));
                        using var target = new ShellDragImage();
                        var cursor = new POINT(-32000, -32000);
                        // Wrap as an incoming COM object, as WPF does for external drags.
                        var incoming = new DataObject((System.Runtime.InteropServices.ComTypes.IDataObject)data);
                        target.Over(window.Handle, incoming, cursor, DragDropEffects.Link);
                        Assert.True(target.IsActive);
                        target.Over(window.Handle, incoming, cursor, DragDropEffects.None);
                        target.Leave();
                        Assert.False(target.IsActive);
                        target.Over(window.Handle, incoming, cursor, DragDropEffects.Link);
                        Assert.True(target.IsActive);
                        target.Drop(incoming, cursor, DragDropEffects.Link);
                        Assert.False(target.IsActive);
                        target.Leave();
                        target.Over(window.Handle, incoming, cursor, DragDropEffects.Move);
                        // A new drag can arrive without the old visual target receiving Leave.
                        var nextDrag = new DataObject((System.Runtime.InteropServices.ComTypes.IDataObject)data);
                        target.Over(window.Handle, nextDrag, cursor, DragDropEffects.Link);
                        Assert.True(target.IsActive);
                        target.Drop(nextDrag, cursor, DragDropEffects.Link);
                        Assert.False(target.IsActive);
                        target.Dispose();
                        Assert.False(target.IsActive);
                    }
                    Assert.Same(privatePayload, data.GetData("Pickets.PreviewTestPayload", false));
                    Assert.False(data.GetDataPresent(DataFormats.FileDrop));
                }
            }
            catch (Exception ex) { failure = ex; }
            finally { System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(25)), "Native drag preview check timed out.");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
