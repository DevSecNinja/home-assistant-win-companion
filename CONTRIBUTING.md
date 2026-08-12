# Contributing

## Development environment

This is a native Windows desktop application, not a cross-platform .NET project.
Use Windows 10 or 11 with:

- The .NET 10 SDK.
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

`dotnet run` is not supported for this unpackaged WinUI project. See the
[developer guide](docs/development.md) for runtime-resolution details and the
supported build commands.

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

For concurrent work, keep branches and pull requests short-lived and update from
`main` before requesting review. Put feature-specific `MainWindow` behavior in a
focused partial-class file rather than growing `MainWindow.xaml.cs`; reserve the
root file for shared window lifecycle and navigation. Stack dependent pull requests
explicitly, and avoid mixing hotspot refactors with user-visible behavior changes.
Enable Git rerere locally (`git config rerere.enabled true`) so repeated mechanical
conflict resolutions can be reused without weakening review.

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
  `mise-version`, `syft-version` and `inno_version`) in
  `.github/workflows/*.yml`.

The Inno Setup compiler uses that same annotation convention: `inno_version` in
`.github/workflows/release.yml` is the pin, and the step derives the release tag
and the asset name from it. Upstream tags are underscore-separated (`is-7_0_2`),
so the pin stores `7_0_2` and `renovate.json5` only supplies the matching
`extractVersion` and `versioning` for that dependency.

Three dependencies are tracked but need manual review, so `renovate.json5`
disables automerge for them:

- `H.NotifyIcon.WinUI` and `Microsoft.WindowsAppSDK` in
  `src/WindowsCompanion.App/WindowsCompanion.App.csproj`: both changes affect
  runtime prerequisites (the installed Windows App Runtime, or the minimum .NET
  version) and need manual verification before merging.
- `jrsoftware/issrc` (Inno Setup): the installer builds the shipped setup
  packages, so a compiler bump should be reviewed and, ideally, smoke-tested.
  The download is not pinned by SHA-256 because Renovate cannot compute a
  checksum for a new release. Instead the workflow requires a valid Authenticode
  signature from the pinned publisher (`inno_signer`) and logs the SHA-256 of
  the downloaded installer. If upstream ever signs under a different name the
  release job fails; verify the new publisher before updating `inno_signer`.
