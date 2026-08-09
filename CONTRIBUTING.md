# Contributing

## Development environment

This is a native Windows desktop application, not a cross-platform .NET project.
Use Windows 10 or 11 with:

- The .NET 9 SDK.
- The Windows SDK required by the app target framework.
- Windows App Runtime 2.3.
- Visual Studio 2022, Rider, or another editor capable of building WinUI 3.

Build and launch through:

```powershell
.\scripts\run.ps1
```

If the source-built app is already running, the script asks permission to close it
gracefully before building and offers an explicit force-close fallback if needed.

Run the Core tests through:

```powershell
dotnet test tests\WindowsCompanion.Core.Tests\WindowsCompanion.Core.Tests.csproj
```

`dotnet run` is not supported for this unpackaged WinUI project. See the README for
the runtime-resolution details.

## Repository conventions

- Use [Conventional Commits](https://www.conventionalcommits.org/) for commit and
  pull request titles.
- Keep platform-independent logic in `WindowsCompanion.Core`; Windows API integration
  belongs in `WindowsCompanion.App`.
- Add happy-path and failure-path unit tests for new Core contracts.
- Update the relevant specification or issue when implementation discoveries
  change expected behavior.
- Never commit Home Assistant URLs, credentials, webhook IDs, logs, or local
  settings.
- Keep direct GitHub Actions and reusable workflows pinned by commit digest.

## Pull requests

Keep changes focused and explain user-visible behavior, privacy implications, and
any Home Assistant protocol assumptions. The Windows app build and Core test suite
must pass before merge.

Security vulnerabilities must be reported privately according to
[SECURITY.md](SECURITY.md), not through a public issue.

## Tooling decisions

The repository deliberately does not provide a Linux devcontainer: it cannot build
or run the WinUI application and would create a misleading partial development
environment. `WindowsCompanion.Core` remains portable and can be tested independently,
but full feature work requires Windows.

Mise is used only to pin repository lint/security tools consumed by the shared
organization workflow. It does not manage the .NET or Windows SDK toolchain.

No repository-wide VS Code settings are committed. Visual Studio and Rider are the
primary full-app environments; individual editor preferences should remain local.
