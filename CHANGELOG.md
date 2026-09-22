# Changelog

All notable changes to Pickets are documented here. Versions follow
[Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- Actionable empty pickets, expansion chevrons, explicit title menus, and outside-corner resize hints.
- Work-area-aware paging for oversized stacks, with header controls and Ctrl+PageUp/PageDown.
- Scrollable, resizable About and keyboard-help windows sharing onboarding's high-contrast styling.
- Persistent connected stacks with saved membership/order and optional one-section-open accordion mode.
- Add-file and add-folder reference dialogs; open location, availability checks, relinking, and
  keyboard-accessible transfers between pickets without moving or copying the original files.
- Keyboard navigation and selection, stack move/resize/reorder commands, configurable focus shortcut,
  UI Automation names/selection/expansion, focus indicators, and Windows high-contrast colors.

- A transparent per-user installer now defaults to Local AppData, exposes destination, shortcut,
  and launch-at-login choices, requires no elevation, and restores captured icons on uninstall.
- A lighter first-run guide now checks required desktop settings, explains why they matter, offers
  desktop-shortcut and launch-at-login setup, and remains available from the tray menu.

### Changed

- Simplified reference and stack terminology; advanced recovery actions are separated from everyday controls.
- Desktop readiness now controls the Start button, with an explanation when readiness is unknown.
- Features are frozen for the local pre-release test build; see `docs/TEST_BUILD.md`.
- Installation guidance now walks portable users through choosing a permanent location before
  creating shortcuts or enabling launch at login.

### Fixed

- Alt+F4 and ordinary picket-window close requests safely hide Pickets without losing tracked windows
  or desktop-icon recovery metadata; application-managed teardown remains explicit.
- Launch availability checks no longer await thumbnail generation. Compact unavailable statuses retain
  detailed path and recovery instructions in tooltips and accessibility help.
- Oversized stacks remain reachable without breaking membership/order, including across page changes.
- Unavailable and missing references remain saved instead of being removed by cleanup.
- Reference probes and thumbnails run off the UI thread with bounded concurrency and timeouts.
- Reference drags no longer advertise filesystem moves to Explorer; failed desktop-icon restoration
  keeps the reference, and quitting checks captured icons even while availability is being refreshed.

- Uninstall now requires successful icon recovery and a saved recovery state before removing files;
  failed recovery offers Retry/Cancel, and silent uninstall stops with an error.
- Onboarding preserves the installer's desktop-shortcut opt-out, reports unavailable desktop checks,
  and requires confirmed readiness before hiding icons.
- The first-run guide now resizes and scrolls to fit smaller screens and higher display scaling.
- Irregular connected layouts settle into a flush column and resize as one group, including at
  fractional display scaling.
- Connected pickets now keep a single shared size, resize together from the group's outside
  boundary, and automatically close small gaps left by older layouts.

## [1.0.0] - 2026-09-21

### Added

- Movable, resizable, roll-up pickets for desktop icons.
- Drag-in, drag-out, and Shift + right-drag lasso interactions.
- Connected picket movement, resizing, snapping, and unlinking.
- Eight coordinated color systems, transparency controls, and optional blur.
- Per-monitor layout profiles and Explorer-restart recovery.
- Atomic layout persistence with automatic backup recovery.
- Emergency `--restore-icons` recovery mode.
- About window with sanitized diagnostic copying.
- Self-contained Windows x64 release packaging and build provenance.

### Changed

- Normal Quit restores desktop icon visibility while retaining the picket layout for the next run.
- Folder portals migrate to ordinary folder shortcuts for a simpler interaction model.

[Unreleased]: https://github.com/Creeptones/Pickets/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/Creeptones/Pickets/releases/tag/v1.0.0
