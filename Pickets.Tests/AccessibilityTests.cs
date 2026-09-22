using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Pickets.Tests;

public sealed class AccessibilityTests
{
    private sealed class TestApp : App
    {
        protected override void OnStartup(StartupEventArgs e) { }
        protected override void OnExit(ExitEventArgs e) { }
    }
    [Fact]
    public void WpfControls_ExposeNamesSelectionAndExpansion_AndRenderWithoutDesktopCapture()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { CheckControls(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(25)), "WPF accessibility check timed out.");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void CheckControls()
    {
        // Never Show these windows: that would attach to Explorer. This tests our real XAML,
        // models and automation peers without installing hooks or touching desktop icons.
        var app = new TestApp();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var windows = new List<PicketWindow>();
        try
        {
            for (var i = 0; i < 3; i++)
            {
                var window = new PicketWindow(new PicketState
                {
                    Title = new[] { "Development", "Music", "Documents" }[i],
                    GroupId = "test-stack", GroupOrder = i, AccordionMode = true,
                    IsCollapsed = i != 1, Width = 340, Height = 280,
                    X = 30, Y = 30 + i * 32,
                    Items = [new ItemState { Path = @"Z:\Offline\Mixdown.wav" }, new ItemState { Kind = ItemKind.Label, LabelText = "Projects" }]
                });
                windows.Add(window);
                ((IList<PicketWindow>)app.Pickets).Add(window);
            }
            windows[0].NormalizeConnectedGroup();
            var music = windows[1];
            var header = (PicketHeaderButton)music.FindName("TitleToggle");
            var peer = UIElementAutomationPeer.CreatePeerForElement(header)!;
            Assert.Equal("Music", peer.GetName());
            var expansion = Assert.IsAssignableFrom<IExpandCollapseProvider>(peer.GetPattern(PatternInterface.ExpandCollapse));
            Assert.Equal(ExpandCollapseState.Expanded, expansion.ExpandCollapseState);
            Assert.NotNull(peer.GetPattern(PatternInterface.Invoke));
            var list = (ListBox)music.FindName("ItemsHost");
            var root = (FrameworkElement)music.Content;
            root.Measure(new Size(music.Width, music.Height));
            root.Arrange(new Rect(0, 0, music.Width, music.Height));
            root.UpdateLayout();
            var item = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
            Assert.NotNull(item);
            var listPeer = UIElementAutomationPeer.CreatePeerForElement(list)!;
            var itemPeer = Assert.Single(listPeer.GetChildren(), p => p.GetName().Contains("Mixdown.wav", StringComparison.Ordinal));
            Assert.Contains("Mixdown.wav", itemPeer.GetName());
            var selection = Assert.IsAssignableFrom<ISelectionItemProvider>(itemPeer.GetPattern(PatternInterface.SelectionItem));
            selection.Select();
            Assert.True(music.Items[0].IsSelected);
            Assert.Single(list.SelectedItems);
            Assert.NotNull(item.ContextMenu);
            Assert.Equal("Picket menu", AutomationProperties.GetName((Button)music.FindName("MenuButton")));
            Assert.Equal(280, music.ToState().Height);
            Capture(root, "pickets-standard.png", music.Width, music.Height);
            music.ApplyHighContrastVisuals();
            Assert.Same(SystemColors.WindowBrush, ((Border)music.FindName("OuterShell")).Background);
            Assert.Same(SystemColors.HighlightTextBrush, music.Resources["PicketSelectionForeground"]);
            root.UpdateLayout();
            Capture(root, "pickets-contrast.png", music.Width, music.Height);
            root.SetValue(Control.FontSizeProperty, 22.0);
            root.Measure(new Size(music.Width, music.Height));
            root.Arrange(new Rect(0, 0, music.Width, music.Height));
            root.UpdateLayout();
            Capture(root, "pickets-large-text.png", music.Width, music.Height);
            expansion.Collapse();
            PumpUntil(() => expansion.ExpandCollapseState == ExpandCollapseState.Collapsed);
            Assert.False((bool)typeof(PicketWindow).GetField("_isRollAnimating", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(music)!, "Animation did not complete.");
            Assert.Equal(ExpandCollapseState.Collapsed, expansion.ExpandCollapseState);
            Assert.True(music.ToState().IsCollapsed);
            var firstHeader = (PicketHeaderButton)windows[0].FindName("TitleToggle");
            Assert.Same(app, Application.Current);
            Assert.Equal(3, ((System.Collections.ICollection)typeof(PicketWindow).GetMethod("ComputeTouchingCluster", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(windows[0], null)!).Count);
            ((IExpandCollapseProvider)UIElementAutomationPeer.CreatePeerForElement(firstHeader)!.GetPattern(PatternInterface.ExpandCollapse)!).Expand();
            PumpUntil(() => !windows[0].ToState().IsCollapsed);
            Assert.False(windows[0].ToState().IsCollapsed);
            Assert.True(music.ToState().IsCollapsed);
            Assert.Equal(windows[0].Top + windows[0].Height, music.Top, 6);
            Assert.Equal(music.Top + music.Height, windows[2].Top, 6);
            var directory = Directory.CreateTempSubdirectory("PicketsUiReference-");
            try
            {
                var path = Path.Combine(directory.FullName, "original.txt");
                var replacementPath = Path.Combine(directory.FullName, "replacement.txt");
                File.WriteAllText(path, "original");
                File.WriteAllText(replacementPath, "replacement");
                music.AddReferences([path], captureDesktop: false);
                windows[2].AddReferences([path], captureDesktop: false);
                var original = Assert.Single(music.Items, i => i.Path == path);
                Assert.Null(original.OriginalDesktopPos);
                music.ReplaceReference(original, replacementPath, false);
                Assert.Single(windows[2].Items, i => i.Path == path);
                var replacement = Assert.Single(music.Items, i => i.Path == replacementPath);
                music.RemoveReferences([replacement]);
                Assert.DoesNotContain(music.Items, i => i.Path == replacementPath);
                Assert.Equal("original", File.ReadAllText(path));
                Assert.Equal("replacement", File.ReadAllText(replacementPath));
                Assert.Single(windows[2].Items, i => i.Path == path);
            }
            finally { directory.Delete(true); }
            CheckReleasePolish(app, windows);
        }
        finally
        {
            foreach (var window in windows) window.CloseForLayoutChange();
            // Do not call App.Shutdown: normal OnExit persists the user's real layout.
            Dispatcher.CurrentDispatcher.InvokeShutdown();
        }
    }

    private static void CheckReleasePolish(App app, List<PicketWindow> windows)
    {
        var empty = new PicketWindow(new PicketState { Title = "Empty", Width = 340, Height = 280 });
        windows.Add(empty);
        ((IList<PicketWindow>)app.Pickets).Add(empty);
        var emptyRoot = (FrameworkElement)empty.Content;
        LayoutAndCapture(emptyRoot, "pickets-empty.png", 340, 280);
        var message = (StackPanel)empty.FindName("EmptyState");
        Assert.Equal(Visibility.Visible, message.Visibility);
        Assert.Contains(Descendants<Button>(message), b => Equals(b.Content, "Add files…"));
        empty.Items.Add(PicketItem.FromPath(@"Z:\Offline\keep.txt"));
        empty.Items[0].OriginalDesktopPos = new POINT { X = 17, Y = 29 };
        emptyRoot.UpdateLayout();
        Assert.Equal(Visibility.Collapsed, message.Visibility);
        var closed = false;
        empty.Closed += (_, _) => closed = true;
        empty.Close();
        Assert.False(closed);
        Assert.True(app.PicketsHidden);
        Assert.Contains(empty, app.Pickets);
        Assert.Equal(17, empty.Items[0].OriginalDesktopPos!.Value.X);
        Assert.Equal(@"Z:\Offline\keep.txt", Assert.Single(empty.ToState().Items).Path);

        var capacity = StackViewport.Page(100, SystemParameters.WorkArea.Height, 0,
            SystemParameters.WorkArea.Width, false).Capacity;
        var paged = new List<PicketWindow>();
        for (var i = 0; i < capacity + 2; i++)
        {
            var p = new PicketWindow(new PicketState { Title = "Page item " + i,
                GroupId = "overflow", GroupOrder = i, Width = 320, Height = 200 });
            paged.Add(p);
            windows.Add(p);
            ((IList<PicketWindow>)app.Pickets).Add(p);
        }
        paged[0].NormalizeConnectedGroup();
        Assert.Equal(capacity, paged.Count(p => p.IsOnStackPage));
        var savedIds = paged.Select(p => p.PicketId).ToArray();
        paged[0].ChangeStackPage(1, focus: false);
        Assert.Equal(2, paged.Count(p => p.IsOnStackPage));
        Assert.All(paged, p => Assert.False(p.IsVisible));
        Assert.Equal(savedIds, paged.Select(p => p.PicketId));
        Assert.All(paged, p => Assert.Equal("overflow", p.ToState().GroupId));
        Assert.Equal(Enumerable.Range(0, paged.Count), paged.Select(p => p.ToState().GroupOrder));
        paged[0].RevealOnStackPage();
        Assert.True(paged[0].IsOnStackPage);
        Assert.False(paged[^1].IsOnStackPage);
        paged[0].ChangeStackPage(1, focus: false);
        paged[0].JoinTouchingGroups();
        Assert.Equal(savedIds, paged.OrderBy(p => p.GroupOrder).Select(p => p.PicketId));

        var readiness = new DesktopReadiness(true, false);
        var welcome = new WelcomeWindow(false, false, () => readiness);
        var dialogs = new AccessibleDialogWindow[] { welcome, new AboutWindow(), new KeyboardHelpWindow("Ctrl+Alt+P") };
        try
        {
            var start = (Button)welcome.FindName("GetStartedButton");
            Assert.False(start.IsEnabled);
            var recheck = Descendants<Button>((FrameworkElement)welcome.Content).Single(b => Equals(b.Content, "Check again"));
            readiness = new DesktopReadiness(false, false);
            recheck.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(start.IsEnabled);
            readiness = new DesktopReadiness(null, false);
            recheck.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(start.IsEnabled);
            for (var i = 0; i < dialogs.Length; i++)
            {
                var dialog = dialogs[i];
                var content = (FrameworkElement)dialog.Content;
                LayoutAndCapture(content, $"dialog-{i}-standard.png", dialog.Width, dialog.Height);
                dialog.ApplyDialogPalette(true);
                Assert.Same(SystemColors.WindowBrush, dialog.Resources["DialogBackground"]);
                content.SetValue(Control.FontSizeProperty, 22.0);
                LayoutAndCapture(content, $"dialog-{i}-small-large-contrast.png", 360, 300);
                var scroll = Descendants<ScrollViewer>(content).First();
                Assert.True(scroll.ScrollableHeight > 0);
                var action = i == 0 ? start : Descendants<Button>(content).Single(b => Equals(b.Content, "Close"));
                var bounds = action.TransformToAncestor(content).TransformBounds(new Rect(action.RenderSize));
                Assert.True(bounds.Top >= 0 && bounds.Bottom <= 300, bounds.ToString());
            }
        }
        finally { foreach (var dialog in dialogs) dialog.Close(); }
        empty.CloseForLayoutChange();
        Assert.True(closed);
        windows.Remove(empty);
        ((IList<PicketWindow>)app.Pickets).Remove(empty);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T value) yield return value;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static void LayoutAndCapture(FrameworkElement root, string filename, double width, double height)
    {
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
        Capture(root, filename, width, height);
    }

    private static void PumpUntil(Func<bool> complete)
    {
        var frame = new DispatcherFrame();
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += (_, _) => { if (complete() || elapsed.Elapsed.TotalSeconds > 5) { timer.Stop(); frame.Continue = false; } };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void Capture(FrameworkElement root, string filename, double width, double height)
    {
        var output = Environment.GetEnvironmentVariable("PICKETS_TEST_RENDER_DIR");
        if (string.IsNullOrEmpty(output)) return;
        Directory.CreateDirectory(output);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width), (int)Math.Ceiling(height), 96, 96, PixelFormats.Pbgra32);
        if (LogicalTreeHelper.GetParent(root) is Window window)
        {
            var background = new DrawingVisual();
            using (var drawing = background.RenderOpen())
                drawing.DrawRectangle(window.Background, null, new Rect(0, 0, width, height));
            bitmap.Render(background);
        }
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(output, filename));
        encoder.Save(stream);
    }
}
