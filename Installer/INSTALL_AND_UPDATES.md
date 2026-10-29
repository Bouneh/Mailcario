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

1. Increase `<Version>` in `Mailcario.csproj` to `X.Y.Z` and commit the changes.
2. Push the commit to `main` with Git. No browser upload or locally built installer is needed.
3. `.github/workflows/build-and-release.yml` builds and checks the app on Windows, builds the installer, creates the `vX.Y.Z` tag, and publishes the GitHub Release with `Mailcario-Setup-X.Y.Z.exe`. It verifies GitHub's `sha256:` asset digest before finishing.

Every push and pull request is compiled and checked. A push to `main` only creates a release when its version has not been published yet. Subsequent commits at the same version do not replace an existing installer; increase the version for the next release. The workflow can be rerun from GitHub Actions if a build fails before publication.

When Mailcario opens, it checks the repository's latest release. If its version is newer, the user can choose **Later** or **Download update**. A download is accepted only from GitHub over HTTPS, is limited to 500 MB, and must match the release's SHA-256 digest. The helper waits for Mailcario to close, runs the Inno installer in the existing installation directory, then starts Mailcario again. A failed update shows an error and does not replace the installed application.

The installer is currently unsigned. Windows may show a publisher warning until a code-signing certificate is obtained. No GitHub upload is performed by the build script.
