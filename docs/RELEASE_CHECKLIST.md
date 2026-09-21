# Pickets release checklist

## Version and source

- [ ] Update `Version`, `AssemblyVersion`, and `FileVersion` in `Pickets.csproj`.
- [ ] Move notable changes from **Unreleased** into the dated changelog entry.
- [ ] Confirm the README describes the shipped controls and requirements.
- [ ] Confirm the working tree is clean and CI passes on `main`.

## Automated verification

- [ ] `dotnet test Pickets.Tests/Pickets.Tests.csproj --configuration Release`
- [ ] `dotnet build Pickets.csproj --configuration Release`
- [ ] Publish with the `win-x64` profile and confirm the folder contains only `Pickets.exe`.
- [ ] Launch the published executable and verify the version in **About Pickets**.

## Windows interaction matrix

- [ ] Clean first launch on Windows 11 as a standard user.
- [ ] Verify the first-run readiness checks, desktop-shortcut option, launch-at-login option, and
      tray-menu quick start guide.
- [ ] Windows 10 version 1809 or newer smoke test.
- [ ] 100%, 125%, 150%, and 200% display scaling.
- [ ] Mixed-DPI multi-monitor arrangement.
- [ ] Dock, undock, and reconnect a monitor.
- [ ] Restart Windows Explorer while Pickets is running.
- [ ] Force-terminate Pickets, relaunch it, and inspect the previous-session log.
- [ ] Drag desktop icons in, between pickets, and back out.
- [ ] Drag a non-desktop file into and back out of a picket.
- [ ] Exercise lasso creation, roll-up stacks, group movement, resizing, and unlinking.
- [ ] Verify missing/renamed items and **Clean up missing items**.
- [ ] Verify former folder portals still load as ordinary folder shortcuts.
- [ ] Verify normal Quit restores icons and a later launch collects them again.
- [ ] Verify **Exit and keep icons hidden** behaves as labeled.
- [ ] Verify `Pickets.exe --restore-icons` both with and without Pickets already running.
- [ ] Verify copied diagnostics redact the Windows username and user-profile path.

## Release

- [ ] If signing is configured, verify the Authenticode signature and timestamp.
- [ ] Create and push an annotated tag matching the project version, for example `v1.0.0`.
- [ ] Confirm the release workflow attaches `Pickets.exe` and `SHA256SUMS.txt`.
- [ ] Verify the GitHub build-provenance attestation.
- [ ] Download the public asset on a clean machine and confirm its SHA-256 checksum.
- [ ] Review generated release notes before announcing the release.
