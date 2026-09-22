# Pickets release checklist

Start with the [short, ordered hands-on test list](TEST_RELEASE.md). Record PASS, FAIL, or
NOT TESTED; an unavailable machine or display configuration is not a pass.

## Version and source

- [ ] Update `Version`, `AssemblyVersion`, and `FileVersion` in `Pickets.csproj`.
- [ ] Move notable changes from **Unreleased** into the dated changelog entry.
- [ ] Confirm the README describes the shipped controls and requirements.
- [ ] Confirm the working tree is clean and CI passes on `main`.
- [ ] Confirm private vulnerability reporting is enabled and the security policy links to it.

## Automated verification

- [ ] `dotnet test Pickets.Tests/Pickets.Tests.csproj --configuration Release`
- [ ] `dotnet build Pickets.csproj --configuration Release`
- [ ] Publish with the `win-x64` profile and confirm the folder contains only `Pickets.exe`.
- [ ] Build `PicketsSetup.exe`; verify its version metadata and both SHA-256 entries.
- [ ] Run `installer/tests/Test-UninstallRecovery.ps1 -Compiler <path-to-ISCC.exe>`; verify all
      recovery failure codes preserve the isolated fixture and success removes it.
- [ ] Launch the published executable and verify the version in **About Pickets**.

## Windows interaction matrix

- [ ] Clean first launch on Windows 11 as a standard user.
- [ ] Install to the default Local AppData destination and to a custom per-user destination.
- [ ] Verify desktop-shortcut and launch-at-login choices independently, then upgrade in place.
- [ ] Uninstall with captured icons and confirm they are restored while `%APPDATA%\Pickets` remains.
- [ ] Verify the first-run readiness checks, desktop-shortcut option, launch-at-login option, and
      tray-menu quick start guide.
- [ ] Decline the installer shortcut, defer launch until later, and confirm onboarding keeps it off.
- [ ] Verify an unavailable Explorer check says "unable to check" and prevents first-run completion.
- [ ] Check the guide in a small window: content scrolls and the Start button remains accessible.
- [ ] Windows 10 version 1809 or newer smoke test.
- [ ] 100%, 125%, 150%, and 200% display scaling.
- [ ] Mixed-DPI multi-monitor arrangement.
- [ ] Dock, undock, and reconnect a monitor.
- [ ] Capture in profile A, remove from B, return to A and then B; the icon restores in B and after Quit.
- [ ] Restart Windows Explorer while Pickets is running.
- [ ] Force-terminate Pickets, relaunch it, and inspect the previous-session log.
- [ ] Drag desktop icons in, between pickets, and back out.
- [ ] Add a non-desktop file/folder by drag and by dialog; verify its original path and contents do not change.
- [ ] Add one file to two pickets; remove either reference and verify the other and original remain.
- [ ] Exercise lasso creation, roll-up stacks, group movement, resizing, and unlinking.
- [ ] Join an L-shape/grid and verify it becomes one flush column that moves and resizes together.
- [ ] Disconnect a removable/network drive; references remain, the UI responds, and **Check again**
      recovers them after reconnection. Test **Locate…** for both renamed files and folders.
- [ ] Enable accordion mode; switch sections, collapse all, disable it, and restart.
- [ ] Resize from the bottom of a collapsed final section; all expanded bodies keep the shared size.
- [ ] Save a stack, reconnect monitors, and confirm membership/order and flush seams persist.
- [ ] Complete a mouse-free pass: focus shortcut, Ctrl+Tab, selection/open/remove, context menu transfer,
      rename, new picket, add files/folders, reorder, group move/resize, and Escape.
- [ ] Change the global focus shortcut, test a conflict, disable it, and restart to confirm persistence.
- [ ] Use Narrator to read title expansion, item names/status, selection, and popup controls.
- [ ] Verify Windows high contrast, larger text, visible focus, and reduced-motion preferences live.
- [ ] Verify former folder portals still load as ordinary folder shortcuts.
- [ ] Verify normal Quit restores icons and a later launch collects them again.
- [ ] Verify **Exit and keep icons hidden** behaves as labeled.
- [ ] Verify `Pickets.exe --restore-icons` both with and without Pickets already running.
- [ ] Verify copied diagnostics redact the Windows username and user-profile path.

