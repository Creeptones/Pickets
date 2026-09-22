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
                    IsCollapsed = i != 1, Width = 340, Height = 280, AutoSizeRows = false,
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
            CheckStackMembershipChanges(app, windows);
            CheckStackRetargeting(app, windows);
            CheckLabelBodyDropRouting(app, windows);
            CheckCompactRows(app, windows);
            CheckRectangleSelection(app, windows);
            CheckReleasePolish(app, windows);
        }
        finally
        {
            foreach (var window in windows) window.CloseForLayoutChange();
            app.Frames.Dispose();
            // Do not call App.Shutdown: normal OnExit persists the user's real layout.
            Dispatcher.CurrentDispatcher.InvokeShutdown();
        }
    }

    private static void CheckRectangleSelection(App app, List<PicketWindow> windows)
    {
        var window = new PicketWindow(new PicketState { Title = "Rectangle selection", Width = 360, Height = 225, AutoSizeRows = false });
        var target = new PicketWindow(new PicketState { Title = "Bulk destination", AutoSizeRows = false });
        windows.AddRange([window, target]);
        ((IList<PicketWindow>)app.Pickets).Add(window);
        ((IList<PicketWindow>)app.Pickets).Add(target);
        for (var i = 0; i < 15; i++) window.Items.Add(new PicketItem { Path = @"Z:\fixture\" + i, DisplayName = "Reference " + i });
        var content = window.Content;
        window.Content = null;
        var host = new Window { Content = content, Resources = window.Resources, Left = -32000, Top = -32000, Width = 360, Height = 225,
            ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true, Background = Brushes.Transparent };
        host.Show();
        host.UpdateLayout();
        try
        {
            var body = (FrameworkElement)window.FindName("BodyArea");
            var scroll = (ScrollViewer)window.FindName("BodyScroll");
            var list = (ListBox)window.FindName("ItemsHost");
            Point InItem(int index)
            {
                var cell = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(index);
                return cell.TranslatePoint(new Point(8, 40), body);
            }
            var start = new Point(5, 5);
            window.BeginBoxSelection(start, System.Windows.Input.ModifierKeys.None);
            window.UpdateBoxSelection(InItem(1));
            Assert.Equal(new[] { window.Items[0], window.Items[1] }, list.SelectedItems.Cast<PicketItem>());
            host.UpdateLayout();
            Assert.True(((FrameworkElement)window.FindName("SelectionBox")).IsVisible);
            Capture((FrameworkElement)content, "pickets-rectangle-selection.png", 360, 225);
            window.EndBoxSelection();
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)window.FindName("SelectionBox")).Visibility);
            Assert.Equal(2, window.ActionItems(window.Items[0]).Length);
            var selectedCell = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
            var prepareMenu = typeof(PicketWindow).GetMethod("Item_ContextMenuOpening", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            prepareMenu.Invoke(window, [selectedCell, null!]);
            var menu = selectedCell.ContextMenu;
            menu.DataContext = window.Items[0];
            Assert.Contains(menu.Items.OfType<MenuItem>(), entry => Equals(entry.Header, "Remove 2 items from picket"));
            var large = menu.Items.OfType<MenuItem>().Single(entry => Equals(entry.Tag, "LargeToggle"));
            large.IsChecked = true;
            large.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.True(window.Items[0].IsLarge && window.Items[1].IsLarge);
            Assert.False(window.Items[2].IsLarge);
            large.IsChecked = false;
            large.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            host.UpdateLayout();

            window.BeginBoxSelection(start, System.Windows.Input.ModifierKeys.Control);
            window.UpdateBoxSelection(InItem(0));
            window.EndBoxSelection();
            Assert.Same(window.Items[1], Assert.Single(list.SelectedItems.Cast<PicketItem>()));
            window.BeginBoxSelection(start, System.Windows.Input.ModifierKeys.Shift);
            window.UpdateBoxSelection(InItem(0));
            window.EndBoxSelection();
            Assert.Equal(2, list.SelectedItems.Count);
            window.BeginBoxSelection(start, System.Windows.Input.ModifierKeys.None);
            window.UpdateBoxSelection(InItem(2));
            window.EndBoxSelection(cancel: true);
            Assert.Equal(new[] { window.Items[0], window.Items[1] }, list.SelectedItems.Cast<PicketItem>().OrderBy(i => i.Path));

            window.BeginBoxSelection(start, System.Windows.Input.ModifierKeys.None);
            window.UpdateBoxSelection(new Point(220, body.ActualHeight - 5));
            var tick = typeof(PicketWindow).GetMethod("SelectionFrame", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            for (var i = 0; i < 20; i++) tick.Invoke(window, [TimeSpan.FromMilliseconds(i * 50)]);
            Assert.True(scroll.VerticalOffset > 0);
            Assert.Contains(list.SelectedItems.Cast<PicketItem>(), item => window.Items.IndexOf(item) >= 6);
            window.EndBoxSelection();

            // Bulk transfer preserves capture ownership and leaves duplicates in the source.
            var first = window.Items[0];
            var duplicate = window.Items[1];
            first.OriginalDesktopPos = new POINT(42, 64);
            target.Items.Add(new PicketItem { Path = duplicate.Path, DisplayName = "Existing reference" });
            var moved = window.TransferReferences([first, duplicate], target);
            Assert.Same(first, Assert.Single(moved));
            Assert.Contains(duplicate, window.Items);
            Assert.Contains(first, target.Items);
            Assert.Equal(42, first.OriginalDesktopPos!.Value.X);
            first.OriginalDesktopPos = null; // The fixture never owns an actual desktop icon.
            window.RemoveReferences([duplicate]);
            Assert.DoesNotContain(duplicate, window.Items);
        }
        finally
        {
            window.EndBoxSelection(cancel: true);
            host.Content = null;
            host.Close();
            window.Content = content;
            window.CloseForLayoutChange();
            target.CloseForLayoutChange();
            ((IList<PicketWindow>)app.Pickets).Remove(window);
            ((IList<PicketWindow>)app.Pickets).Remove(target);
            windows.Remove(window);
            windows.Remove(target);
        }
    }

    private static void CheckCompactRows(App app, List<PicketWindow> windows)
    {
        foreach (var large in new[] { false, true })
        foreach (var font in new[] { 12.0, 22.0 })
        {
            var width = 18 + 3 * (PicketContentSizing.CellWidth(large, font) + 6) + SystemParameters.VerticalScrollBarWidth;
            var window = new PicketWindow(new PicketState { Title = "Compact rows", Width = width, AutoSizeRows = true })
                { FontSize = font };
            windows.Add(window);
            ((IList<PicketWindow>)app.Pickets).Add(window);
            for (var i = 0; i < 7; i++)
            {
                if (i == 3) window.Items.Add(PicketItem.CreateLabel("Section label"));
                window.Items.Add(new PicketItem { DisplayName = "Example reference " + i, IsLarge = large });
            }
            window.NormalizeConnectedGroup();
            var content = window.Content;
            window.Content = null;
            var host = new Window { Content = content, Left = -32000, Top = -32000,
                Width = width, Height = window.Height, FontSize = font,
                ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
                AllowsTransparency = true, Background = Brushes.Transparent };
            host.Show();
            host.UpdateLayout();
            try
            {
                var frame = DateTime.UtcNow.Ticks;
                void SettleContent()
                {
                    for (var pass = 0; pass < 4; pass++)
                    {
                        host.UpdateLayout();
                        app.Frames.ProcessFrame(TimeSpan.FromTicks(++frame));
                        host.Height = window.Height;
                    }
                    host.UpdateLayout();
                }
                SettleContent();
                var body = (ScrollViewer)window.FindName("BodyScroll");
                var list = (ListBox)window.FindName("ItemsHost");
                var sixth = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(6);
                var seventh = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(7);
                Capture((FrameworkElement)content, $"compact-rows-{large}-{font}.png", width, window.Height);
                Assert.True(sixth.TranslatePoint(new Point(0, sixth.ActualHeight), body).Y <= body.ActualHeight - 3,
                    $"Second row clipped: large={large}, font={font}, bottom={sixth.TranslatePoint(new Point(0, sixth.ActualHeight), body).Y}, body={body.ActualHeight}, viewportWidth={body.ViewportWidth}, listWidth={list.ActualWidth}, cell={sixth.DesiredSize}, dpi={VisualTreeHelper.GetDpi(body).DpiScaleX}");
                Assert.True(seventh.TranslatePoint(new Point(0, 10), body).Y >= body.ActualHeight - 4,
                    "Third icon row should require scrolling.");
                Assert.True(body.ScrollableHeight > 0);
                Capture((FrameworkElement)content, $"compact-rows-{large}-{font}.png", width, window.Height);
                var twoRows = window.ToState().Height;
                while (window.Items.Count > 3) window.Items.RemoveAt(window.Items.Count - 1);
                SettleContent();
                Assert.True(window.ToState().Height < twoRows);
                Assert.True(window.ToState().AutoSizeRows);
                var checkingHeight = window.ToState().Height;
                foreach (var item in window.Items) item.IsMissing = false;
                SettleContent();
                Assert.True(window.ToState().Height < checkingHeight, "Available references should not reserve empty status space.");
            }
            finally
            {
                host.Content = null;
                host.Close();
                window.Content = content;
                window.CloseForLayoutChange();
                ((IList<PicketWindow>)app.Pickets).Remove(window);
                windows.Remove(window);
            }
        }
    }

    private static void CheckLabelBodyDropRouting(App app, List<PicketWindow> windows)
    {
        var window = new PicketWindow(new PicketState
        {
            Title = "Drop routing fixture", X = -32000, Y = -32000, Width = 788, Height = 260
        });
        windows.Add(window);
        ((IList<PicketWindow>)app.Pickets).Add(window);
        for (var i = 0; i < 6; i++) window.Items.Add(new PicketItem { DisplayName = "Reference " + i });
        window.Items.Add(PicketItem.CreateLabel("Label test"));
        // Host the real controls offscreen in an ordinary window. PicketWindow is never shown,
        // so its Explorer parenting and desktop hooks cannot run.
        var content = window.Content;
        window.Content = null;
        var host = new Window { Content = content, Left = -32000, Top = -32000, Width = 788, Height = 260,
            ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true, Background = Brushes.Transparent };
        host.Show();
        host.UpdateLayout();
        try
        {
            var body = (ScrollViewer)window.FindName("BodyScroll");
            var argsConstructor = typeof(DragEventArgs).GetConstructors(System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic).Single();
            var policy = typeof(PicketWindow).GetMethod("GetDropEffect", System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic)!;
            var data = new DataObject(DataFormats.FileDrop, new[] { @"Z:\fixture-only\Ephemera Copy.exe" });
            foreach (var x in new[] { 15.0, 150.0, 400.0, 720.0 })
            foreach (var y in new[] { 15.0, 100.0, body.ActualHeight - 15 })
            {
                var bodyPoint = new Point(x, y);
                var windowPoint = body.TranslatePoint(bodyPoint, host);
                var hit = Assert.IsAssignableFrom<UIElement>(host.InputHitTest(windowPoint));
                var hitPoint = body.TranslatePoint(bodyPoint, hit);
                var args = (DragEventArgs)argsConstructor.Invoke([data, DragDropKeyStates.LeftMouseButton,
                    DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link, hit, hitPoint]);
                Assert.Equal(DragDropEffects.Link, (DragDropEffects)policy.Invoke(window, [args])!);
            }
        }
        finally
        {
            host.Content = null;
            host.Close();
            window.Content = content;
            window.CloseForLayoutChange();
            ((IList<PicketWindow>)app.Pickets).Remove(window);
            windows.Remove(window);
        }
    }

    private static void CheckStackRetargeting(App app, List<PicketWindow> windows)
    {
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var prepare = typeof(PicketWindow).GetMethod("CreateStackAnimation", flags)!;
        var active = typeof(PicketWindow).GetField("_isRollAnimating", flags)!;
        foreach (var horizontal in new[] { false, true })
        foreach (var accordion in horizontal ? new[] { false } : new[] { false, true })
        {
            var members = new List<PicketWindow>();
            for (var i = 0; i < 3; i++)
            {
                var member = new PicketWindow(new PicketState
                {
                    Title = "Retarget " + i, GroupId = "retarget", GroupOrder = i,
                    GroupHorizontal = horizontal, AccordionMode = accordion, AutoSizeRows = false,
                    IsCollapsed = i != 0, Width = 200, Height = 160, X = 30, Y = 30 + i * 32
                });
                members.Add(member);
                windows.Add(member);
                ((IList<PicketWindow>)app.Pickets).Add(member);
            }
            members[0].NormalizeConnectedGroup();
            var original = Bounds();
            var closing = Begin(0, true);
            closing(0.35);
            CheckSeams();
            var midway = Bounds();
            var midwayAngles = Angles();
            var reversing = Begin(0, false);
            Assert.Equal(midway, Bounds());
            reversing(0);
            Assert.Equal(midway, Bounds());
            Assert.Equal(midwayAngles, Angles());
            closing(1); // A cancelled callback cannot finish over the newer transition.
            Assert.Equal(midway, Bounds());
            reversing(0.5);
            CheckSeams();
            reversing(1);
            Assert.Equal(original, Bounds());
            Assert.False(members[0].ToState().IsCollapsed);

            var openSecond = Begin(1, false);
            openSecond(0.3);
            var beforeSwitch = Bounds();
            var openThird = Begin(2, false);
            openThird(0);
            Assert.Equal(beforeSwitch, Bounds());
            openSecond(1);
            Assert.Equal(beforeSwitch, Bounds());
            openThird(0.6);
            CheckSeams();
            openThird(1);
            Assert.False(members[2].ToState().IsCollapsed);
            Assert.Equal(accordion, members[0].ToState().IsCollapsed);
            Assert.Equal(accordion, members[1].ToState().IsCollapsed);
            Assert.All(members, p => Assert.False((bool)active.GetValue(p)!));

            // Keyboard/automation requests must reverse the intended state too. Hidden windows
            // finish immediately, exercising the no-animation path used by reduced motion.
            var pending = Begin(2, true);
            pending(0.4);
            members[2].SetExpanded(true);
            Assert.False(members[2].ToState().IsCollapsed);
            pending(1);
            Assert.False(members[2].ToState().IsCollapsed);
            var last = Begin(0, false);
            last(0.2);
            members[1].SettleStack(); // Settle through a member that does not own the animation.
            Assert.All(members, p => Assert.False((bool)active.GetValue(p)!));
            CheckSeams();
            foreach (var member in members)
            {
                app.DeletePicket(member);
                windows.Remove(member);
            }

            Action<double> Begin(int index, bool collapsed)
                => (Action<double>)prepare.Invoke(members[index], [collapsed])!;
            Rect[] Bounds() => members.Select(p => new Rect(p.Left, p.Top, p.Width, p.Height)).ToArray();
            double[] Angles() => members.Select(p => ((RotateTransform)p.FindName("ChevronRotation")).Angle).ToArray();
            void CheckSeams()
            {
                for (var i = 1; i < members.Count; i++)
                    Assert.Equal(horizontal ? members[i - 1].Left + members[i - 1].Width : members[i - 1].Top + members[i - 1].Height,
                        horizontal ? members[i].Left : members[i].Top, 6);
            }
        }
    }

    private static void CheckStackMembershipChanges(App app, List<PicketWindow> windows)
    {
        // Exercise the real windows and App deletion path without showing them on the desktop.
        foreach (var horizontal in new[] { false, true })
        foreach (var accordion in horizontal ? new[] { false } : new[] { false, true })
        {
            var members = new List<PicketWindow>();
            var id = Guid.NewGuid().ToString();
            for (var i = 0; i < 4; i++)
            {
                var member = new PicketWindow(new PicketState
                {
                    Title = "Membership " + i, GroupId = id, GroupOrder = i,
                    GroupHorizontal = horizontal, AccordionMode = accordion, AutoSizeRows = false,
                    IsCollapsed = i != 0, Width = 200, Height = 160, X = 30, Y = 30 + i * 32
                });
                members.Add(member);
                windows.Add(member);
                ((IList<PicketWindow>)app.Pickets).Add(member);
            }
            members[0].NormalizeConnectedGroup();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var resizeField = typeof(PicketWindow).GetField("_resizeCluster", flags)!;
            resizeField.SetValue(members[0], members);
            typeof(PicketWindow).GetField("_resizeOrientation", flags)!.SetValue(members[0],
                horizontal ? GroupOrientation.Row : GroupOrientation.Column);
            var widthBefore = members[0].Width;
            var heightBefore = members[0].ToState().Height;
            typeof(PicketWindow).GetMethod("ResizeCluster", flags)!.Invoke(members[0], [60.0, 30.0]);
            resizeField.SetValue(members[0], null);
            var requestedWidth = widthBefore + (horizontal ? 15 : 60);
            var pixelWidth = 1 / VisualTreeHelper.GetDpi(members[0]).DpiScaleX;
            Assert.All(members, p => Assert.InRange(p.Width, requestedWidth - pixelWidth, requestedWidth + 0.000001));
            Assert.All(members, p => Assert.Equal(heightBefore + 30, p.ToState().Height, 6));
            CheckSeams();
            var menu = (ContextMenu)members[0].Resources["TitleContextMenu"];
            Assert.DoesNotContain(menu.Items.OfType<MenuItem>(), item =>
                Equals(item.Header, "Collapse picket") || Equals(item.Header, "Expand picket") || Equals(item.Tag, "Expansion"));

            var added = new PicketWindow(new PicketState { Title = "Inserted", Width = 320, Height = 240 });
            windows.Add(added);
            ((IList<PicketWindow>)app.Pickets).Add(added);
            added.AttachAfter(members[1]);
            members.Insert(2, added);
            CheckSeams();
            Assert.Equal(horizontal, added.ToState().GroupHorizontal);
            Assert.Equal(accordion, added.ToState().AccordionMode);
            if (accordion) Assert.Same(added, Assert.Single(members, p => !p.ToState().IsCollapsed));

            // Model an in-flight animation owned by a different section. Deletion must finish
            // it while the old members still exist, before closing the gap for the new stack.
            var finishField = typeof(PicketWindow).GetField("_finishRollAnimation",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var settled = false;
            finishField.SetValue(members[0], (Action)(() =>
            {
                Assert.Contains(added, app.Pickets);
                settled = true;
                finishField.SetValue(members[0], null);
                members[^1].Top += 160;
            }));
            app.DeletePicket(added);
            Assert.True(settled);
            members.Remove(added);
            windows.Remove(added);
            CheckSeams();

            // Deleting the first section must not move the whole stack down/right into its hole.
            var first = members[0];
            var anchor = new Point(first.Left, first.Top);
            app.DeletePicket(first);
            members.Remove(first);
            windows.Remove(first);
            Assert.Equal(anchor.X, members[0].Left, 6);
            Assert.Equal(anchor.Y, members[0].Top, 6);
            CheckSeams();
            var last = members[^1];
            app.DeletePicket(last);
            members.Remove(last);
            windows.Remove(last);
            CheckSeams();
            foreach (var member in members)
            {
                app.DeletePicket(member);
                windows.Remove(member);
            }

            void CheckSeams()
            {
                Assert.All(members, p => Assert.Equal(id, p.GroupId));
                Assert.Equal(Enumerable.Range(0, members.Count), members.Select(p => p.GroupOrder));
                for (var i = 1; i < members.Count; i++)
                {
                    Assert.Equal(members[0].Width, members[i].Width, 6);
                    Assert.Equal(horizontal ? members[i - 1].Left + members[i - 1].Width
                        : members[i - 1].Top + members[i - 1].Height,
                        horizontal ? members[i].Left : members[i].Top, 6);
                }
            }
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
