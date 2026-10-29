# Mailcario installation and updates

The Windows installer installs Mailcario for the current user without administrator rights. It bundles the .NET 10 runtime and installs Microsoft's WebView2 runtime only when missing. User accounts and settings live in `%LOCALAPPDATA%\Mailcario`, outside the installation directory.

## Build an installer

Install the free Inno Setup 7 compiler, then run from this directory:

```powershell
.\build-installer.ps1 -GitHubRepository "OWNER/Mailcario"
```

The output is `dist\Mailcario-Setup-X.Y.Z.exe`. The script reads the version from `..\Mailcario.csproj`, publishes the application and a separate update helper, excludes `oauth-providers.local.json`, verifies Microsoft's WebView2 bootstrapper signature, and prints the installer's SHA-256 hash. Pass `-InnoCompiler "C:\path\to\ISCC.exe"` if Inno Setup is installed elsewhere.

`update-source.json` currently points to `Bouneh/Mailcario`. If `-GitHubRepository` is omitted, the script uses that value.

## Publish a future update

1. Increase `<Version>` in `Mailcario.csproj` to `X.Y.Z` and build a new installer with the same `-GitHubRepository` value.
2. Create a **published, non-prerelease** GitHub Release tagged `vX.Y.Z`.
3. Attach the installer with the exact name `Mailcario-Setup-X.Y.Z.exe`. GitHub's release API must expose the asset's `sha256:` digest.

When Mailcario opens, it checks the repository's latest release. If its version is newer, the user can choose **Later** or **Download update**. A download is accepted only from GitHub over HTTPS, is limited to 500 MB, and must match the release's SHA-256 digest. The helper waits for Mailcario to close, runs the Inno installer in the existing installation directory, then starts Mailcario again. A failed update shows an error and does not replace the installed application.

The installer is currently unsigned. Windows may show a publisher warning until a code-signing certificate is obtained. No GitHub upload is performed by the build script.
