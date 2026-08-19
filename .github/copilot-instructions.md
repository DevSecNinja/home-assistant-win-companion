# Copilot instructions

## Build, run, test, and lint

This is a Windows-only .NET 10 / WinUI 3 application. Full app work requires
Windows, the .NET 10 SDK, the matching Windows SDK, and Windows App Runtime 2.3.

Use the repository script to build and launch from source:

```powershell
.\scripts\run.ps1
.\scripts\run.ps1 -Configuration Release
.\scripts\run.ps1 -NoLaunch
```

Do not use `dotnet run`. For this unpackaged WinUI project it resolves the runtime
from the app output directory and can show a misleading missing-runtime dialog.
`run.ps1` also handles a running source-built app that would lock build outputs.

Build the app for the supported architectures:

```powershell
dotnet build src\WindowsCompanion.App\WindowsCompanion.App.csproj -c Release -p:Platform=x64 -r win-x64 --nologo
dotnet build src\WindowsCompanion.App\WindowsCompanion.App.csproj -c Release -p:Platform=ARM64 -r win-arm64 --nologo
```

Run tests and coverage:

```powershell
.\scripts\test.ps1
.\scripts\test.ps1 -Coverage
```

Coverage applies to `WindowsCompanion.Core`; the gates are 85% line and 70% branch.
Run a class or single xUnit test with:

```powershell
dotnet test --project tests\WindowsCompanion.Core.Tests\WindowsCompanion.Core.Tests.csproj -c Release -- --filter-query "/*/*/RouteValidatorTests/*"
dotnet test --project tests\WindowsCompanion.Core.Tests\WindowsCompanion.Core.Tests.csproj -c Release -- --filter-query "/*/*/RouteValidatorTests/One_url_is_the_default_configuration"
```

CI is defined in `.github/workflows/ci.yml` and builds x64 and ARM64, uploads
unsigned artifacts, and runs Core tests. Linting is delegated by
`.github/workflows/lint.yml` to the pinned shared workflow. Tool versions are in
`.mise.toml`; after `mise install`, the enabled checks are `yamllint`,
`actionlint`, `gitleaks`, `checkov`, `trivy`, and `zizmor`.

## Architecture

- `WindowsCompanion.Core` contains platform-independent Home Assistant protocol,
  connection/routing state machines, persistence models, lifecycle decisions, and
  sensor logic. It must remain unit-testable without WinUI or Windows APIs.
- `WindowsCompanion.App` is the thin Windows shell: WinUI/tray UI, P/Invoke and
  `SystemEvents` sources, Credential Locker, OAuth loopback listener, toasts,
  logging, and startup registration.
- `AppController` is the composition root. It loads the session, runs OAuth/device
  registration, selects a route, builds REST/WebSocket clients and the sensor
  catalog, owns reconnect/failover, and maps notifications to Windows toasts.
- `SessionStore` deliberately splits persistence: non-secrets are in
  `%LOCALAPPDATA%\WindowsCompanion\settings.json`; refresh tokens, webhook IDs, and
  cloudhook URLs use `ISecretStore`/Windows Credential Locker. Preserve migration
  paths so upgrades do not register duplicate Home Assistant devices.
- `ConnectionManager` owns the long-lived WebSocket and periodic sensor sync.
  `ConnectionLifecycle` serializes user actions, route switches, teardown, and
  rebuilds; use its leases rather than introducing parallel connection mutation.
- One Home Assistant URL is the default. Separate internal/external routing is an
  explicit opt-in. `RouteValidator` proves routes belong to the same registration,
  `RouteSelector` orders candidates from local network trust, and
  `RouteSupervisor` applies probing, cooldown, and failover without re-registering.
- Home Assistant communication uses the built-in `mobile_app` integration:
  OAuth/REST for auth and registration, webhook commands for sensors, and
  `mobile_app/push_notification_channel` over WebSocket for local notifications.

## Sensor model

