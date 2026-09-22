# Pickets

[![Build](https://github.com/Creeptones/Pickets/actions/workflows/build.yml/badge.svg)](https://github.com/Creeptones/Pickets/actions/workflows/build.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![Windows 10/11](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D4.svg)](#requirements)

A lightweight, offline desktop-icon organizer for Windows. Pickets collects shortcuts and files
inside movable, translucent groups that stay at the wallpaper layer of your desktop.

- No accounts, network access, ads, or telemetry.
- Choose a guided per-user installer or a standalone portable build.
- Your files stay where they are; Pickets manages their desktop presentation.

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
| Capture several icons | Hold **Shift** and right-drag a rectangle on the desktop |
| Open or roll up a picket | Single-click its title |
| Move a picket or connected stack | Drag its title bar |
| Resize a picket or connected group | Drag an outside edge or corner |
| Return an icon to the desktop | Drag it out, or choose **Remove from picket** |
| Open an item | Double-click it |
| Hide or show every picket | Press **Ctrl+Alt+D**, double-click the desktop, or use the tray icon |
| Create, customize, or delete a picket | Right-click its title |
| Quit safely | Choose **Quit Pickets**; desktop icons are restored until the next launch |
| Release icons permanently | Choose **Release all icons and quit**, or run with `--restore-icons` |

Rolled-up pickets remain visible as a tidy stack of titles. Pickets snap to screen edges and one
another, and connected groups stay flush, share one size, and move or resize as a single outer
frame. Internal edges between connected pickets are not resize handles. The title menu also provides eight
coordinated color systems, transparency levels, optional background blur, section labels, and
large icons.

Connected groups form a row or a column. Joining pickets into an irregular arrangement, such as an
L-shape or grid, settles the group into one consistently sized vertical stack.

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
- Group movement, group resizing, and one-click unlinking.
- Separate layouts for different monitor arrangements.
- Explorer-restart recovery and a watchdog for icons Windows moves back on-screen.
- Emergency icon restoration independent of normal UI startup.
- About window with version information, the data folder, and sanitized diagnostic copying.
- Single-instance behavior and an always-accessible system tray.
- Guided per-user installer and a standalone portable download.
- Fully offline operation with no telemetry or user account.

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
portable executable, and checksums to a GitHub Release.

## Troubleshooting

**Icons remain visible after adding them:** Confirm that both desktop arrangement options listed
under [Before first use](#before-first-use)
are disabled. Explorer may briefly redraw an icon before Pickets hides it again.

**All pickets disappeared:** Press **Ctrl+Alt+D**, double-click an empty area of the desktop, or
left-click the Pickets tray icon. Launching Pickets again also surfaces the already-running instance.

**Launch at login stopped working after moving the executable:** Open a picket's title menu and
toggle **Launch at login** off and back on. This records the new portable executable path.

**Setup says Pickets is still running:** Choose **Quit Pickets** from the tray menu, then continue
Setup. Normal Quit restores the desktop icons while the app is closed; they are collected again
when the updated app starts.

**A layout does not load:** Check `%APPDATA%\Pickets\debug.log`. Pickets will try
`layout.json.bak` automatically when the primary file is invalid or unreadable.

**Pickets cannot start and desktop icons are still hidden:** Run
`Pickets.exe --restore-icons`. This permanently releases every captured icon so later launches do
not hide it again.

## Privacy and security

Pickets uses low-level mouse hooks only to recognize the desktop lasso and double-click gestures.
It does not record keystrokes, inspect document contents, connect to the internet, or transmit any
data. The run-at-login option writes only the current executable path to the current user's standard
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
