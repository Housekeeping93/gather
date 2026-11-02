# Gather 0.10.0 - MVP

## Install and get started

**[Download the Windows installer](https://github.com/Housekeeping93/gather/releases/download/v0.10.0/Gather-Setup-0.10.0.exe)** · **[Installation guide](docs/INSTALLATION.md)** · **[All releases](https://github.com/Housekeeping93/gather/releases)**

Gather 0.10.0 is the full MVP release for Windows 11 x64. The guide covers setup, service linking, updates and troubleshooting. Downloading the source ZIP does not install Gather.

## MVP release

Gather combines separate service accounts, native Signal text conversations, split view (Alt+F), quick switch (Ctrl+K), layout presets, focus / quiet hours, and a configurable privacy curtain. [Full release notes](docs/releases/0.10.0.md).

## Previous update: dev.11

The privacy curtain now preserves your current window size, position and maximized state when hiding or showing conversations, including focus returning from a Gather popup. The updated local installer is `outputs/installers/Gather-Setup-0.10.0-dev.11.exe`. [Fix details](docs/releases/0.10.0-dev.11.md).

## Previous update: 0.10.0-dev.10 - privacy curtain and Alt+F

**Ctrl+Shift+P** or the toolbar privacy button covers conversations and account details, hides Gather popups and suppresses notifications. Restore with the same shortcut or **Show conversations**. Settings offers three shortcut choices. The curtain preserves sessions and layout, and remains covered across restart. This is screen privacy, not encryption or an authentication lock.

**Alt+F** switches between split and single view; the focused conversation stays visible when collapsing. Ctrl+Shift+S remains supported. [G18 verification and desktop checks](docs/G18-verification.md) describe the tested behaviour and remaining browser/keyboard checks. A tested dev.10 installer is available in the local workspace at `outputs/installers/Gather-Setup-0.10.0-dev.10.exe`.

## Previous update: 0.10.0-dev.9 - shell spacing and focus view

Choose **Settings → Shell spacing → Compact or Comfortable**. Compact retains the existing shell dimensions; Comfortable enlarges buttons, account tiles, pane headers and spacing without changing provider zoom. **Ctrl+Shift+F** or the toolbar focus-view button hides the sidebar and pane headers. The visible **Exit focus view** button restores them. Both preferences survive restart. This layout focus view leaves timed focus and scheduled Do Not Disturb unchanged.

At narrower widths, secondary toolbar actions move into **More actions**. Quick switch and the current notification/focus status remain available. Existing account sessions stay attached, split ratios and selected panes are retained, and provider zoom is unchanged. [Verification and desktop checks](docs/G19-verification.md) describe the tested 125% display scale and remaining hardware checks.

Build a feature installer from this workspace with `./scripts/Build-Installer.ps1`. It validates version metadata, publishes the app, checks runtime inventories and startup, compiles the production installer, and tests a separate QA installer with the same file payload in an isolated workspace folder. It does not install over the user's running copy. Read the resulting installer verification JSON in `outputs/docs` for the actual build paths and checksums.

## Previous update: 0.10.0-dev.8 - quick switch

Feature deliveries always include a tested Windows installer, its exact location, and the source pull request. Keep previous installer versions. Current local installers are in `../../outputs/installers`, published apps in `../../outputs/releases`, and verification evidence in `../../work/qa` and `../../work/logs`.

Press **Ctrl+K** or choose the search icon on the toolbar to switch service accounts. Search by account name, provider name, or a combination of words. Empty search lists recently used accounts first, followed by the remaining accounts in sidebar order. Up/Down selects a result, Enter switches, and Escape closes the panel and restores focus. Selecting an account already visible in the other pane focuses that pane without duplicating or reloading its session.

The switcher searches only account/provider names. It does not read messages. Recent account IDs are stored locally in the existing settings file; account profile IDs and permanent mute preferences are preserved. Results include accessible account/provider labels and a polite live announcement of the selection. Existing Ctrl+Tab navigation remains available outside dialogs.

Build and validation details, including desktop checks still needed, are in [docs/G01-verification.md](docs/G01-verification.md). Run the offline UI check with `--smoke-test --smoke-quick-switch --data-dir <fresh-folder>`. Add `--smoke-quick-switch-keyboard` on an interactive Windows desktop to exercise actual injected keyboard events, and `--smoke-quick-switch-web` for a real offline WebView2. The default diagnostic uses native fixtures, invokes the same command handlers and uses UI Automation for dialog buttons.

The automated test runner supports `--skip-registry-checks` for restricted environments. This explicitly skips the two startup-registration tests that write private test registry keys; normal runs continue to include them. Set TEMP and TMP to a writable test directory if the default temporary folder is unavailable.

## Previous update: 0.10.0-dev.7 - focus and scheduled Do Not Disturb

G02 adds timed focus (25, 50 or custom minutes), recurring quiet hours with starting days, and per-account exceptions. Open **Focus & DND** on the toolbar or **Settings → Quiet hours & exceptions**. Quiet hours default to off. Temporary restrictions preserve account mute preferences, survive restart and combine safely when focus and scheduled DND overlap. Web popups and native Signal notification delivery use the same policy.

Verification: all 75 automated checks passed. Isolated dialog checks cover Save/Cancel, invalid times/days, exceptions, reload and focus options. Offline real WebView2 main/popup checks cover effective mute, badge restoration, notification policy and the suspend/resume handler. Existing layout and packaged startup/runtime checks pass. Physical Windows sleep/wake and authenticated provider playback were not exercised. Use --smoke-test --smoke-quiet-ui or --smoke-test --smoke-quiet-web with a fresh --data-dir for the dedicated checks. See ../../outputs/docs/Gather-G02-0.10.0-dev.7.md.

## Previous update: 0.10.0-dev.6 — compact desktop layout

The shell now gives conversations more room: a 32-DIP title bar, a single 44-DIP toolbar with inline status, 8-DIP outer margins, 40-DIP pane headers, and no permanent footer. The service rail is narrower, toolbar actions use consistent icons, and split view uses a muted green accent in both themes. Full account names and status remain available through tooltips when truncated. The 12-DIP divider retains dragging, keyboard resizing, and double-click reset.

Verification: all 57 existing checks pass. The published app passes runtime/logo inventory validation, empty-profile startup, and normal startup. Offline visual checks cover dark split view, light single view, Home, long account names, and 30/70 and 70/30 splits at the minimum 960 × 640 window size. Service bounds align with the pane surfaces after resizing, swapping, and collapsing. No provider sessions were opened by these checks. Authenticated web-service rendering was not retested.

Quit the earlier Gather through Settings → Quit Gather before opening dev.6. The same account data and settings are used. If moving to a new portable folder with Windows startup enabled, save Settings in the new copy to update its startup path.

## Previous update: 0.10.0-dev.5 — Windows startup setting

This build adds **Settings → Start Gather when Windows starts**. Turn it on and choose **Save** to open Gather after you sign in to Windows. Turn it off and save to remove the startup entry. Cancel leaves it unchanged. Startup is off by default; this update does not automatically enable it.

The setting registers the current executable for the current Windows user without administrator access. Custom data folders are retained. Keep Gather's whole app folder in its saved location; if you move it or switch builds, reopen Settings in that copy and save with startup on to update the path. Windows Settings → Apps → Startup can separately pause Gather; re-enable it there if needed. Startup opens the normal window with your existing services and layout.

Verification: 57 automated checks pass, including startup registration round trips, preservation of other entries, Windows command-line parsing and rejected paths. A focused settings diagnostic verifies Save, Cancel, reload and disable through the actual dialog using a private test registry key. Both themes were inspected. No real startup entry was enabled by the tests, and an actual Windows sign-in remains a desktop acceptance check. Reproduce the UI check with `--smoke-test --smoke-settings-ui --data-dir` and a fresh empty folder. The installer script compiles with conditional cleanup of only its own startup entry; this development delivery is the portable app folder.

## Signal support retained

Signal is a sixth native service inside Gather. This build includes signal-cli 0.14.7 and Eclipse Temurin 25.0.4.1+1 for Windows x64. Both downloads passed their upstream checksum checks. The user confirmed that phone linking and chatting work in dev.3. Dev.4 adds contact/profile pictures in the conversation selector and selected-chat header, plus Enter to send and Shift+Enter for a new line. Ctrl+Enter still sends. IME composition and key repeats do not trigger a send. The 0.9.0-rc.2 distribution in outputs is unchanged.

Quit any earlier Gather build through Settings → Quit Gather before opening this one. Existing linked accounts reconnect without another QR scan. For a new account, select Signal → Link Signal, then use Signal on your phone → Settings → Linked devices to scan the code. Conversations remain inside Gather and can use either split-view pane. This first text preview receives new messages; it does not import older phone history.

The current slice includes the official Signal logo, mixed native/web split view, local QR generation, a process JSON-RPC adapter, text state handling and Windows-protected Gather message history. Files, calls, older history and full disappearing/view-once support are not implemented. The latter message bodies are omitted from ordinary history. The adapter disables attachment downloads and the send log. Protection of the adapter's own identity files still needs review.

Profile pictures load through signal-cli's `getAvatar` command. An unavailable profile picture falls back to a synced contact picture, then initials. Group pictures use the group avatar where available. Visible pictures load asynchronously with at most two requests in progress; oversized or malformed images are ignored without interrupting chat. Gather keeps decoded thumbnails in memory per account and does not add image files to its message history. signal-cli maintains its own avatar cache in the existing connection folder. Existing linked accounts are retained; no relink is required for this update.

The preview uses `settings-signal-preview.json`; it imports legacy settings if that file is absent and leaves `settings.json` for the old release. Website profile IDs remain unchanged. Use isolated `--data-dir` folders for testing and avoid running both releases against the same browser profile simultaneously.

Build the solution with the .NET SDK selected in `global.json`. `Gather.Tests` now runs 57 checks; these include controlled RPC tests, avatar retrieval/fallback/payload limits, Windows child-process ownership, complete runtime inventories, cancellation before account creation and startup registration. The Signal server is never accessed by those tests.

```powershell
dotnet run --project Gather.Tests/Gather.Tests.csproj
dotnet publish Gather.App/Gather.App.csproj -c Release -o artifacts/Gather-Signal-Development
```

Signal's process interface is pinned to signal-cli 0.14.7. To prepare a trusted, extracted signal-cli JVM distribution and Windows x64 Java 25 distribution without changing system Java:

```powershell
./scripts/Prepare-SignalRuntime.ps1 -SignalCliDirectory 'C:\downloads\signal-cli-0.14.7' -JavaDirectory 'C:\downloads\java-25'
```

Run the import and verification scripts in PowerShell 7. The import checks versions, copies both complete distributions to a temporary sibling folder, inventories their files and verifies every hash before moving them to `Gather.App/runtimes/signal`. Existing installations are preserved. A failed staging folder stays outside the packaged runtime path. Rebuild afterward. The dev.4 package passes `Verify-Package.ps1 -RequireSignalRuntime`, including complete runtime hashes and app startup. Runtime files are copied as ordinary files, outside the WinUI resource index. Upstream provenance and license locations are documented in `Notices/signal-runtime.md`; final redistribution/source review remains pending before a public release. Runtime binaries are included in the development app but excluded from the source ZIP.

Gather checks the same complete inventory before starting Signal, including Java's VM/modules and the native Signal dependency. File verification runs in the background; a cancelled startup cannot create account data or start Signal. The manifest verifies consistency with the imported files, not upstream authenticity, so use trusted distributions.

The app and package validator share 13 synthetic inventory fixtures covering complete, missing, changed, malformed and unexpected files. These fixtures never execute a runtime. To reproduce both sets of checks, use a new fixture directory:

```powershell
dotnet run --project Gather.Tests/Gather.Tests.csproj -- --runtime-fixtures artifacts/runtime-fixtures
./scripts/Verify-SignalRuntime.ps1 -FixtureDirectory artifacts/runtime-fixtures
```

To explicitly test the real runtime and Signal provisioning service, use a new isolated report directory. This check obtains a linking URI only in memory, checks QR capacity, stops the owned process and verifies an unlinked restart. It does not finish linking, access existing accounts, subscribe to messages or send anything. Reports omit the provisioning secret.

```powershell
dotnet run --project Gather.Tests/Gather.Tests.csproj -- --signal-runtime-check artifacts/Gather-Signal-Development artifacts/runtime-live-check
```

For offline visual QA use `--smoke-test --smoke-ui --data-dir` with a fresh empty directory. This displays labelled sample messages and opens no sessions. The report includes Signal pane placement, swap and collapse checks.

For the focused Signal avatar, initials, theme and split-pane check use `--smoke-test --smoke-signal-ui --data-dir` with a fresh directory. The full web/Signal visual diagnostic currently hits a native crash in this environment in both dev.3 and dev.4; the focused Signal diagnostic and ordinary startup checks pass. Diagnostic avatar captures use an original synthetic silhouette and contain no account data.

To verify synthetic QR matrices with ReportLab:

```powershell
dotnet run --project Gather.Tests/Gather.Tests.csproj -- --qr-fixtures artifacts/qr-fixtures
python scripts/Verify-SignalQr.py artifacts/qr-fixtures
```

Upstream references: [Signal release](https://github.com/AsamK/signal-cli/releases/tag/v0.14.7), [JSON-RPC](https://github.com/AsamK/signal-cli/blob/v0.14.7/man/signal-cli-jsonrpc.5.adoc), [contact schema](https://github.com/AsamK/signal-cli/blob/v0.14.7/src/main/java/org/asamk/signal/json/JsonContact.java), [official logo](https://signal.org/brand/).

## Prior web-service baseline: 0.9.0-rc.2

The documentation below describes the existing web-service baseline. Its 19-check count and version references are historical; current development verification is described above.


Gather is a native Windows desktop shell for official messaging websites. It includes side-by-side split view, isolated account profiles, saved layouts, an account rail, light/dark/system appearance, tray behavior, and Windows notification integration with per-account mute and private previews.

**This is a compatibility prototype, not a verified production messaging client.** Account linking and notifications require hands-on checks with the actual providers and Windows desktop session. No provider accounts, credentials, browser profiles, or test captures are included in this source package.

## Run the preview

Extract the whole Windows preview package into a folder and open `Gather.exe`. Keep the files beside the executable. The .NET and Windows App SDK dependencies are included; the Microsoft Edge WebView2 Evergreen runtime must be installed (it is normally present on Windows 11).

The initial target is Windows 11 x64. This preview is unsigned. It does not install an updater or start with Windows automatically.

## Use it

1. Choose a service, give the account a name, and continue to its official website.
2. Complete the provider's login yourself. For WhatsApp and Telegram this normally involves approving the connection from your phone.
3. Add a second service and enable **Split view**. Select services from each pane's dropdown. Drag the divider, use **Swap sides**, or turn split view off to keep the focused service.
4. Use the service rail to switch accounts. Drag tiles to reorder them, or right-click for **Move up / Move down**. The order is saved without changing your open panes.
5. Use the pane's **…** menu to reload or open account settings. Account removal clears only that account's browser data after confirmation.
6. Closing the window keeps Gather in the tray if available and enabled. Right-click its tray icon and choose **Quit Gather**, or quit through Settings.

The top arrows and **Ctrl+Tab / Ctrl+Shift+Tab** navigate forward/backward through tile order, wrapping at the ends. A single pane moves to the next/previous service. Split view slides one service at a time: **Telegram | WhatsApp → WhatsApp | Slack**. Left reverses the movement. With only two services, the panes swap. These controls navigate services, not a website's page history or conversations within a service.

Ctrl+1 through Ctrl+5 select accounts in tile order. Ctrl+Shift+S toggles split view. The divider supports left/right arrows when focused. A local UI-thread shortcut handler handles Ctrl+Tab before the embedded browser; it does not register system-wide hotkeys. Physical keyboard behavior while typing in provider pages still needs normal-desktop verification.

Use the moon/sun button for dark/light mode, or **Settings → Appearance** for Light / Dark / System. The shell, menus, dialogs, browser background and supported website color preference follow the choice. Services with their own saved appearance can override it.

**Settings → Taskbar badge** selects Number / Dot / Off. Numbers combine reported counts from unmuted accounts and cap at 99+. Activity without a reliable count appears as a dot; a number followed by + is a lower bound. Activity-only dots clear when you focus the account; reported counts clear when the website updates them. Service badges remain visible for muted accounts. A taskbar overlay needs a visible/minimized taskbar button and Windows badge support; closing to the tray hides that taskbar button.

## Authentication and feature limits

- WhatsApp, Telegram and Slack are the core targets. Messenger and Discord entries are experimental.
- The user confirmed working WhatsApp, Telegram, Messenger and Discord logins. Slack's email-code route is the chosen solution. Google/Apple browser sessions do not transfer to Gather; a blocked identity flow offers restarting Slack's sign-in here. External browsing does not count as in-app account linking.
- Hosted sites retain their own interface and read/unread semantics. Exact unread counts are shown only when available from the page title.
- Voice/video calls, global conversation search, automatic updates and configurable sleeping are not implemented in this preview. Loaded accounts remain active for notifications.
- Notifications use Windows tray alerts and honor per-account mute and preview privacy. Clicking an alert opens Gather, not an individual conversation. Windows can suppress alerts through notification settings or Do not disturb. The App SDK notification-registration path was removed in rc.2 after a native startup crash; no extra notification runtime is required.
- Only non-persistent web notifications have a native bridge. Persistent/service-worker notifications and notification clicks after a full app restart remain unverified.
- Pane menus include saved content-size controls from 75% to 150%, using CSS zoom; Reset content size returns to 100%. Provider layouts may respond differently. The shell remembers its restored size and applies display-aware limits.

## Local data

Normal launches store settings and WebView2 profiles under `%LOCALAPPDATA%\Gather`. One app-owned WebView2 environment contains a separate named profile for each account. Settings contain account labels, provider IDs and layout preferences; they do not contain passwords. Browser profiles themselves are sensitive and must not be shared.

The app checks top-level navigation destinations, keeps browser security enabled, exposes no native host objects or general script-message bridge, and blocks microphone/camera/geolocation in this messaging-only preview. External identity flows are offered in the default browser. A custom profile directory can be selected with `--data-dir "C:\path\to\folder"`.

## Build

Install the .NET 10 SDK matching `global.json` and use Windows x64. NuGet restores the Windows build dependencies. The project uses WinUI 3, Windows App SDK 2.4.0, and the WebView2 version selected by that SDK.

```powershell
dotnet restore Gather.sln
dotnet run --project Gather.Tests/Gather.Tests.csproj
dotnet publish Gather.App/Gather.App.csproj -c Release -o artifacts/Gather
```

The executable is `artifacts/Gather/Gather.exe`. Copy the whole folder for distribution. Build output and local data must stay out of source control.

## Verification

The core test runner covers navigation boundaries, malicious hostnames, account profile IDs, persisted settings, unread parsing and split-layout state. It does not simulate provider authentication.

19 core checks pass, including sliding split navigation, reordering, wraparound, persisted appearance and badge preferences, and activity/count handling. Packaged startup and a separate native visual check pass. The visual check uses synthetic badges without opening provider sessions. Taskbar registration reports E_NOTIMPL on the agent's private desktop, so actual overlays remain a normal-desktop acceptance check.

For the native theme, badge and navigation check, use `--smoke-test --smoke-ui --data-dir` with a new empty folder. It captures native light/dark layouts and badge images, exercises forward/backward buttons and reordering, writes a report, then exits. Synthetic data remains only in that diagnostic folder.

For an isolated UI check:

```powershell
artifacts/Gather/Gather.exe --smoke-test --data-dir "C:\temp\Gather-empty-check"
artifacts/Gather/Gather.exe --smoke-test --seed-services --data-dir "C:\temp\Gather-service-check"
```

Use new diagnostic folders with no signed-in accounts. The smoke check writes diagnostic captures and a report to that folder, then exits. `--seed-services` creates fresh account slots for WhatsApp, Telegram and Slack; it does not authenticate them. A failed service process must be reported as a failure, not as successful account linking.

The agent's restricted execution environment launched the native shell but denied WebView2 renderer/GPU process startup. The actual login pages, authenticated sessions, native tray integration and end-to-end notifications therefore need a normal Windows desktop run. The source and package retain browser sandbox protections.

## Release packaging

Official service logos are packaged locally with source attribution and hashes in Notices. Runtime license texts are included there too. Settings saves retain settings.json.bak; invalid JSON is archived before recovering a previous settings copy where available.

The installer uses Inno Setup 6.7.3. With ISCC.exe on PATH, after publishing:

```powershell
ISCC.exe /DPackageRoot="C:\absolute\path\to\artifacts\Gather" /DReleaseOutput="C:\absolute\path\to\artifacts" scripts\Gather.iss
```

The production installer targets LocalAppData\Programs\Gather, creates a Start menu entry and an optional desktop shortcut, checks for WebView2, and preserves LocalAppData\Gather on uninstall. It requires Windows 11 x64-compatible and no administrator privileges.

For isolated packaging QA only, compile with /DQA. That build suppresses shortcut/uninstall registry registration, uses a local default directory and skips prerequisite checks; install it into a new test directory and use Verify-Package.ps1 on its installed files before uninstalling. This is how the install/launch/hash/uninstall round trip was verified. Do not distribute the QA installer. The normal shell-folder integration still needs a Windows desktop installation check.

This is an unsigned release candidate. A publisher-signed public release and end-to-end desktop acceptance are outstanding. No analytics, auto-updater or new provider-account access is added by this release.

