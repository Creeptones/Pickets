# Pickets: release test list

**Do one box at a time. Start with Round 1. Take a break between rounds.**
Use a disposable desktop shortcut, not an important original file. If something fails, stop that
test and record what happened. Leave unavailable hardware marked **NOT TESTED**.

Build: use `release/Pickets.exe` and `release/PicketsSetup.exe` from this checkout.
`release/TEST_BUILD.txt` identifies the candidate; `release/SHA256SUMS.txt` identifies the exact files.

**Before you start — about 2 minutes**

- [ ] Choose **Quit Pickets** from the tray. Starting a new copy while the old one is running just
      brings the old copy forward.
- [ ] Copy `%APPDATA%\Pickets` somewhere safe if it exists. Keep this backup until release testing ends.
- [ ] Create a disposable shortcut on your desktop named **Pickets test**.
- [ ] Launch the candidate. Keep **Auto arrange icons** and **Align icons to grid** off.

**Round 1 — does everyday use feel safe? About 5 minutes**

- [ ] **Capture and return.** Drag **Pickets test** into a picket, then remove it.
      **Pass:** its desktop icon returns; the shortcut still opens its original target.
- [ ] **Visible drags.** Slowly drag **Pickets test** from the desktop across a title and into an
      expanded body; keep holding and move back out, then press Escape. Repeat by dragging a
      captured test icon between pickets and back to the desktop. Try an ordinary non-desktop
      reference too, cancelling with Escape. If available, repeat across displays with different scaling.
      **Pass:** the preview follows the pointer across boundaries and disappears on drop/Escape;
      titles indicate a blocked drop; reference transfers and desktop restoration still work;
      ordinary originals are never moved/copied and cancellation leaves membership intact.
- [ ] **Hide and return.** Capture it again. Press **Alt+F4**, then **Ctrl+Alt+D**.
      **Pass:** Pickets hides, returns, and still contains the shortcut.
- [ ] **Quit and relaunch.** Choose **Quit Pickets**, then launch the candidate again.
      **Pass:** the desktop icon returns while Pickets is closed, then is collected again on launch.
- [ ] **References stay references.** Add the same disposable file through **Add files** in two
      pickets. Remove one reference.
      **Pass:** the original file and the other reference still exist.
- [ ] **Motion across the app.** Open/close several different pickets, switch accordion sections,
      drag a connected stack, and resize its outer corner.
      **Pass:** sections and arrows move together, seams stay flush, and the final size/order saves.
      Try the same with Windows animations disabled: expansion should finish immediately.
- [ ] **Quick clicks.** Click the same title again before it finishes opening, then quickly switch
      between accordion sections. Try Space on a focused title too.
      **Pass:** the latest request wins, movement redirects without jumping, and seams stay attached.
      Each click counts; a double-click toggles twice.
- [ ] **Click or drag.** Click a title with a slight hand wobble. Then deliberately drag a stack
      away and back to its starting point. Press Escape while holding a title before dragging.
      **Pass:** a small wobble toggles without moving; a deliberate drag never toggles on release;
      Escape cancels the pending click. Repeat at another display scaling if available.

**Round 2 — the recovery blocker. About 10 minutes**

- [ ] **Compact rows.** Open a short section and one containing more than two rows of icons.
      **Pass:** the short section fits one row; the larger section shows two complete rows and
      scrolls to the rest. Labels remain readable and connected titles remain attached. Add/remove
      a reference across the one/two-row boundary, then restart; the sizing follows the contents.
      Repeat with large icons. A deliberate vertical resize should retain the chosen manual height.

- [ ] **Monitor round-trip.** With two displays connected (A), capture **Pickets test**. Disconnect
      one display (B), and remove the test reference from B if it is present. Reconnect (A), then
      disconnect again (B). Choose **Quit Pickets**.
      **Pass:** the icon is visible in B and remains visible after Quit. Reconnecting and relaunching
      A still retains its saved picket membership. If you cannot change display arrangements, mark
      this **NOT TESTED**.
- [ ] **Explorer restart.** Relaunch and capture the test shortcut. In Task Manager, select
      **Windows Explorer → Restart**. Wait a few seconds, then choose **Quit Pickets**.
      **Pass:** pickets return after Explorer restarts; Quit restores the desktop icon.
- [ ] **Crash recovery.** Capture the shortcut; wait two seconds for the layout to save. End only
      **Pickets** in Task Manager. Run `Pickets.exe --restore-icons` from the candidate's folder.
      **Pass:** the desktop icon returns. Relaunching Pickets no longer hides this released icon.
- [ ] **Recovery while running.** Capture it again and run `Pickets.exe --restore-icons` while
      Pickets remains open.
      **Pass:** Pickets exits, the icon returns, and the next launch leaves it visible.

Malformed-layout and failed-uninstall recovery gates are covered by the automated tests. You do not
need to deliberately corrupt your real layout files for this list.

**Round 3 — install and access. About 15–20 minutes**

- [ ] **Install without extras.** Quit Pickets. Run `PicketsSetup.exe` as a standard user, choose
      the default folder, and turn both desktop-shortcut and sign-in startup options off. Defer launch,
      then open Pickets from Start.
      **Pass:** no administrator prompt; onboarding keeps the installer shortcut choice off.
- [ ] **Upgrade.** Capture **Pickets test**, then run the installer again. Allow it to close Pickets
      and reopen it afterward.
      **Pass:** saved membership and appearance survive; Quit restores the icon.
- [ ] **Uninstall.** Relaunch with the shortcut captured, then uninstall through Windows Settings.
      **Pass:** the icon returns before app removal; `%APPDATA%\Pickets` remains.
- [ ] **Custom install and options.** Reinstall to a custom per-user folder. Try the desktop shortcut
      and sign-in option independently (one on, the other off), verifying each result. Turn sign-in
      off again if you do not want it after testing.
      **Pass:** only the options you choose take effect, and Pickets opens from the chosen folder.
- [ ] **Keyboard and Narrator.** Use **Ctrl+Alt+D**, **Tab**, **Ctrl+Tab**, **Enter**, **Shift+F10**,
      and **Escape** to navigate, open a shortcut, and move a reference. Turn Narrator on with
      **Ctrl+Windows+Enter** and listen to the title, expanded/collapsed state, item name, and selection.
      **Pass:** focus is visible and controls are usable and announced. Toggle Narrator off afterward.
- [ ] **Small and scaled screens.** Check 100%, 125%, 150%, and 200% scaling, plus a mixed-DPI monitor
      pair where available. Open Welcome, About, and keyboard help; try high contrast and larger text.
      **Pass:** actions remain reachable and text is readable. Restore your preferred display settings.
- [ ] **Big stack.** Create enough connected pickets to show page arrows; use the arrows,
      **Ctrl+PageUp/Down**, and **Ctrl+Tab**. Try accordion mode and restart Pickets.
      **Pass:** every picket remains reachable and saved membership/order survives.

**Send back this tiny result card**

```text
Windows version/build:
Candidate from TEST_BUILD.txt:
Round 1: PASS / FAIL / NOT TESTED
Round 2: PASS / FAIL / NOT TESTED
Round 3: PASS / FAIL / NOT TESTED
Failed or skipped box:
What happened:
```

Complete the supported Windows 10 and Windows 11 passes and the
[full release checklist](RELEASE_CHECKLIST.md) before publishing. This short list prioritizes the
release blockers; it does not waive the remaining matrix entries. Automated green checks and a
finished code fix mean **ready to test**. Recorded live passes are still required for **ready to release**.
