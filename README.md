<p align="center">
  <img src="docs/assets/mailcario-wordmark.svg" alt="Mailcario" width="430">
</p>

<h3 align="center">A familiar inbox, on your terms.</h3>

<p align="center">
  A free, open-source desktop email client for Windows.<br>
  Multiple accounts, a focused Outlook-style layout, calendar invitations, and privacy by default.
</p>

<p align="center">
  <a href="https://github.com/Bouneh/Mailcario/releases/latest"><img alt="Latest release" src="https://img.shields.io/github/v/release/Bouneh/Mailcario?style=flat-square&color=11747B"></a>
  <a href="LICENSE"><img alt="MIT license" src="https://img.shields.io/badge/license-MIT-11747B?style=flat-square"></a>
  <img alt="Windows 10 and 11" src="https://img.shields.io/badge/Windows-10%20%7C%2011-11747B?style=flat-square">
  <a href="https://github.com/Bouneh/Mailcario/stargazers"><img alt="GitHub stars" src="https://img.shields.io/github/stars/Bouneh/Mailcario?style=flat-square&color=11747B"></a>
</p>

<p align="center">
  <a href="https://github.com/Bouneh/Mailcario/releases/latest"><strong>Download for Windows</strong></a>
  &nbsp;·&nbsp; <a href="README.fr.md">Lire en français</a>
  &nbsp;·&nbsp; <a href="CONTRIBUTING.md">Contribute</a>
</p>

<p align="center"><img src="docs/images/inbox-dark.png" alt="Mailcario inbox with folders, message list and reading pane" width="100%"></p>
<p align="center"><sub>Interface preview with fictional accounts and messages.</sub></p>

## Email that feels at home on Windows

| A clear workspace | Useful everyday tools | Privacy built in |
| --- | --- | --- |
| Outlook-inspired three-pane layout, resizable columns, folders, working folder search, light and dark themes. | Multiple IMAP accounts, SMTP sending, replies, forwarding, attachments, signatures from any account, a local address book, rules and notifications. | Remote images stay blocked until you allow them. Suspected tracking pixels remain blocked. HTML scripts are disabled. |

Mailcario is a **local Windows email client**. You do not need to host a server to read or send mail through your existing provider. Account settings stay on your PC; stored credentials are protected for your Windows user account.

Each folder loads recent messages first, then loads older messages as you scroll or select **Load older messages**. Search results also continue beyond the first page.

Right-click any mail folder to enable or disable notifications for that folder. A message moved by a Mailcario rule can alert you from its destination folder, and clicking the alert opens that folder. The general notification, sound and preview options remain in **Settings**.

Received attachments are listed in the reading pane. You can download them, preview PDF, image and text files inside Mailcario, and print those previewable formats. Other file types can be saved and opened with a compatible application.

### A closer look

| Compose | Import Thunderbird accounts |
| --- | --- |
| ![Mailcario compose window](docs/images/compose-dark.png) | ![Importing Thunderbird IMAP accounts](docs/images/thunderbird-import.png) |

The compose ribbon includes clipboard tools, rich-text formatting, color palettes, numbered lists and recipient checking. You can drag files into a draft and switch between saved signatures. Unread inbox counts appear on the taskbar icon; opening a message updates the count.

<details>
<summary><strong>Message privacy controls</strong></summary>

![Remote images and tracking pixel notices](docs/images/privacy-dark.png)

Images from the internet are blocked by default. A message banner lets you load ordinary remote images when you choose; detected tracking pixels stay blocked. Signature images embedded in the email can also be shown on demand without contacting an external host. In **Settings**, you can enable automatic image loading separately for each account. Image hosts may then learn your IP address and when you opened a message; tracking detection is not infallible.
</details>

## Get started

1. Download `Mailcario-Setup-X.Y.Z.exe` from the [latest release](https://github.com/Bouneh/Mailcario/releases/latest).
2. Run the installer. Mailcario installs for your Windows account without administrator rights.
3. Add an address, or choose **File → Import from Thunderbird** to bring in IMAP account and server settings.
4. Sign in with your provider. Thunderbird passwords and local messages are not copied.

Mailcario checks for a newer published release when it starts. If one is available, you can choose **Later** or **Download update**. The download is checked against GitHub's SHA-256 digest before installation; Mailcario then restarts automatically. New releases are built and published by GitHub Actions when the project version increases on `main`.

Use **About** to see the installed version, open the repository, and check for or download an update at any time. Drag the divider beneath the account list to adjust its height.

New mail appears in a Windows notification with a short message preview. Closing the window keeps Mailcario running in the notification area; use its icon to reopen the inbox or quit the application.

### Provider support

| Provider | Connection |
| --- | --- |
| La Poste | IMAP/SMTP with your account credentials |
| Gmail | Google sign-in in your browser when the configured OAuth app permits your account; otherwise use an app password. Public OAuth access depends on Google's approval. |
| iCloud | IMAP/SMTP with an app-specific password |
| Other providers | Manual IMAP/SMTP settings |
| Outlook / Hotmail | OAuth support is implemented, but a Microsoft application client ID must be configured before sign-in works |

Mailcario can read calendar invitations attached to email and respond to them. Its calendar view is currently local; full calendar synchronization with Google or Microsoft is not part of this first release.

## Built in the open

Mailcario is written in **C# / WPF on .NET 10**. The repository contains the client, the Inno Setup installer, the update helper, and a preview tool that generates screenshots with fictional data.

```powershell
dotnet build Mailcario.csproj -c Release
dotnet run --project Preview/MailcarioPreview.csproj -c Release -- --thunderbird-import-check
```

See [CONTRIBUTING.md](CONTRIBUTING.md) for setup and development guidance, and [Installer/INSTALL_AND_UPDATES.md](Installer/INSTALL_AND_UPDATES.md) for release packaging.

## Star history

[![Mailcario star history](docs/assets/star-history.svg)](https://star-history.com/#Bouneh/Mailcario&Date)

The chart is refreshed from this repository's GitHub Actions workflow. [Explore it on Star History](https://star-history.com/#Bouneh/Mailcario&Date).

## License

Mailcario is released under the [MIT License](LICENSE). Contributions are welcome.
