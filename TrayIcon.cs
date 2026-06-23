using System;
using System.Drawing;
using System.Windows.Forms;

namespace Pickets;

/// <summary>
/// System-tray presence for Pickets. The fences live at the desktop (WorkerW) layer and can
/// all be hidden at once, leaving the app with no visible surface -- the tray icon is the always-
/// reachable control point: it proves an instance is running and lets the user quit it.
///
/// Built on WinForms <see cref="NotifyIcon"/> (hence &lt;UseWindowsForms&gt; in the csproj). NotifyIcon
/// re-adds itself to the tray automatically when Explorer restarts, so the icon never goes stale.
/// All events fire on the WPF dispatcher thread, so the supplied callbacks need no marshalling.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;

    public TrayIcon(
        Action onToggleVisibility,
        Action onNewFence,
        Action onRestoreAndQuit,
        Action onQuit,
        Func<bool> getRunAtLogin,
        Action<bool> setRunAtLogin)
    {
        var menu = new ContextMenuStrip();

        menu.Items.Add(new ToolStripMenuItem("Show / hide fences", null, (_, _) => onToggleVisibility()));
        menu.Items.Add(new ToolStripMenuItem("New fence", null, (_, _) => onNewFence()));
        menu.Items.Add(new ToolStripSeparator());

        // "&&" renders as a literal ampersand (a single "&" would become a mnemonic underline).
        var runAtLogin = new ToolStripMenuItem("Run at login");
        runAtLogin.Click += (_, _) => setRunAtLogin(!runAtLogin.Checked);
        menu.Items.Add(runAtLogin);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Restore icons && quit", null, (_, _) => onRestoreAndQuit()));
        menu.Items.Add(new ToolStripMenuItem("Quit Pickets", null, (_, _) => onQuit()));

        // Sync the checkmark to actual registry state every time the menu opens, so a change made
        // from a fence's title menu (or another tool) is always reflected.
        menu.Opening += (_, _) => runAtLogin.Checked = getRunAtLogin();

        _icon = new NotifyIcon
        {
            Icon = LoadAppIcon(),
            Text = "Pickets",
            Visible = true,
            ContextMenuStrip = menu,
        };

        // Left-click is the quick "show/hide my fences" toggle; right-click opens the menu (handled
        // automatically by NotifyIcon via ContextMenuStrip).
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) onToggleVisibility();
        };
    }

    /// <summary>Loads the executable's embedded application icon (app.ico) for the tray; falls back
    /// to the generic application icon if extraction fails.</summary>
    private static Icon LoadAppIcon()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
            {
                var ico = Icon.ExtractAssociatedIcon(exe);
                if (ico != null) return ico;
            }
        }
        catch { /* fall through to the system default */ }
        return SystemIcons.Application;
    }

    public void ShowBalloon(string title, string text)
    {
        try
        {
            _icon.BalloonTipTitle = title;
            _icon.BalloonTipText  = text;
            _icon.ShowBalloonTip(3000);
        }
        catch { /* balloon tips are advisory only */ }
    }

    public void Dispose()
    {
        // Hide before Dispose so the icon vanishes immediately rather than lingering until the user
        // hovers the tray (a classic NotifyIcon ghosting gotcha).
        _icon.Visible = false;
        _icon.Dispose();
    }
}
