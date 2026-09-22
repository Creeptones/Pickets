# Frozen UX/safety test build — 2026-09-21

Scope is frozen: no new features before test sign-off. Only reproducible release-blocking fixes,
accessibility regressions, and corrections to documentation/packaging are in scope.
The existing project version remains 1.0.0; identify this local candidate using its source commit
and `release/SHA256SUMS.txt`, not the version alone. No public release or tag is created by rebuilding.

## Completed scope

1. Empty pickets explain drag/add behavior and offer file/folder actions.
2. Titles show expansion, a dedicated menu, and an outside resize hint.
3. Menus use picket/stack/label terminology, one appropriate Locate action, and advanced recovery.
4. Short reference statuses retain actionable details; launching does not await thumbnails.
5. Welcome, About, and keyboard help scroll/resize and support high contrast; Start requires readiness.
6. Normal window close/Alt+F4 safely hides, retaining tracked windows and recovery metadata.
7. Oversized stacks page within the work area while preserving membership and order.

Automated coverage includes the actual WPF controls without showing or attaching them to Explorer,
normal-close cancellation and explicit teardown, empty actions, readiness changes, small-window
dialog scrolling, high-contrast resources, stack-page navigation/order, and thumbnail independence.
Isolated renders are not a substitute for testing the real Windows desktop.

## Test this build

Back up `%APPDATA%\Pickets` first. Use **Quit Pickets** in the tray before launching the rebuilt
portable executable; starting it alongside the older running copy only focuses that older copy.
Use the rebuilt installer for the installation pass. Do not mix binaries during a test run.

- [ ] Empty picket: Add files/Add folders, drag desktop icons, inspect the explanation and originals.
- [ ] Header: expand/collapse, menu by mouse and Shift+F10, rename, move, shared resize and detach.
- [ ] References: available/offline/missing, Check again, Locate file versus folder, slow thumbnail;
      launch remains responsive and originals are unchanged after removing one of several references.
- [ ] Welcome: both settings ready, one enabled, Explorer unavailable, Check again, help popup.
      About/help: small display, 100/125/150/200% scaling, large text, high contrast, keyboard and Narrator.
- [ ] Alt+F4: capture a disposable desktop shortcut, hide, return using tray/global shortcut, then Quit;
      the icon restores. Relaunch and check membership. Repeat around Explorer restart and monitor changes.
- [ ] Large stack: enough pickets to trigger pages; Previous/Next, Ctrl+PageUp/Down and Ctrl+Tab reach all.
      Expand all, accordion, move/resize, join/detach/reorder, restart, and reconnect monitors. No lost
      membership or order, no inaccessible title, no accidental off-page snapping. Pages reset on restart.
- [ ] Installer: default/custom destination, independent shortcut/startup choices, upgrade, uninstall
      with captured icons, and recovery failure followed by Retry/Cancel.

## Exit criteria

The automated suite, Release build, portable publish, installer build, and uninstall recovery fixture
must pass. The [full Windows interaction matrix](RELEASE_CHECKLIST.md) must then be signed off on
real Windows 10/11 machines, including Narrator, mixed DPI, Explorer restart and icon restoration.
Until that happens this is ready for testing, not a declaration that final 1.0 validation is complete.
