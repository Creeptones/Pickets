# Pickets

A lightweight desktop icon organizer for Windows 11. Drop your desktop shortcuts into tidy,
movable, see-through boxes called pickets that sit on the wallpaper behind your icons.

Not affiliated with Stardock. "Fences" is a trademark of Stardock Corporation. Pickets is an
independent, open source project.

## Features

- Group desktop icons into movable, resizable pickets.
- Drag any file or shortcut onto a picket to tuck its icon away.
- Shift + right-drag a box on the desktop to lasso icons into a new picket.
- 18 color themes, adjustable transparency, and optional blur.
- Point a picket at a folder to mirror its contents live.
- Add section labels and switch icons to a large 2x2 size.
- Pickets snap to screen edges and to each other, and snapped groups move together.
- Separate layouts for each monitor setup.
- System tray icon to show or hide pickets, add a picket, or quit.
- Single instance, with crash and Explorer restart recovery.
- Runs fully offline. No network, no telemetry, no accounts.

## Requirements

- Windows 10 (1809 or newer) or Windows 11.
- .NET 9 SDK to build from source.
- Turn off "Auto arrange icons" and "Align icons to grid" (right click desktop, then View).
  Pickets warns you if either is on.

## Build and run

```powershell
dotnet build -c Release
dotnet run -c Release
```

Self contained build (no .NET install needed to run it):

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

The build is unsigned, so SmartScreen may warn on first run. Pick More info, then Run anyway.

## Basic use

- Add icons: drag files onto a picket, or Shift + right-drag a box on the desktop.
- Move or resize: drag the title bar or the edges.
- Hide or show all pickets: Ctrl + Alt + D, double click the desktop, or use the tray icon.
- Restore icons: remove an item from a picket, or use the tray to restore all and quit.

Your layout is saved to %APPDATA%\Pickets\layout.json.

## Notes

- The global mouse hook is only used to detect the lasso and double click gestures. It does not
  log keystrokes or send anything anywhere. The full source is here to verify.
- Moving a desktop icon can make hidden icons flash back for about a second before a watchdog
  re-hides them.

## License

MIT. See [LICENSE](LICENSE).
