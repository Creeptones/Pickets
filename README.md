# Pickets

[![Build](https://github.com/Creeptones/Pickets/actions/workflows/build.yml/badge.svg)](https://github.com/Creeptones/Pickets/actions/workflows/build.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![Windows 10/11](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D4.svg)](#requirements)

A lightweight, offline desktop-icon organizer for Windows. Pickets collects shortcuts and files
inside movable, translucent groups that stay at the wallpaper layer of your desktop.

- No accounts, network access, ads, or telemetry.
- No installer required.
- Your files stay where they are; Pickets manages their desktop presentation.

## Download

Download the latest portable `Pickets.exe` from
[GitHub Releases](https://github.com/Creeptones/Pickets/releases). It is a self-contained Windows
x64 executable, so the .NET runtime does not need to be installed separately.

Pickets is currently unsigned. Windows SmartScreen may warn the first time it runs; choose
**More info**, verify that the app is Pickets, and then choose **Run anyway**.

## Before first use

Pickets needs control of desktop icon positions. Right-click the desktop, open **View**, and turn
off both:

- **Auto arrange icons**
- **Align icons to grid**

The app checks these settings at startup and warns when either one is enabled.

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
another, and connected groups move or resize as a single outer frame. Internal edges between
connected pickets are not resize handles. The title menu also provides eight
coordinated color systems, transparency levels, optional background blur, section labels, and
large icons.

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

All state stays under `%APPDATA%\Pickets`:

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
- Fully offline operation with no telemetry or user account.

## Requirements

- Windows 10 version 1809 or newer, or Windows 11.
- Windows x64 for the downloadable portable build.
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

Release builds enable the recommended .NET analyzers and treat warnings as errors. Pushes and pull
requests are also compiled and publish-checked on Windows through GitHub Actions.

Pushing a version tag that matches `Pickets.csproj` (for example, `v1.0.0`) runs the release
workflow. It tests and publishes the app, creates `SHA256SUMS.txt`, records GitHub build provenance,
and attaches both files to a GitHub Release.

## Troubleshooting

**Icons remain visible after adding them:** Confirm that both desktop arrangement options listed
under [Before first use](#before-first-use)
are disabled. Explorer may briefly redraw an icon before Pickets hides it again.

**All pickets disappeared:** Press **Ctrl+Alt+D**, double-click an empty area of the desktop, or
left-click the Pickets tray icon. Launching Pickets again also surfaces the already-running instance.

**Launch at login stopped working after moving the executable:** Open a picket's title menu and
toggle **Launch at login** off and back on. This records the new portable executable path.

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
