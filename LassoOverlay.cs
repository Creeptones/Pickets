using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Pickets;

/// <summary>
/// Transparent topmost window that spans the entire virtual desktop and renders the
/// lasso rectangle. Click-through via WS_EX_TRANSPARENT so mouse events continue to
/// reach the desktop listview below (we're not handling input here -- DesktopLassoHook
/// does that via a global mouse hook).
/// </summary>
public sealed class LassoOverlay : IDisposable
{
    private readonly Window _window;
    private readonly Rectangle _rect;
    private readonly int _vsLeft, _vsTop;

    public LassoOverlay()
    {
        _vsLeft = WindowInterop.GetSystemMetrics(WindowInterop.SM_XVIRTUALSCREEN);
        _vsTop  = WindowInterop.GetSystemMetrics(WindowInterop.SM_YVIRTUALSCREEN);
        var vsWidth  = WindowInterop.GetSystemMetrics(WindowInterop.SM_CXVIRTUALSCREEN);
        var vsHeight = WindowInterop.GetSystemMetrics(WindowInterop.SM_CYVIRTUALSCREEN);

        var canvas = new System.Windows.Controls.Canvas
        {
            Background = System.Windows.Media.Brushes.Transparent,
        };

        _rect = new Rectangle
        {
            // Soft white fill, thin bright stroke -- readable on both light and dark wallpapers.
            Fill = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            Stroke = new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF)),
            StrokeThickness = 1.2,
            StrokeDashArray = new DoubleCollection { 3, 2 },
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };
        canvas.Children.Add(_rect);

        _window = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            ShowInTaskbar = false,
            Topmost = true,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = _vsLeft, Top = _vsTop,
            Width = vsWidth, Height = vsHeight,
            IsHitTestVisible = false,
            Focusable = false,
            Content = canvas,
        };

        _window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(_window).Handle;
            var ex = WindowInterop.GetWindowLong(hwnd, WindowInterop.GWL_EXSTYLE);
            WindowInterop.SetWindowLong(hwnd, WindowInterop.GWL_EXSTYLE,
                ex | WindowInterop.WS_EX_TRANSPARENT
                   | WindowInterop.WS_EX_TOOLWINDOW
                   | WindowInterop.WS_EX_NOACTIVATE);
        };
    }

    public void Show(RECT screenRect)
    {
        UpdateRect(screenRect);
        _rect.Visibility = Visibility.Visible;
        if (!_window.IsVisible) _window.Show();
    }

    public void UpdateRect(RECT screenRect)
    {
        // Canvas coords are virtual-screen-local (window is positioned at virtual-screen origin).
        System.Windows.Controls.Canvas.SetLeft(_rect, screenRect.left - _vsLeft);
        System.Windows.Controls.Canvas.SetTop(_rect,  screenRect.top  - _vsTop);
        _rect.Width  = Math.Max(0, screenRect.right  - screenRect.left);
        _rect.Height = Math.Max(0, screenRect.bottom - screenRect.top);
    }

    public void Hide()
    {
        _rect.Visibility = Visibility.Collapsed;
        if (_window.IsVisible) _window.Hide();
    }

    public void Dispose()
    {
        _window.Close();
    }
}
