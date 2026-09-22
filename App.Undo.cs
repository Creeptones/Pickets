using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Pickets;

public partial class App
{
    internal UndoHistory UndoHistory { get; } = new();
    private Window? _undoNotice;

    internal void RecordUndo(string description, Func<bool> restore)
    {
        if (UndoHistory.IsRestoring) return;
        UndoHistory.Record(description, restore);
        if (_tray == null && !_pickets.Any(p => p.IsVisible)) return;
        _undoNotice?.Close();
        var panel = new DockPanel { Margin = new Thickness(12) };
        var undo = new Button { Content = "Undo", Padding = new Thickness(10, 5, 10, 5), ToolTip = "Undo (Ctrl+Z)", Margin = new Thickness(12, 0, 0, 0) };
        DockPanel.SetDock(undo, Dock.Right);
        panel.Children.Add(undo);
        panel.Children.Add(new TextBlock { Text = description, ToolTip = description, TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 64, VerticalAlignment = VerticalAlignment.Center });
        var work = SystemParameters.WorkArea;
        var notice = new Window
        {
            Title = "Pickets — Undo", Width = Math.Min(380, work.Width), SizeToContent = SizeToContent.Height,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowActivated = false,
            ShowInTaskbar = false, Topmost = true, Background = SystemColors.WindowBrush, Foreground = SystemColors.WindowTextBrush,
            Content = new Border { BorderBrush = SystemColors.WindowTextBrush, BorderThickness = new Thickness(1), Child = panel },
            Left = Math.Max(work.Left, work.Right - 392), Top = Math.Max(work.Top, work.Bottom - 100)
        };
        undo.Click += (_, _) => UndoLastAction();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        timer.Tick += (_, _) => notice.Close();
        notice.Closed += (_, _) => { timer.Stop(); if (_undoNotice == notice) _undoNotice = null; };
        _undoNotice = notice;
        notice.Show();
        timer.Start();
    }

    internal void UndoLastAction()
    {
        if (UndoHistory.Description == null) return;
        if (!UndoHistory.Undo())
        {
            MessageBox.Show("Pickets could not finish undoing that action. Your references have been kept. Check that Explorer is available, then try Undo again.", "Pickets");
            return;
        }
        _undoNotice?.Close();
        MarkDirty();
    }

    private void ClearUndoHistory()
    {
        _quickFind?.Close();
        UndoHistory.Clear();
        _undoNotice?.Close();
    }
}
