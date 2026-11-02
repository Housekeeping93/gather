# G19 shell density and focus view

Gather 0.10.0-dev.9, 2 November 2026.

## Behaviour

Settings offers Compact and Comfortable shell spacing. Compact retains the previous 32-DIP title bar, 44-DIP toolbar, 72-DIP sidebar, 32-DIP buttons and 40-DIP pane headers. Comfortable uses 40, 56, 88, 40 and 52 DIPs respectively, with larger account tiles, dividers and outer spacing. Native Windows display scaling applies to these dimensions; provider zoom is independent.

Ctrl+Shift+F or the toolbar button toggles layout focus view. It hides the sidebar and pane headers and replaces secondary toolbar actions with quick switch, current notification/focus status, and a named, keyboard-reachable Exit focus view button. Ctrl+Shift+F is handled by the same window-local hook as Ctrl+K and Ctrl+Tab; repeated keydown messages do not repeatedly toggle the layout. Other applications' input is not intercepted. At narrow widths, secondary actions remain available in More actions.

Both preferences persist in the existing settings file. Session controls retain their parent and are not recreated. Account IDs, provider zoom, split ratio, selected panes, unread state, permanent mute and G02 notification policy remain independent of shell layout. Layout focus view is distinct from the timed focus timer.

## Verification

The Release build completes with no warnings or errors. The automated suite passes 83 checks with two existing private-registry-write checks explicitly skipped using `--skip-registry-checks`. Four new core checks cover legacy/default normalization, density/focus metrics, save/reload compatibility and shortcut routing.

The offline `--smoke-test --smoke-shell-layout --data-dir <fresh-directory>` diagnostic passes both densities at requested logical window sizes of 1360×900 and 960×640 on the available 125% display scale, in normal and focus view. It asserts pane/session alignment, title/sidebar/control dimensions, reachable restore controls, session reuse, preserved zoom/mute/quiet policy, Settings Cancel and Save, persisted preferences, and quick-switch access in focus view. Dark and light layouts are inspected using original synthetic native Signal conversations; no account is connected.

Actual client dimensions and scale are recorded in `shell-layout-report.json`; outer Windows frame dimensions differ slightly from client size. Physical Ctrl+Shift+F from browser/native inputs, title-bar dragging/caption-button interaction and 100%, 150% and 200% monitor-scale transitions remain desktop checks. The diagnostic invokes the shared focus-view action, not injected physical input. Browser sessions are reused by the same layout path, but authenticated browser continuity was not exercised here.

## Installer workflow

Run `./scripts/Build-Installer.ps1` from the source directory after loading the workspace build environment. It publishes with the required single-node flags, verifies Signal runtime and logo hashes, checks isolated empty and normal startup, compiles a version-matched installer, and runs a separate QA installer with a test identity and the same file payload. The installed QA copy receives the same runtime/startup verification. Existing production installation and account data are not touched by these checks.

Deliverables are grouped in `outputs/installers`, `outputs/releases`, `outputs/docs` and `outputs/previews`; raw QA evidence and logs remain in `work/qa` and `work/logs`. Each completed workflow emits `Gather-installer-verification-<version>.json` with the exact package, installer, SHA-256 and QA directory.

## Desktop acceptance

1. Change both presets with real service sessions, including a browser draft. Confirm profile sign-ins, zoom and draft content remain unchanged; check both light and dark themes.
2. From a native Signal composer and an embedded browser input, toggle Ctrl+Shift+F twice. Confirm focus remains usable, pane selection/ratio is restored, and a held shortcut does not flicker between modes.
3. Use Exit focus view with keyboard and Narrator, then restart with Comfortable and focus view saved. Confirm both preferences restore without trapping focus.
4. At 100%, 125%, 150% and 200% Windows scaling, check title-bar dragging, window controls, sidebar scrolling, overflow actions, pane alignment and the visible restore button at minimum window size.
5. Run the default automated suite in a session allowed to create private HKCU test keys to exercise the two existing registry checks.
