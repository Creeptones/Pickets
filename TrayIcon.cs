using System;
using System.Drawing;
using System.Windows.Forms;

namespace Pickets;

/// <summary>
/// System-tray presence for Pickets. The pickets live at the desktop (WorkerW) layer and can
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
        Action onFocus,
        Action onShortcutSettings,
        Action onKeyboardHelp,
        Action onNewPicket,
        Action onShowWelcome,
        Action onReleaseAndQuit,
        Action onQuit,
        Action onExitHidden,
        Action onAbout,
        Func<bool> getRunAtLogin,
        Action<bool> setRunAtLogin)
    {
        var menu = new ContextMenuStrip();

        menu.Items.Add(new ToolStripMenuItem("Show / hide pickets", null, (_, _) => onToggleVisibility()));
        menu.Items.Add(new ToolStripMenuItem("Focus Pickets", null, (_, _) => onFocus()));
        menu.Items.Add(new ToolStripMenuItem("Change focus shortcut...", null, (_, _) => onShortcutSettings()));
        menu.Items.Add(new ToolStripMenuItem("Keyboard help...", null, (_, _) => onKeyboardHelp()));
        menu.Items.Add(new ToolStripMenuItem("New picket", null, (_, _) => onNewPicket()));
        menu.Items.Add(new ToolStripMenuItem("Quick start guide...", null, (_, _) => onShowWelcome()));
        menu.Items.Add(new ToolStripSeparator());

        // "&&" renders as a literal ampersand (a single "&" would become a mnemonic underline).
        var runAtLogin = new ToolStripMenuItem("Start at sign-in");
        runAtLogin.Click += (_, _) => setRunAtLogin(!runAtLogin.Checked);
        menu.Items.Add(runAtLogin);
        menu.Items.Add(new ToolStripMenuItem("About Pickets...", null, (_, _) => onAbout()));

        menu.Items.Add(new ToolStripSeparator());
        var advanced = new ToolStripMenuItem("Advanced");
        advanced.DropDownItems.Add(new ToolStripMenuItem("Release all icons && quit...", null, (_, _) => onReleaseAndQuit()));
        advanced.DropDownItems.Add(new ToolStripMenuItem("Exit and keep icons hidden", null, (_, _) => onExitHidden()));
        menu.Items.Add(advanced);
        menu.Items.Add(new ToolStripMenuItem("Quit Pickets...", null, (_, _) => onQuit()));

        // Sync the checkmark to actual registry state every time the menu opens, so a change made
        // from a picket's title menu (or another tool) is always reflected.
        menu.Opening += (_, _) => runAtLogin.Checked = getRunAtLogin();

        _icon = new NotifyIcon
        {
            Icon = LoadAppIcon(),
            Text = "Pickets",
            Visible = true,
            ContextMenuStrip = menu,
        };

        // Left-click is the quick "show/hide my pickets" toggle; right-click opens the menu (handled
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
