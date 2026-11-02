# G01 quick switch verification

Gather 0.10.0-dev.8, 2 November 2026.

## Scope

Ctrl+K and the toolbar search button open a modal account switcher. Search matches all typed words against account/provider names, ignoring case. Recently used account IDs persist in the existing settings file, with removed IDs pruned and history limited to 20 accounts. Remaining accounts retain sidebar order. No message or browser-content indexing is involved.

Up/Down changes selection, Enter opens the selected account in the focused pane, and Escape restores the previous control's focus. Existing visible accounts use their existing pane/session. Account identities, split ratio, permanent mutes and quiet-hours policy are preserved. Named results and a polite live region expose the selected account/provider and result position to accessibility clients.

## Automated checks

Load `../tooling/build-env.ps1` in the canonical workspace before running these commands:

```powershell
dotnet build Gather.App/Gather.App.csproj -c Release --no-restore -m:1 -nr:false -p:BuildInParallel=false -p:UseSharedCompilation=false
dotnet run --project Gather.Tests/Gather.Tests.csproj --no-restore -p:BuildInParallel=false -p:UseSharedCompilation=false -- --skip-registry-checks
```

The Release build completes without warnings or errors. The restricted-environment run passes 79 checks and explicitly skips two registry-write tests. A normal run was attempted and reached 78 passing checks before its first registry test was denied access. The normal test command still includes both registry tests by default.

Six new core checks cover mixed account/provider queries, case and Unicode matching, recent ordering, save/reload and removal, legacy/null/oversized history, and pane selection without duplicate placement. Existing Signal, quiet-hours, profile identity, navigation and startup-command parsing checks remain included.

## Offline UI diagnostic

Run a built Gather executable with a new data directory:

```powershell
Gather.exe --smoke-test --smoke-quick-switch --data-dir C:\path\to\new-check-folder
```

The default diagnostic uses synthetic native Signal sessions and an editable native input fixture for a web account. It exercises empty/no-match states, search focus, command handlers, repeated open requests, accessible labels/live-region text, Escape focus restoration, Enter and button selection, recent persistence, focused-pane replacement and existing-session reuse. It captures dark and narrow light dialog images and writes `quick-switch-report.json`; failures write `quick-switch-failure.txt`. No provider account is opened and no Signal connection is made.

The default diagnostic invokes shared command handlers directly and uses UI Automation to invoke dialog buttons. Add `--smoke-quick-switch-keyboard` to require real injected keyboard events on an interactive desktop. Add `--smoke-quick-switch-web` to replace the native input fixture with a real WebView2 displaying local HTML and check that switching causes zero browser navigations. Simulated input did not reach Gather in this restricted session, and WebView2 did not create a usable controller in the attempted offline browser checks. Physical Ctrl+K, actual browser draft/session continuity, and screen-reader speech remain desktop checks. The report identifies whether real keyboard injection and real WebView2 were exercised.

The default native-fixture diagnostic passes all of its assertions, including focus moving into the native Signal composer after selecting its existing pane. It also verifies preserved permanent mute, a saved recent-account order, no-match Enter behavior, and release of the dialog guard after Cancel.

## Desktop acceptance checks

1. With a native Signal composer focused, press Ctrl+K. Type an account/provider name, change selection with Up/Down and press Enter. Repeat with a provider page's input focused and confirm the provider does not receive Ctrl+K.
2. In split view, select the account already visible on the other side. Confirm the existing pane is focused, its page/draft is unchanged, and no duplicate session appears. Select a third account and confirm it replaces only the focused side.
3. Open Ctrl+K from a draft field and press Escape. Continue typing in that same field. Repeat from Home and confirm focus remains reachable.
4. With Narrator enabled, check the search label, selected account/provider and result position. Confirm long account names remain readable via accessible names/tooltips in dark and light themes at minimum window size.
5. Run the normal test command in an environment with access to private HKCU test keys to complete both startup-registration checks. Neither test writes an actual Windows startup entry.
