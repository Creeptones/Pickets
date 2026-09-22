# Changelog

All notable changes to Pickets are documented here. Versions follow
[Semantic Versioning](https://semver.org/).

## [Unreleased]

- Rectangle selection inside pickets with Shift-add, Ctrl-toggle, Escape cancellation, and
  edge scrolling. Context menus apply supported actions to the selection, including reference
  transfers, icon sizing, availability checks, opening, and removal.

## [1.0.0] - release candidate (publication pending)

The first release is awaiting the live Windows checks in `docs/TEST_RELEASE.md`.
Set the publication date when those checks pass and the release is published.

### Added

- Movable, resizable, roll-up pickets for desktop icons, with drag-in, drag-out, and lasso capture.
- Eight coordinated color systems, transparency controls, and optional blur.
- Per-monitor layout profiles and Explorer-restart recovery.
- Atomic layout persistence, backup recovery, and emergency `--restore-icons` recovery mode.
- About window with sanitized diagnostic copying.
- Self-contained Windows x64 release packaging, checksums, and build provenance.
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

- New and existing pickets automatically fit up to two measured icon rows and scroll additional
  references. Connected sections retain individual content heights and shared widths. Compact
  tiles remove empty status space and limit names to two lines; explicit height resizing remains
  available as a saved override.
- Explorer drag images remain visible over picket bodies and titles. Drags started inside
  Pickets carry a DPI-scaled icon/name preview, including between pickets and back to the desktop.
  Preview cleanup covers leaving, dropping, cancellation, hiding, and window closure.
- Title clicks and keyboard expansion requests redirect an in-progress stack animation from
  its current geometry; rapid clicks are no longer discarded. Each title click toggles once,
  including both clicks of a double-click.
- Title dragging starts after Windows' drag tolerance is crossed, scaled for the current display.
  Small pointer jitter remains a click; dragging back to the starting point does not collapse a picket.
- Every picket's expand/collapse and accordion motion shares WPF's rendering cadence instead of
  a fixed 16 ms timer. Stack borders and resize handles refresh once per frame across dragging,
  resizing, and layout changes; corner resizing applies one layout pass. Rendering callbacks
  detach while idle, and reduced-motion behavior is preserved.
- One consistently sized vector chevron rotates with the stack's expand/collapse animation,
  respecting Windows reduced-motion preferences.
- Simplified reference and stack terminology; advanced recovery actions are separated from everyday controls.
- Normal Quit restores desktop icons while retaining ownership for the next launch.
- Former folder portals migrate to ordinary folder shortcuts.
- Desktop readiness now controls the Start button, with an explanation when readiness is unknown.
- Features are frozen for the local pre-release test build; see `docs/TEST_BUILD.md`.
- Installation guidance now walks portable users through choosing a permanent location before
  creating shortcuts or enabling launch at login.

### Fixed

- Adding a picket from a title menu or Ctrl+N joins the invoking stack directly; deleting a
  section closes the gap and keeps the stack anchored. Stack reflow finishes pending expansion
  animations before changing membership. The redundant expand/collapse title-menu entry is removed.
- Display-profile changes restore captures absent from the incoming profile before replacing their
  windows; failed saves or restores retain the current pickets for recovery.
- Normal Quit includes captured icons from every saved display profile.
- Unrecognized, structurally invalid, and unsupported layout schemas fall back to a valid backup.
  If neither saved copy is readable, startup and emergency recovery stop without overwriting them.
- Unavailable file locations are not treated as deleted when deciding whether recovery succeeded.
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

[Unreleased]: https://github.com/Creeptones/Pickets/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/Creeptones/Pickets/releases/tag/v1.0.0
