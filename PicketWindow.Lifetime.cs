using System.ComponentModel;
using System.Linq;
using System.Windows;

namespace Pickets;

public partial class PicketWindow
{
    private bool _allowClose;

    // A desktop widget's ordinary close action must not leave a dead window in App.Pickets.
    // Hide keeps captured icons represented and lets the tray/global shortcut bring them back.
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose && Application.Current is App app && !app.IsShuttingDown && app.Pickets.Contains(this))
        {
            e.Cancel = true;
            app.HidePickets(explain: true);
        }
        base.OnClosing(e);
    }

    internal void CloseForLayoutChange()
    {
        _allowClose = true;
        SettleStack();
        Close();
    }
}
