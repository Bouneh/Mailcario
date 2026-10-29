# Contributing to Mailcario

Thank you for helping build a free, approachable email client for Windows. Issues, design feedback, translations, documentation, and code contributions are welcome.

## Development setup

- Windows 10 or 11
- .NET 10 SDK
- Inno Setup 7 only if you need to build the installer

```powershell
dotnet build Mailcario.csproj -c Release
dotnet run --project Preview/MailcarioPreview.csproj -c Release -- --thunderbird-import-check
dotnet run --project Preview/MailcarioPreview.csproj -c Release -- --security-check
```

The `Preview` project renders screenshots using fictional data. Run it with an output directory argument to regenerate the documentation screenshots; review images before submitting them.

## Before opening a pull request

1. Open an issue for substantial new behavior so we can agree on the scope.
2. Keep changes focused and preserve the French and English interface strings in `Localizer.cs`.
3. Run the relevant build and preview checks. Explain any feature you could not verify.
4. Do not commit mail data, account files, credentials, `oauth-providers.local.json`, release binaries, or build output.
5. Describe what changed and include screenshots for visual changes.

Mailcario treats incoming HTML and remote content as untrusted. Preserve blocking of scripts, automatic remote images, and tracking pixels when changing message rendering.

By contributing, you agree that your contribution is distributed under the repository's MIT License.
