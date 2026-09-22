# Changelog

All notable changes to Pickets are documented here. Versions follow
[Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- A transparent per-user installer now defaults to Local AppData, exposes destination, shortcut,
  and launch-at-login choices, requires no elevation, and restores captured icons on uninstall.
- A lighter first-run guide now checks required desktop settings, explains why they matter, offers
  desktop-shortcut and launch-at-login setup, and remains available from the tray menu.

### Changed

- Installation guidance now walks portable users through choosing a permanent location before
  creating shortcuts or enabling launch at login.

### Fixed

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
