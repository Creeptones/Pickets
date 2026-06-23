# Pickets

A lightweight desktop-icon organizer for Windows 11. Sort your desktop shortcuts into movable,
resizable, translucent **fences** that float on the wallpaper behind your icons — keeping the
desktop tidy without a heavyweight shell extension.

> **Not affiliated with, or endorsed by, Stardock Corporation.** *Fences* is a trademark of
> Stardock Corporation. Pickets is an independent, open-source project.

## Features

- **Fences** — translucent containers you can drag, resize, roll up, recolor (18 palettes),
  and set per-fence transparency or acrylic blur on.
- **Capture desktop icons** — drag a file/shortcut onto a fence (or **Shift + right-drag** a lasso
  on the desktop) to move its icon into the fence; the real desktop icon is hidden, not deleted.
- **Folder portals** — point a fence at a folder and it live-mirrors that folder's contents.
- **Section labels**, **large (2×2) icons**, inline rename.
- **Snap & link** — fences magnetically snap to screen edges and to each other; snapped clusters
  move together.
- **Per-display profiles** — separate layouts per monitor arrangement (handy for docking/remote).
- **System tray icon** — show/hide all fences, create a fence, toggle run-at-login, or quit.
- **Single-instance** with crash/Explorer-restart resilience.
- **Runs entirely offline.** No network access, no telemetry, no accounts.

## Requirements

- Windows 10 (1809+) or Windows 11
- [.NET 9 SDK](https://dotnet.microsoft.com/download) to build from source
- Desktop **must** have *Auto arrange icons* and *Align icons to grid* turned **off**
  (right-click desktop → View). Pickets warns you on launch if either is on — it can only hide
  icons cleanly when both are off.

## Build & run

```powershell
dotnet build -c Release
dotnet run -c Release
```

To produce a self-contained executable (no .NET install needed to run it):

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

The build is unsigned, so Windows SmartScreen may show an "Unknown publisher" prompt the first
time you run a downloaded binary — choose **More info → Run anyway**.

## Usage

- **Add icons:** drag files/shortcuts onto a fence, or **Shift + right-drag** a box on the desktop.
- **Move/resize:** drag the title bar; drag the right/bottom/corner edges to resize.
- **Hide/show all fences:** `Ctrl + Alt + D`, double-click the bare desktop, or use the tray icon.
- **Restore icons:** right-click a fence title → *Restore all icons and quit*, or remove individual
  items (*Remove from fence*) to send their icons back to the desktop.
- **Quit:** tray icon → *Quit*, or a fence title → *Quit*.

Layout is saved to `%APPDATA%\Pickets\layout.json`.

## How it works (and a heads-up for antivirus)

Pickets parents its fence windows to Explorer's `WorkerW` layer (the space between the wallpaper and
the desktop icons) and hides captured icons by repositioning them off-screen via `IFolderView`.
It installs a global low-level **mouse hook** purely to detect the lasso gesture and the
double-click-desktop shortcut — it does **not** log keystrokes or send anything anywhere; everything
is local. Some antivirus heuristics flag low-level hooks generically; the full source is here so you
can verify exactly what it does.

## Known limitations

- Moving a desktop icon makes Windows briefly clamp hidden icons back on-screen; a 1-second watchdog
  re-hides them, so you may see a short flicker.
- The off-screen hiding technique requires *Auto arrange* / *Align to grid* to stay off.
- Folder-portal sync does not yet recover from a `FileSystemWatcher` buffer overflow (very rapid bulk
  changes); restart the app or toggle the portal to resync.

## License

[MIT](LICENSE) © 2026 Creeptone
