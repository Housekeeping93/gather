# Install Gather on Windows

Gather brings several messaging services into one Windows app. You do not need Git, a code editor, or the .NET SDK to use an installer.

## Get the installer

Download the **[Windows installer for 0.10.0-dev.9](https://github.com/Housekeeping93/gather/releases/download/v0.10.0-dev.9/Gather-Setup-0.10.0-dev.9.exe)** from the [development release](https://github.com/Housekeeping93/gather/releases/tag/v0.10.0-dev.9).

For newer versions, open [Gather Releases](https://github.com/Housekeeping93/gather/releases) and look under a release's **Assets** for `Gather-Setup-<version>.exe`. Choose the newest development prerelease if you want the latest preview features. The GitHub **Code > Download ZIP** option and **Source code** release assets contain source code, not an installable app.

## What you need

- **Windows 11, 64-bit x64.** ARM devices and other operating systems have not been verified.
- **Microsoft Edge WebView2 Runtime.** It is normally present on Windows 11. If setup reports it missing, use the [Microsoft WebView2 download page](https://developer.microsoft.com/microsoft-edge/webview2/) to install the Evergreen Runtime, then run Gather setup again.
- An internet connection and an account with each messaging service you want to add. Signal linking also requires Signal on your phone.

The installer includes Gather's .NET dependencies and the Signal/Java runtime. You do not need to install Java separately for the bundled Signal integration.

## Install

1. Download the installer supplied by the maintainer or attached to a Gather release. If a SHA-256 checksum is supplied, verify it as described below.
2. If Gather is already running, open **Settings > Quit Gather**. Closing its window may leave it running in the system tray.
3. Double-click `Gather-Setup-<version>.exe` and follow setup. Installation is for your Windows user and normally does not require administrator rights. A desktop shortcut is optional.
4. Open **Gather** from the Windows Start menu. The default installation folder is `%LOCALAPPDATA%\Programs\Gather`.

Current development installers are unsigned, so Windows may show an unrecognized-app warning. Confirm the download source and checksum before proceeding; do not disable Windows security protections to install Gather.

### Optional: verify the download

If the release or maintainer provides `Gather-SHA256SUMS-<version>.txt`, open PowerShell in the folder containing the installer and run:

```powershell
Get-FileHash .\Gather-Setup-<version>.exe -Algorithm SHA256
```

Replace `<version>` with the downloaded version. Compare the full hash with the checksum for that exact filename. If they differ, do not run the file; download it again or contact the maintainer.

## Connect your services

Choose **Add a service**, select a provider, give the account a name, and complete that provider's sign-in inside Gather.

| Service | First setup |
| --- | --- |
| WhatsApp / Telegram | Follow the provider's sign-in screen; approve or link from your phone when prompted. |
| Slack | Sign in to your workspace. The email-code route is recommended for this preview; an existing external-browser session does not automatically transfer into Gather. |
| Signal | Choose **Link Signal**. On your phone, open Signal's **Settings > Linked devices** and scan Gather's QR code. |
| Messenger / Discord | Use the provider's sign-in screen. These integrations are experimental. |

Gather does not require a separate Gather account. Web accounts use separate local browser profiles. Signal is a text-focused development preview: calls, attachments, and importing old phone-message history are not implemented. Microphone and camera access are blocked in the current web-service preview.

## Everyday controls

| Action | Where to find it |
| --- | --- |
| Find an account quickly | **Ctrl+K**, or the toolbar search icon. |
| Open two services side by side | **Split view**; drag the divider to resize. At narrow widths, some actions are under **More actions**. |
| Larger shell controls | **Settings > Shell spacing > Comfortable**. Compact restores the smaller spacing. Provider zoom is separate. |
| Hide the sidebar and pane headers | **Ctrl+Shift+F**, or the focus-view toolbar button. Use **Exit focus view** to restore them. |
| Quiet hours and notification focus timer | **Focus & DND**, or **Settings > Quiet hours & exceptions**. Layout focus view does not mute notifications. |

## Update Gather

Download the newer installer, quit Gather through Settings, and run setup again. You normally do not need to uninstall first. Existing account profiles and settings remain in `%LOCALAPPDATA%\Gather`; do not delete that folder during an upgrade.

There is no automatic updater in this development build. Check the Releases page or ask the maintainer for newer installers.

## Uninstall

Use **Windows Settings > Apps > Installed apps > Gather > Uninstall**. Uninstall removes the program and its shortcuts but retains the account-data folder for a possible reinstall.

To sign an account out and remove its local data, use its **Account settings > Remove account** action in Gather. For Signal, also revoke Gather from **Linked devices** on your phone. Avoid manually deleting `%LOCALAPPDATA%\Gather` unless you intend to remove all Gather profiles, settings, and local Signal data.

## Troubleshooting

| Problem | What to try |
| --- | --- |
| You downloaded a source ZIP | Use the `.exe` installer under release Assets instead; source ZIPs do not install Gather. |
| Setup asks for WebView2 | Install or repair Microsoft's Evergreen WebView2 Runtime, then rerun setup. |
| Gather still looks like the old version | Quit it through Settings before reinstalling. Launch **Gather** from Start rather than an older portable folder. |
| A service does not sign in | Check your connection and provider status. For Slack, try its email-code flow. Report the provider and Gather version if the issue persists. |
| The sidebar has disappeared | You may be in layout focus view. Click **Exit focus view** or press **Ctrl+Shift+F**. |

Report problems in [GitHub Issues](https://github.com/Housekeeping93/gather/issues). Include your Gather version, Windows version, affected provider, and reproduction steps. Remove message content, phone numbers, QR codes, tokens, and other private details from screenshots or logs before sharing them.
