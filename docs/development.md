# Developer guide

Windows Companion is a Windows-only .NET 10 and WinUI 3 application. The
platform-independent core remains separately testable, but building and running
the complete application requires Windows.

## Prerequisites

- Windows 10 or 11.
- .NET 10 SDK.
- The Windows SDK matching the app target framework.
- Windows App Runtime 2.3.
- Visual Studio 2022, Rider, or another editor that supports WinUI 3.

## Build and run

Use the repository script to build and launch from source:

```powershell
.\scripts\run.ps1
.\scripts\run.ps1 -Configuration Release
.\scripts\run.ps1 -NoLaunch
```

Do not use `dotnet run`. For this unpackaged WinUI project it can resolve the
runtime from the application output directory and show a misleading missing
runtime dialog. The script pins the platform, launches the output it just built,
and handles a running source-built process that would lock build outputs.

Build the supported architectures directly when validating release output:

```powershell
dotnet build src\WindowsCompanion.App\WindowsCompanion.App.csproj `
  -c Release -p:Platform=x64 -r win-x64 --nologo

dotnet build src\WindowsCompanion.App\WindowsCompanion.App.csproj `
  -c Release -p:Platform=ARM64 -r win-arm64 --nologo
```

## Tests

```powershell
.\scripts\test.ps1
.\scripts\test.ps1 -Coverage
.\scripts\test.ps1 -EndToEnd
.\scripts\test.ps1 -Ui
```

Coverage applies to `WindowsCompanion.Core`; the gates are 85% line and 70%
branch. End-to-end tests use a loopback Home Assistant substitute and synthetic
credentials while exercising the real OAuth, REST, webhook, WebSocket,
persistence, and connection stack.

Native UI tests require an unlocked interactive Windows desktop. Hosted CI
compiles them, while trusted main builds and manual runs execute them sequentially
on the self-hosted interactive runner. See the
[end-to-end test quickstart](../specs/010-mocked-ha-e2e-testing/quickstart.md) for
filters, diagnostics, and runner constraints.

Run a focused xUnit class or test with:

```powershell
dotnet test tests\WindowsCompanion.Core.Tests\WindowsCompanion.Core.Tests.csproj `
  -c Release --filter "FullyQualifiedName~RouteValidatorTests"
```

## Architecture

| Project | Responsibility |
| --- | --- |
| `src\WindowsCompanion.Core` | Home Assistant protocol, routing and connection state, persistence models, lifecycle decisions, and sensor logic without WinUI or Windows API dependencies. |
| `src\WindowsCompanion.App` | WinUI and tray shell, Windows API sources, Credential Locker, OAuth loopback listener, toasts, logging, and startup registration. |
| `tests\WindowsCompanion.Core.Tests` | xUnit coverage for platform-independent behavior. |
| `tests\WindowsCompanion.App.Tests` | Tests for application-layer behavior. |
| `brand\` | Vector masters and generation scripts for shipped artwork. |

`AppController` is the composition root. `ConnectionLifecycle` serializes user
actions, route changes, teardown, and rebuilds. `ConnectionManager` owns the
long-lived WebSocket and sensor synchronization. Avoid introducing a parallel
owner for connection mutation.

`SensorCatalog` owns sensor-source lifetimes. A disabled source must perform no
collection, polling, OS event handling, or transmission. Keep deterministic state
formatting and classification in Core; keep Windows enumeration and hooks in App.

## Persistence and secrets

Non-secret configuration is stored under:

```text
%LOCALAPPDATA%\WindowsCompanion\settings.json
```

Refresh tokens, webhook IDs, and cloudhook URLs are stored through
`ISecretStore` in Windows Credential Locker. Lifecycle transitions use a separate
local journal so an interrupted shutdown write cannot damage settings.

Preserve persistence migrations. Device IDs and registered-sensor metadata prevent
upgrades from creating duplicate Home Assistant devices or leaving removed sensor
entities active.

Never put credentials, Home Assistant URLs, network identifiers, or real sensor
values in source, fixtures, tests, or logs.

## Home Assistant integration

The app uses the built-in `mobile_app` integration:

- OAuth and REST for authentication and device registration.
- Webhooks for sensor registration and state updates.
- `mobile_app/push_notification_channel` over WebSocket for local notifications.

Read [protocol and platform notes](protocol-notes.md) before changing these flows.
The original contracts are under
[`specs/001-ha-companion-foundation/contracts/`](../specs/001-ha-companion-foundation/contracts/).

## Specifications and contribution workflow

Consequential user-visible behavior and verified protocol discoveries belong in
[`specs/`](../specs/). The project uses
[GitHub Spec Kit](https://github.com/github/spec-kit) when a feature's uncertainty
justifies the full workflow.

See [CONTRIBUTING.md](../CONTRIBUTING.md) for commit, pull request, security,
dependency, and repository conventions.
