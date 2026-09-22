# Pickets user guide

[Back to the overview](../README.md)

Detailed setup, controls, and recovery instructions for the current source. Release-candidate
features still need [hands-on sign-off](TEST_RELEASE.md).

- [Install, update, or uninstall](#install-and-first-run)
- [Required desktop settings](#before-first-use)
- [Everyday controls and stacks](#using-pickets)
- [File references and dragging](#file-references-not-file-operations)
- [Keyboard and accessibility](#keyboard-and-accessibility)
- [Local data and recovery](#local-data-and-recovery)
- [Troubleshooting](#troubleshooting)
- [Privacy and security](#privacy-and-security)

## Install and first run

### Choose a download

| Download | Best for | What it changes |
| --- | --- | --- |
| `PicketsSetup.exe` (recommended) | Most people | Installs Pickets for your Windows account, adds a Start menu shortcut, and offers clearly labeled desktop-shortcut and launch-at-login choices |
| `Pickets.exe` (portable) | USB drives, testing, or a fully manual setup | Runs from its current folder and installs nothing automatically |

### Recommended: guided installer

1. Download `PicketsSetup.exe` from
   [GitHub Releases](https://github.com/Creeptones/Pickets/releases).
2. Review the license and the plain-language **What setup changes** page.
3. Confirm the install folder. The recommended default is
   `%LOCALAPPDATA%\Programs\Pickets`; select **Browse** if you prefer another location.
4. Choose whether to create a desktop shortcut and start Pickets when you sign in. The desktop
   shortcut is selected by default; launch at login is off until you opt in.
5. Select **Install**, then leave **Launch Pickets** selected to open the short first-run guide.

Setup is per-user and does not request administrator access. It copies one self-contained
executable, adds a Start menu shortcut, and applies only the choices shown in the wizard. It does
not install a service or driver, change Windows desktop settings, create an account, access the
network, or add telemetry.

### Portable option

1. Download the latest portable `Pickets.exe` from
   [GitHub Releases](https://github.com/Creeptones/Pickets/releases).
2. Move it out of Downloads into a permanent folder, such as
   `%LOCALAPPDATA%\Programs\Pickets`. Pickets is self-contained, so no .NET installation or
   administrator access is required.
3. Run `Pickets.exe`. The short first-run guide checks the two required desktop settings and can
   create a desktop shortcut or start Pickets when you sign in.

Keep the portable executable in its permanent folder before creating either shortcut. Moving it
later leaves those shortcuts pointing to the old location.

### Updating

To update an installed copy, download and run the newer `PicketsSetup.exe`. Setup recognizes the
existing installation, reuses its location, and replaces the application executable. Your layouts
and appearance settings stay in `%APPDATA%\Pickets` and are not part of the installation folder.
If Setup asks to close Pickets, allow it to do so and relaunch the app when the update finishes.

For a portable copy, quit Pickets and replace the old `Pickets.exe` with the new one in the same
folder.

### Uninstalling

Open **Windows Settings → Apps → Installed apps**, find **Pickets**, and choose **Uninstall**.
Before removing the application, the uninstaller asks Pickets to restore every captured desktop
icon. If recovery cannot finish, uninstall stops with **Retry** and **Cancel** choices and leaves
the app available for recovery. After success, it removes the executable, its Start menu and desktop
shortcuts, and the launch-at-login entry created by Setup.

Saved layouts and diagnostic logs remain in `%APPDATA%\Pickets`, making a later reinstall
recoverable. After uninstalling, you may delete that folder manually if you also want to erase the
saved layouts and logs.

### Verifying a download

Both downloads are currently unsigned. Windows SmartScreen may warn the first time either runs;
choose **More info**, verify that the app is Pickets, and then choose **Run anyway**. Each release
includes SHA-256 checksums and GitHub build-provenance attestations for both executables.

To compare a download with its entry in the release's `SHA256SUMS.txt`:

```powershell
Get-FileHash .\PicketsSetup.exe -Algorithm SHA256
```

## Before first use

Pickets needs control of desktop icon positions. Right-click the desktop, open **View**, and turn
off both:

- **Auto arrange icons**
- **Align icons to grid**

The first-run guide shows the live status of both settings and includes a **Check again** button.
It only proceeds once both settings are confirmed off; if Explorer cannot be checked, it says so.
The guide respects your installer's desktop-shortcut choice and fits smaller displays with scrolling.
If either setting changes later, Pickets reopens the guide at startup. You can reopen it from the tray
menu at any time.

## Using Pickets

| Action | Control |
| --- | --- |
| Add desktop icons | Drag them into a picket |
| Add references from anywhere | Title menu → **Add… → Add files / Add folders**, or **Ctrl+O / Ctrl+Shift+O** |
| Capture several icons | Hold **Shift** and right-drag a rectangle on the desktop |
| Open or roll up a picket | Single-click its title |
| Move a picket or connected stack | Drag its title bar |
| Resize a picket or connected group | Drag an outside edge or corner |
| Return a captured icon to the desktop | Drag it out, or choose **Remove from picket** |
| Remove any reference | Select it and press **Delete**; the original file is never deleted |
| Open an item | Double-click it |
| Focus Pickets | **Ctrl+Alt+D** by default; press again while focused to hide |
| Hide or show every picket | Double-click the desktop or use the tray icon |
| Create, customize, or delete a picket | Click **⋯** or right-click its title |
| Quit safely | Choose **Quit Pickets**; desktop icons are restored until the next launch |
| Release icons permanently | Tray → **Advanced recovery → Release all icons and quit**, or run with `--restore-icons` |
| Hide with the keyboard | **Alt+F4** hides Pickets safely; it does not quit or delete a picket |

Empty pickets offer **Add files…** and **Add folders…**, with a reminder that originals stay put.
The title chevron indicates expansion, **⋯** opens the menu, and the outside corner shows a resize
hint on hover or keyboard focus.

Rolled-up pickets remain visible as a tidy stack of titles. Pickets snap to screen edges and one
another, and connected groups stay flush, share one width, and move or resize as a single outer
frame. Internal edges between connected pickets are not resize handles. The title menu also provides ten
coordinated color systems, transparency levels, optional background blur, section labels, and
large icons. New and existing pickets fit their contents up to two icon rows, with scrolling for
more references. Short groups use one row; labels remain compact. Names use up to two lines, with
the full reference available in the tooltip. Manually resizing the height overrides automatic
sizing for the connected stack; resizing only its width keeps automatic sizing.
Use **Appearance → Height → Automatic (up to two rows)** to return to content sizing, or
**Manual** to keep an explicit height for the stack.

Drag from empty space inside a picket to rectangle-select items. Hold **Shift** to add to the
selection or **Ctrl** to toggle items; **Escape** cancels the rectangle. Drag near the top or
bottom edge to scroll. Right-click a selected item to open, check, resize, move, or remove the
selection. Removal only removes references and restores captured desktop icons; originals stay put.
When multiple items are selected, a compact action strip shows the count and **Move / Size /
Remove / Clear**. Dragging a selected item carries the whole selection with a count badge and
highlights an eligible destination. Duplicate references remain in the source.
Dropping several captured references back onto the desktop restores their original positions.

Press **Ctrl+Z** to undo additions, removals, transfers, icon size changes, reference/picket
reordering, or adding/deleting a picket. The brief Undo notification and title/tray menu provide
the same action. History keeps up to 30 actions during the current session and resets when the
app restarts, Explorer is rebuilt, or the display profile changes. Failed desktop recapture keeps
the reference and the undo action available for retry.

Press **Ctrl+F**, or choose **Find reference…** from the title/tray menu, to search reference names,
paths, labels, and picket names. Choose a result and press **Enter** to reveal its section and focus
the item, including in a collapsed picket or another stack page.

Connected groups form a row or a column. Joining pickets into an irregular arrangement, such as an
L-shape or grid, settles the group into one vertical stack. Membership, order,
sizing, and collapsed state are saved across restarts and display profiles. Use **Stack → Detach picket** to detach one; merely moving the group does not break it apart.

### Optional accordion stacks

Open the title menu and enable **Stack → Open one picket at a time**. The group becomes a vertical stack:
opening a section closes the other bodies and slides their titles into place. Click the open title
to collapse everything. Turn the option off to allow several open sections again. Body content
scrolls when the stack needs to fit a smaller work area. Expanded sections retain one shared size.

Very large stacks use pages to keep every visible title and body within the work area. Use the
header's **‹ / ›** buttons, **Stack → Previous / Next page**, or **Ctrl+PageUp / Ctrl+PageDown**.
**Ctrl+Tab** also reveals the page containing the next picket. Pages do not split membership or
change saved order; the first page is shown after restart. Paged stacks change instantly.

Creating a picket from a title menu or with **Ctrl+N** inserts it directly after that picket in the
same stack. Deleting a section closes the gap automatically and preserves the stack's position.
Click a title or focus it and press **Space** to expand or collapse; the title menu omits this action.
Clicks during expansion redirect the whole stack from its current position, including the chevrons.
Each click toggles once, so a double-click toggles twice. Slight mouse movement remains a click;
dragging begins after the Windows drag tolerance is crossed. Escape cancels a pending title press.

### File references, not file operations

**Add files / Add folders** stores references without moving, copying, hiding, or creating shortcuts.
The same file can appear in several pickets. Dragging a desktop icon in retains the existing desktop
capture behavior; adding it through the dialogs leaves its desktop icon visible.

Right-click a reference for **Open file location**, **Check again**, **Locate…**,
and **Move reference to** another picket. Locate updates the saved reference, not the original file.
Unplugged drives, unavailable shares, and missing files stay saved with a visible status. Reconnect
and choose **Check again**, or locate the replacement if it moved. Availability checks and thumbnails
run off the UI thread with bounded concurrency and timeouts. Opening a reference waits only for
its availability check, never its thumbnail. Short status text is paired with path and recovery
instructions in its tooltip and accessibility help.

Dragging between pickets transfers a reference. Dragging a captured icon out restores its desktop
presentation. Other reference drags remain inside Pickets; they never ask Explorer to move or copy
the source. **Remove from picket** removes only the reference (and restores a captured desktop icon).
Drag previews stay visible across picket boundaries: incoming Explorer drags retain their Windows
preview, and drags started in Pickets show the icon and name. Drop into an expanded body; titles
show a blocked-drop cursor. Escape cancels the drag and removes its preview.

### Keyboard and accessibility

Use **Focus Pickets** in the tray or the configurable global shortcut (default **Ctrl+Alt+D**).
Change it from the tray or title menu → **Settings and help → Change focus shortcut**; **None** disables it.
If another app owns the key combination, Pickets reports the conflict and keeps your previous setting.

| Key | Action |
| --- | --- |
| Ctrl+Tab / Ctrl+Shift+Tab | Next / previous picket |
| Tab / Shift+Tab | Move between controls |
| Enter / Space on a title | Expand / collapse |
| Down on a title | Open and enter its references |
| Arrow keys | Navigate references |
| Space / Ctrl+Space; Shift+Arrow | Select / toggle; extend selection |
| Enter on a reference | Open it |
| Delete | Remove selected references, never the files |
| Shift+F10 | Context menu, including **Move reference to** |
| F2 | Rename this picket |
| Escape | Collapse and focus the title |
| Ctrl+O / Ctrl+Shift+O | Add files / folders |
| Ctrl+N | New picket |
| Ctrl+Z | Undo the last supported organizational action |
| Ctrl+F | Find a reference across the current layout |
| Ctrl+Shift+Up / Down | Reorder focused reference, or section when its title is focused |
| Alt+Arrow | Move the connected group |
| Ctrl+Alt+Arrow | Resize the connected group |
| Ctrl+PageUp / Ctrl+PageDown | Previous / next page of a large stack |
| Alt+F4 | Hide Pickets safely without closing tracked windows |
| F1 | Scrollable keyboard help, including your configured focus shortcut |

Titles expose names and expanded/collapsed state through Windows UI Automation; references expose
selection and availability information. Controls have visible focus indicators, use Windows
high-contrast colors when enabled, and accommodate larger text with wrapping and scrolling.
Welcome, About, and keyboard help also resize and scroll, use high-contrast colors, and keep their
primary action visible on smaller screens. Start stays disabled until desktop readiness is confirmed.
Animations respect the Windows animation preference. All pickets share a render-driven animation
loop; dragging and resizing batch border and handle updates once per rendered frame. Animation
duration stays consistent across refresh rates, and the loop detaches when idle. Actual frame rate
depends on WPF, Windows, and the display workload; no specific monitor FPS is guaranteed.
Diagnostic logs include callback timing after animated stack changes to help investigate stutter.
Automated UI Automation and rendering tests
cover this baseline; a hands-on Narrator and mixed-DPI focus pass remains part of the
[release checklist](RELEASE_CHECKLIST.md).

The system tray remains available when every picket is hidden. From there you can show or hide
pickets, create a new one, enable launch at login, open About/diagnostics, or quit. Normal Quit
restores captured icons while Pickets is not running; the saved layout collects them again on the
next launch. **Exit and keep icons hidden** remains available as an explicitly labeled advanced
option.

## How it works

Pickets does not move captured files to a private folder. For desktop items, it remembers the
original icon coordinates and asks Windows Explorer to position those icons off-screen while their
shortcuts remain visible inside a picket. Removing an item restores its desktop icon.

Layouts are stored per monitor arrangement, so docking, undocking, or changing display setups can
use an appropriate layout without losing the others. Appearance settings follow a named picket
across those display profiles.

### Local data and recovery

Layouts and diagnostic logs stay under `%APPDATA%\Pickets`:

| File | Purpose |
| --- | --- |
| `layout.json` | Current layouts, contents, and appearance settings |
| `layout.json.bak` | Previous layout, used for automatic recovery |
| `debug.log` | Current-session diagnostic log |
| `debug.log.previous` | Previous-session log, retained after a restart or crash |

Layout saves use atomic replacement so an interrupted write does not overwrite the only good copy.
If the primary layout cannot be read, Pickets automatically attempts to load the backup.
Unrecognized layouts and unsupported schema versions also trigger backup recovery. If neither saved
copy is readable, Pickets stops and leaves both files unchanged so recovery can be investigated.

If Pickets cannot start normally, run the portable executable from PowerShell or Command Prompt:

```powershell
.\Pickets.exe --restore-icons
```

Recovery restores captured icons, clears their capture metadata across every display profile, and
exits without starting the normal windows or desktop hooks. If Pickets is already running, the
recovery request is forwarded to that instance.

## Troubleshooting

**Icons remain visible after adding them:** Confirm that both desktop arrangement options listed
under [Before first use](#before-first-use)
are disabled. Explorer may briefly redraw an icon before Pickets hides it again.

**All pickets disappeared:** Use your focus shortcut (**Ctrl+Alt+D** by default), double-click an empty area of the desktop, or
left-click the Pickets tray icon. Launching Pickets again also surfaces the already-running instance.

**Launch at login stopped working after moving the executable:** Open a picket's title menu and
toggle **Settings and help → Start at sign-in** off and back on. This records the new portable executable path.

**Setup says Pickets is still running:** Choose **Quit Pickets** from the tray menu, then continue
Setup. Normal Quit restores the desktop icons while the app is closed; they are collected again
when the updated app starts.

**A layout does not load:** Check `%APPDATA%\Pickets\debug.log`. Pickets will try
`layout.json.bak` automatically when the primary file is invalid or unreadable.

**Pickets cannot start and desktop icons are still hidden:** Run
`Pickets.exe --restore-icons`. This permanently releases every captured icon so later launches do
not hide it again.

**A drive or file is unavailable:** The reference stays saved. Reconnect its location and choose
**Check again**, or use **Locate…**. Pickets never automatically deletes missing
references.

## Privacy and security

Pickets uses low-level mouse hooks only to recognize the desktop lasso and double-click gestures.
It does not record keystrokes or run online services. Windows checks referenced paths and generates
file thumbnails; references on network drives or shares can therefore contact those locations.
Opening an item uses its associated Windows application. Pickets sends no telemetry.
The run-at-login option writes only the current executable path to the current user's standard
Windows `Run` registry key and does not require administrator access.
Setup also records its installation folder and desktop-shortcut choice under the current user's
`Software\Pickets\Setup` registry key so onboarding can respect those choices. Uninstall removes
these setup preferences.
