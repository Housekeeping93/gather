# G18 privacy curtain and Alt+F split toggle

## Window-state correction in dev.11

Activating and dismissing the curtain now preserves the current window size, position and maximized state. The native activation helper restores only minimized windows; otherwise it uses SW_SHOW, which preserves the current geometry. The earlier unconditional SW_RESTORE unmaximized Gather.

The extended native privacy diagnostic passes exact bounds, presenter-state and saved-size assertions at 1060x720, 1360x900 and maximized size (3858x2118 including frame on the test monitor). Each case covers curtain activation, a hidden popup trying to activate while covered, and curtain dismissal. Existing privacy assertions and all 87 automated checks also pass, with the same two registry checks skipped. Build has zero warnings/errors. Initial evidence: `work/qa/g18-window-dbcc475ae97a43da92c858ae0fe3db0b`, `work/logs/g18-window-build.log` and `work/logs/g18-window-tests.log`.

## Original dev.10 delivery

Gather 0.10.0-dev.10, 2 November 2026.

## Behaviour

Press Ctrl+Shift+P or the toolbar privacy button to hide Gather's conversations and account details. The curtain is also available in layout focus view. Settings offers Ctrl+Shift+P, Ctrl+Shift+L and Ctrl+Shift+H; Save applies the choice. Show conversations or the selected shortcut restores the previous layout. Shortcuts apply while Gather or a registered Gather popup has focus; they are not system-wide hotkeys.

The curtain collapses both service surfaces and the sensitive shell without removing their controls from their parent. Registered browser windows are hidden and restored; new website popup requests are handled without opening a window while covered. Popups are registered before asynchronous browser initialization, so a pending popup is included. Open dialogs and menus are dismissed; unsaved dialog edits are discarded. New dialogs, service switching and layout shortcuts are blocked while covered. Focus moves to Show conversations; collapsed content is excluded from keyboard and accessibility navigation.

Notifications are suppressed while covered, regardless of per-account quiet-hours exceptions or preview preferences. The currently displayed Gather tray balloon is cleared, taskbar badges are hidden, and covered accounts are not acknowledged as read. Previously seen messages cannot be made unseen. Notification settings, account mutes, timed focus and quiet-hours rules are retained. Audio policy is unchanged. The curtain and shortcut preference survive restart, including closing to the tray. It is screen privacy, not encryption or an authentication lock; anyone can restore it.

Alt+F toggles split/single view through the same action as the Split view button. Ctrl+Shift+S remains supported. Collapsing keeps the focused account and remembers its companion and split ratio. Expanding needs two accounts. Ctrl+F remains available to providers, Ctrl+Shift+F remains layout focus view, and Ctrl+Alt+F/AltGr+F is not intercepted. Repeated keydown messages do not repeatedly toggle when the thread-local input hook is available. The hook handles both WM_KEYDOWN and WM_SYSKEYDOWN from the main window and registered browser popup windows.

## Verification

- 87 automated checks pass; two existing registry-write checks are explicitly skipped with `--skip-registry-checks`. New checks cover configurable shortcut normalization/routing, Alt+F modifier distinctions and persistent privacy/account/layout compatibility.
- Release publish succeeds without compiler warnings. The installed native-fixture diagnostic passes in dark/light and normal/focus layouts: shell/session visibility, focus target, registered popup hide/restore, notification suppression, active-account policy, guarded shortcuts, dialog dismissal, unsaved Settings cancellation, saved shortcut preference, session/draft retention and split transitions. The thread input hook is available. Actions are invoked directly; physical keypresses are not claimed as verified.
- Published and isolated QA-installed copies pass Signal runtime/logo inventory, empty-profile startup and normal startup. The QA installer exits 0. The privacy diagnostic also passes from the installed copy.
- The installed shell-layout regression passes both densities, normal/focus modes and requested 1360x900 / 960x640 layouts at 125% scale. An isolated normal startup with saved privacy enabled renders the curtain and retains its custom shortcut. Notification suppression is verified at the routing callback; the Windows tray backend is unavailable in this sandbox, so physical balloon removal still needs a normal-desktop check.
- The real offline WebView2 diagnostic was attempted and failed to create a browser controller in this execution session. This is recorded separately, not counted as a successful browser test. Authenticated provider sessions were not opened.

Run from the workspace after loading `work/tooling/build-env.ps1`:

```powershell
dotnet run --project Gather.Tests/Gather.Tests.csproj --no-restore -- --skip-registry-checks
# Use a separate fresh directory for each diagnostic:
Gather.exe --smoke-test --smoke-privacy --data-dir <fresh-directory>
Gather.exe --smoke-test --smoke-privacy --privacy-web --data-dir <fresh-directory>
```

Evidence: `work/logs/g18-tests.log`, `work/logs/0.10.0-dev.10-publish.log`, `work/logs/g18-installer-workflow.log`, `work/qa/g18-native-e261bfd426b64d3e9375fa01f34bff6b`, `work/qa/g18-web-894421e8dc5d4fe28091b0dc5eee23e9`, and `work/qa/installer-0.10.0-dev.10-92454131def04f638ab7efac6938f324/privacy-installed` (paths relative to the outer Gather workspace).

## Remaining desktop checks

1. With real browser and Signal drafts, use Alt+F twice, the privacy shortcut twice, and hold each chord. Confirm draft/session retention and focused account; check Ctrl+F still reaches the provider.
2. Trigger privacy from a browser popup and from Settings/quick switch. Confirm no account details, popup or notification text remain visible, then restore. Repeat while a sign-in popup is initializing.
3. Use Tab, Shift+Tab and Narrator while covered; only the curtain and standard window controls should be reachable. Check the task switcher and tray notification behaviour on the normal desktop.
4. Restart while covered and test minimize/restore, close-to-tray, minimum window size and alternate display scales. Gather must stay covered until explicitly restored.

## Installer

`outputs/installers/Gather-Setup-0.10.0-dev.10.exe`

SHA-256: `4d77c19566c324fe375c4d2b502bfca02831031a1990525026b44bc3afa7ab04`.

Quit the existing Gather instance through Settings or its tray menu before installing. The QA checks use isolated profiles and do not replace the user's installation.
