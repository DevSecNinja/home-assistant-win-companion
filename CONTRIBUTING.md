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

## Dependency management

[Renovate](https://docs.renovatebot.com/) (`renovate.json5`) tracks and proposes
updates for every dependency in this repository:

- NuGet packages referenced by the `.csproj` files.
- npm packages in `brand/package.json`.
- Tool versions pinned in `.mise.toml`.
- Direct and reusable GitHub Actions, including the SHA-pinned digests and the
  `# renovate: datasource=... depName=...`-annotated version inputs (for example
  `mise-version` and `syft-version`) in `.github/workflows/*.yml`.

Two categories are intentionally exempt or restricted:

- `H.NotifyIcon.WinUI` and `Microsoft.WindowsAppSDK` in
  `src/WindowsCompanion.App/WindowsCompanion.App.csproj` are still tracked by
  Renovate, but `renovate.json5` disables automerge for them: both changes affect
  runtime prerequisites (the installed Windows App Runtime, or the minimum .NET
  version) and need manual verification before merging.
- The Inno Setup installer download in `.github/workflows/release.yml` (`INNO_URL`
  / `INNO_SHA256`) is **not** managed by Renovate. The workflow verifies the
  downloaded installer's SHA-256 hash and Authenticode signature before running
  it, and Renovate cannot compute or verify that hash for a new release. Bumping
  this pin requires manually downloading the new installer, confirming its
  Authenticode signature, recomputing the SHA-256 checksum, and updating both
  values together.
