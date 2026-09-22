# Pickets

[![Build](https://github.com/Creeptones/Pickets/actions/workflows/build.yml/badge.svg)](https://github.com/Creeptones/Pickets/actions/workflows/build.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![Windows 10/11](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D4.svg)](#requirements)

A lightweight desktop organizer for Windows. Pickets keeps references to your files and folders
inside movable, translucent groups at the wallpaper layer. Your files stay in their original locations.

- No accounts, ads, telemetry, or background web services.
- Choose a guided per-user installer or a standalone portable build.
- Your files stay where they are; Pickets manages their desktop presentation.

## Test-build feature freeze

The release candidate is ready for hands-on validation after the recovery fixes. No further features
are planned before test sign-off; only release-blocking fixes. Start with the
[short, step-by-step test list](docs/TEST_RELEASE.md). The [full release checklist](docs/RELEASE_CHECKLIST.md)
tracks final sign-off. A local rebuild is not a new public release.

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
| Add references from anywhere | Title menu → **Add files / Add folders**, or **Ctrl+O / Ctrl+Shift+O** |
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
another, and connected groups stay flush, share one size, and move or resize as a single outer
frame. Internal edges between connected pickets are not resize handles. The title menu also provides eight
coordinated color systems, transparency levels, optional background blur, section labels, and
large icons.

Connected groups form a row or a column. Joining pickets into an irregular arrangement, such as an
L-shape or grid, settles the group into one consistently sized vertical stack. Membership, order,
shared dimensions, and collapsed state are saved across restarts and display profiles. Use **Stack → Detach picket** to detach one; merely moving the group does not break it apart.

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
[release checklist](docs/RELEASE_CHECKLIST.md).

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

## Features

- Movable, resizable, roll-up desktop groups.
- Drag-in and drag-out icon management.
- Shift + right-drag desktop lasso.
- Eight contrast-aware themes, transparency, and Windows background blur.
- Section labels and optional large icons.
- Magnetic screen-edge and picket-to-picket snapping.
- Persistent connected groups, shared resizing, opt-in accordion stacks, and one-click unlinking.
- Non-destructive file/folder references with availability checks and relinking.
- Keyboard navigation, configurable focus shortcut, UI Automation, and high-contrast support.
- Separate layouts for different monitor arrangements.
- Explorer-restart recovery and a watchdog for icons Windows moves back on-screen.
- Emergency icon restoration independent of normal UI startup.
- About window with version information, the data folder, and sanitized diagnostic copying.
- Single-instance behavior and an always-accessible system tray.
- Guided per-user installer and a standalone portable download.
- Works offline for local files, with no telemetry or user account.

## Requirements

- Windows 10 version 1809 or newer, or Windows 11.
- Windows x64 for the downloadable installer and portable build.
- **Auto arrange icons** and **Align icons to grid** disabled.

The .NET 10 SDK is needed only when building from source.

## Build from source

Clone the repository, then run:

```powershell
dotnet build Pickets.csproj --configuration Release
dotnet run --project Pickets.csproj --configuration Release
```

To create the same single-executable package used for releases:

```powershell
dotnet publish Pickets.csproj --configuration Release --property:PublishProfile=win-x64
```

The result is written to:

```text
bin\Release\net10.0-windows\win-x64\publish\Pickets.exe
```

To build the installer locally, publish into its staging folder, install
[Inno Setup 6](https://jrsoftware.org/isinfo.php), and run the included build script:

```powershell
dotnet publish Pickets.csproj --configuration Release --property:PublishProfile=win-x64 --output release\portable
.\installer\build-installer.ps1 -Version 1.0.0
```

The installer is written to `release\PicketsSetup.exe`.

Release builds enable the recommended .NET analyzers and treat warnings as errors. Pushes and pull
requests are also compiled and publish-checked on Windows through GitHub Actions.

Pushing a version tag that matches `Pickets.csproj` (for example, `v1.0.0`) runs the release
workflow. It tests and publishes the app, builds the per-user installer, creates
`SHA256SUMS.txt`, records GitHub build provenance for both executables, and attaches the installer,
portable executable, and checksums to a draft GitHub Release. Review the notes, complete the live
test sign-off, and verify the assets and provenance before publishing the draft.

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

## Contributing

Bug reports and focused pull requests are welcome through
[GitHub Issues](https://github.com/Creeptones/Pickets/issues). When reporting a crash or desktop
interaction problem, include reproduction steps and the relevant diagnostic log with any private
paths removed. **About Pickets → Copy diagnostics** produces a summary with the Windows username
and user-profile path redacted automatically.

See the [changelog](CHANGELOG.md), [release checklist](docs/RELEASE_CHECKLIST.md), and
[security policy](SECURITY.md) for project-maintenance details.

## License and trademark

Pickets is available under the [MIT License](LICENSE).

Pickets is an independent open-source project and is not affiliated with Stardock. “Fences” is a
trademark of Stardock Corporation.