- A sensor source implements `ISensorSource` and may expose several related
  `SensorDefinition`s. Keep deterministic formatting, classification, and state
  transitions in Core; keep Windows enumeration/hooks in App services.
- `SensorCatalog` owns source lifetimes. It calls `Start` when the first sensor from
  a source is enabled and `Stop` when the last is disabled. Disabled must mean zero
  collection, polling, OS hooks, and transmission—not merely filtering output.
- Sources must return only requested IDs. Expensive sources implement
  `IRefreshableSensorSource`; shared sensors should capture one snapshot per read.
  Use `SensorPollLoop` for cancellable single-flight polling and `ChangeGate<T>` or
  equivalent comparison to avoid unchanged immediate pushes.
- `onChanged` requests a full immediate sensor sync, so do not invoke it for every
  poll or noisy OS event unless that bandwidth is intentional and disclosed in the
  sensor's `ResourceUsage` text.
- Local previews must not leak sensitive data before opt-in. Follow
  `SensorPreviewGate` and the network/Wi-Fi sources when adding sensitive sensors.
- Sensor IDs are stable contracts. Home Assistant states are limited to 255
  characters; `SensorCatalog` truncates string states, but design concise states
  and put diagnostics in attributes.
- `SensorSyncService` serializes periodic and change-driven syncs. Enable/disable
  uses `register_sensor` because `update_sensor_states` ignores the `disabled`
  flag. Keep `RegisteredSensors` persisted: it allows removed sensors to be
  re-registered as disabled and retired rather than left stale in Home Assistant.

## Reliability, privacy, and protocol conventions

- Never put refresh tokens, webhook IDs, cloudhook URLs, Home Assistant URLs, Wi-Fi
  identifiers, or other sensitive sensor values in logs, tests, fixtures, or
  source. Use fake `.example`/`.local` values in tests.
- Do not bypass TLS validation. External routes require HTTPS. Route probing must
  reject captive portals, unsafe redirects, and unrelated hosts before sending
  credentials.
- Windows lifecycle delivery is best effort. Keep Windows message/P/Invoke mapping
  thin, with deduplication, journaling, timeouts, and recovery in Core. Never block
  or veto shutdown/suspend.
- Background sources must be idempotent across repeated start/stop, unregister
  events/hooks, cancel pending work, and drop callbacks after stop. Avoid broad
  catches except at explicit best-effort boundaries that already log or document
  the failure.
- Async lifecycle tests that use background loops belong to
  `AsyncLifecycleCollection`; it disables parallelization to prevent CI
  thread-pool starvation. Prefer `TaskCompletionSource` handshakes and fake clocks
  over short `Task.Delay` polling/timeouts.
- Home Assistant golden payloads live under
  `tests\WindowsCompanion.Core.Tests\Golden`. Change them only for an intentional,
  evidence-backed protocol change and update the relevant contract/spec.

## Feature development workflow

- New features must follow the speckit pipeline before implementation:
  specify → clarify → plan → tasks → analyze → implement.
- Run `speckit-analyze` after `speckit-tasks` and before implementation to catch
  inconsistencies early.
- Run `speckit-converge` after implementation to verify completeness.
- Feature artifacts (spec.md, plan.md, tasks.md) live under `specs/<feature-name>/`.
- Do not skip steps. If a feature was started without speckit, first produce the
  missing artifacts (`speckit-specify`, then `speckit-clarify`, then `speckit-plan`,
  then `speckit-tasks`) before running `speckit-converge` to reconcile the codebase.

## Repository workflow

- Record consequential user-visible behavior and Home Assistant/Windows protocol
  discoveries under `specs\`; shipped behavior and verified upstream behavior take
  precedence over stale plans, which must be corrected.
- Keep direct GitHub Actions and reusable workflows pinned to full commit digests.
- Use Conventional Commit and PR titles. All commits pushed to this repository
  must be cryptographically signed; verify GitHub reports them as verified.
- Keep changes focused and describe user-visible behavior, privacy implications,
  and Home Assistant protocol assumptions in the PR.
