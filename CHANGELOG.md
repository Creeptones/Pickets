# Changelog

All notable changes to Pickets are documented here. Versions follow
[Semantic Versioning](https://semver.org/).

## [Unreleased]

### Fixed

- Connected pickets now resize from the group's outside boundary, preventing internal-edge drags
  from overlapping stacked panels.

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
