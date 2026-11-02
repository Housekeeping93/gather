# Changelog

## 0.10.0 — MVP · 2026-11-02

The first full MVP brings together the features developed in the previews below.

- Separate web-service accounts and native Signal text conversations.
- Single/split view with a resizable divider, pane swapping and saved layouts.
- Alt+F layout switching and Ctrl+K account search with recent accounts.
- Compact/Comfortable spacing, layout focus view and light/dark/system themes.
- Focus timers, recurring quiet hours and per-account exceptions.
- Configurable privacy curtain that hides conversations and Gather popups, suppresses notifications and preserves window size and maximized state.
- Optional Windows startup, tray operation, unread indicators and notification preview preferences.

See [MVP release notes](docs/releases/0.10.0.md) for installation and known limitations.

## 0.10.0-dev.11

- Fixed the privacy curtain taking Gather out of maximized mode. Covering, uncovering and returning focus from a covered popup now preserve window size and position.

## 0.10.0-dev.10

- Added the privacy curtain with a configurable shortcut and a visible restore action.
- Hid registered popups and suppressed notifications while covered; retained sessions and saved curtain state.
- Added Alt+F to toggle split/single view while keeping the focused account.

## 0.10.0-dev.9

- Added Compact and Comfortable layout presets.
- Added Ctrl+Shift+F layout focus view and a visible exit action.
- Moved secondary toolbar actions into an overflow menu at narrow widths.

## 0.10.0-dev.8

- Added Ctrl+K quick switch with account/provider search, keyboard navigation and recent accounts.
- Selecting an already visible account focuses its existing pane.

## 0.10.0-dev.7

- Added timed focus, recurring quiet hours, overnight schedules and account exceptions.
- Added skip/resume controls and restoration of temporary notification policy across restart.

## 0.10.0-dev.6

- Reduced header, sidebar and pane spacing to give conversations more room.
- Added inline status and a consistent split-view control in both themes.

## 0.10.0-dev.5

- Added the optional Start Gather when Windows starts setting.

## Earlier previews

- **0.10.0-dev.4:** Signal contact/profile pictures, Enter to send and Shift+Enter for a new line.
- **0.10.0-dev.3:** Native Signal phone linking and text conversations inside Gather.
- **0.9.0-rc.2:** Web-service shell with isolated profiles, split view, saved layouts, theme preferences, tray operation and notification controls.

Detailed historical validation and runtime notes are in [Development](docs/DEVELOPMENT.md).