## Release

### Frozen UX/safety candidate (2026-09-21)

See [the focused test plan](TEST_BUILD.md) for the five polish items and two edge-case fixes.
Automated checks and isolated WPF renders cover the candidate; the live matrix above remains
required. In particular, verify Alt+F4/return/Quit recovery and oversized-stack paging on real monitors.
The existing 1.0.0 version is retained; use the source commit and generated hashes to identify this build.

### Interaction implementation verification (2026-09-21)

Automated tests cover stack geometry at 100–200% scaling, accordion state transitions, saved
membership/order and profile seeding, unavailable references, shortcut parsing, and the real WPF
controls' UI Automation names, selection, and expansion. Isolated standard/high-contrast/large-text
renders were inspected. These do not replace the live Windows interaction matrix above.
The desktop automation helper was unavailable during this pass (native-pipe connection failure),
so live global focus, Narrator, and mixed-DPI Explorer interaction are explicitly unverified.
Local verification: 59 tests passed; Release builds had zero warnings; the single-file portable
package and installer built successfully; all four installer recovery failures blocked removal
and the success case allowed uninstall.

### Publication

- [ ] If signing is configured, verify the Authenticode signature and timestamp.
- [ ] Create and push an annotated tag matching the project version, for example `v1.0.0`.
- [ ] Confirm the release workflow attaches `PicketsSetup.exe`, `Pickets.exe`, and `SHA256SUMS.txt`.
- [ ] Verify the GitHub build-provenance attestation.
- [ ] Review the draft release and record live test sign-off before publishing it.
- [ ] Download the public asset on a clean machine and confirm its SHA-256 checksum.
- [ ] Review generated release notes before announcing the release.

### Release-audit fixes (2026-09-22)

Profile transitions restore inactive captures before closing their windows; failed saves or recovery
keep the current windows available. Normal Quit collects across all saved profiles. Layout loading
rejects unknown schemas and invalid ownership structures, uses a valid backup, and stops without
overwriting data if both copies are unreadable. Regression tests exercise these cases, including
profile round-trips and failure/retry. Private vulnerability reporting was enabled and verified.
Release tags now create a draft for final review. The live Windows matrix still requires sign-off.
Local verification: 98 tests passed with no skipped tests; Release compilation passed with warnings
treated as errors. Portable publishing, installer compilation, and all five uninstall recovery-fixture
cases passed. The final candidate's source ID and hashes are recorded in `release/TEST_BUILD.txt`
and `release/SHA256SUMS.txt`; these local checks do not replace CI on the eventual `main` commit.

### App-wide rendering follow-up (2026-09-22)

All stack expansion and accordion motion now uses one WPF rendering subscription, with deferred
border/handle refreshes shared by all pickets. Dragging and resizing no longer refresh every
intermediate geometry change; corner resizing performs one reflow. The loop detaches when idle.
109 tests passed, including simulated 60/120/144/165/240 Hz callback schedules, duplicate-frame
filtering, batched updates, cleanup, and WPF stack resizing. These are scheduling/behavior checks,
not measurements of displayed FPS. The live motion test and diagnostic callback timings should
be checked on the target displays before release.

### Responsive title interactions (2026-09-22)

Title clicks and keyboard expansion requests can redirect a running stack animation from the
current bounds and chevron angles. Cancelled callbacks cannot overwrite the newer transition;
accordion targets, flush seams, and the original stack anchor are retained. Title presses use
Windows drag tolerance in screen coordinates adjusted for display scaling, with capture-loss and
Escape cancellation. Both clicks of a double-click now count as separate toggles.
115 tests passed, including deterministic mid-animation reversal/switching on real unshown WPF
windows and gesture cases at 100%, 125%, 150%, and 200% scaling. Live mouse/capture, keyboard,
and reduced-motion checks remain in the short test list.
