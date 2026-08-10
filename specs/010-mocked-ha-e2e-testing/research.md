# Research: Mocked Home Assistant End-to-End Testing

## Decision 1: Loopback Home Assistant test server

**Decision**: Build a reusable test-only ASP.NET Core Kestrel server bound to
`127.0.0.1` on an OS-assigned port.

**Rationale**: The companion must be reached from a separately launched process
through real `HttpClient` and `ClientWebSocket` connections. Kestrel supports
ephemeral ports, HTTP form and JSON endpoints, WebSocket server-push, cancellation,
and clean async lifetime management without adding a runtime package to production.

**Alternatives considered**:

- `TestServer`: rejected because it has no real TCP port and cannot be reached by
  the launched companion process.
- WireMock.Net: rejected because its HTTP stubbing is useful but its WebSocket
  model does not naturally implement HA's server-initiated authentication and
  test-controlled push sequence.
- `HttpListener`: rejected because it requires manual routing, serialization, and
  WebSocket lifetime code that Kestrel already provides.
- Containerized Home Assistant: retained only as possible future scheduled
  compatibility validation; rejected for required tests because startup,
  provisioning, fault injection, and Windows-hosted CI are slower and less
  deterministic.

## Decision 2: Protocol fidelity boundary

**Decision**: Model only the surfaces used by the companion:

- `GET /auth/authorize`
- `POST /auth/token`
- `GET /api/`
- `GET /api/config`
- `POST /api/mobile_app/registrations`
- `POST /api/webhook/{webhookId}`
- WebSocket `/api/websocket`

The server validates request shape and ordering, records sanitized interactions,
and exposes typed scenario/fault controls to tests.

**Rationale**: This gives high fidelity for the application's contractual surface
without pretending to reproduce Home Assistant internals. Golden payload tests
continue to guard exact serialization, while end-to-end tests guard real transport
and orchestration.

**Alternatives considered**:

- General HA emulator: rejected as unbounded and likely to drift.
- Loose status-only stubs: rejected because they would miss registration,
  webhook rejection, refresh, reconnect, and local-push behavior.

## Decision 3: Application end-to-end composition

**Decision**: Use narrow injectable dependencies in `AppController` and
`OAuthLoginService` while preserving their production defaults. End-to-end tests
instantiate the real controller stack with real HTTP/WebSocket clients, temporary
settings, uniquely scoped Credential Locker storage, deterministic platform
sources, and a test URI launcher that completes the fake authorization redirect.

**Rationale**: The current `AppController` constructs every Windows service
internally, preventing safe isolated orchestration tests. Constructor seams allow
the production composition to remain unchanged while tests exercise the same
controller, session, registration, connection, and sensor logic.

**Alternatives considered**:

- Test only Core classes independently: rejected because that remains integration
  or unit coverage, not an assembled application journey.
- Launch the full UI for every end-to-end scenario: rejected because it makes all
  protocol coverage dependent on an interactive desktop and slows diagnosis.
- Add production HTTP shortcuts for tests: rejected because bypassing real
  transport defeats the purpose of the suite.

## Decision 4: Native UI automation

**Decision**: Use FlaUI 5 UIA3 from a `net10.0-windows` xUnit project. Add stable
`AutomationProperties.AutomationId` values to tested controls and page objects
that locate elements only by those identifiers and control types.

**Rationale**: FlaUI directly wraps Windows UI Automation, launches unpackaged
executables by path, stays in the repository's C# test stack, and needs no
sidecar service or Developer Mode. Its .NET 8+ target is compatible with .NET 10.

**Alternatives considered**:

- WinAppDriver/Appium: rejected because the underlying WinAppDriver binary has
  not had a release since 2020, is closed source, requires a service, and has no
  explicit modern WinUI 3 support.
- Raw UI Automation COM: rejected because it duplicates element lookup, retry,
  interaction, and capture plumbing already provided by FlaUI.
- Playwright/Selenium: rejected because they automate web content, not native
  WinUI controls.

## Decision 5: Test-only executable composition

**Decision**: Use a debug-only launch contract that selects an isolated
settings path, unique Credential Locker resource, unique instance identity, and
non-interactive URI launcher. Reject non-loopback server addresses in this mode.
Release builds do not contain or recognize this composition.

**Rationale**: UI tests need restart persistence and deterministic OAuth without
opening a user's browser or touching their normal app data. Compile-time exclusion
prevents shipped binaries from exposing a test credential or browser path.

**Alternatives considered**:

- Environment variables in production: rejected because they silently alter
  shipped behavior and are easy to inherit accidentally.
- Plaintext test secret files: rejected because using the same Credential Locker
  abstraction better preserves security assumptions and tests restart behavior.
- Automating a real browser login: rejected because it is slow, brittle, and
  would require credentials.

## Decision 6: Continuous integration split

**Decision**:

- Run Core and headless end-to-end suites as required checks on
  `windows-latest`.
- Build the UI test project on hosted runners.
- Run rendered UI smoke tests sequentially on a runner labelled
  `self-hosted`, `windows`, `x64`, `interactive`, triggered for trusted `main`
  pushes or manually. Do not execute untrusted fork code on that runner.

**Rationale**: Native WinUI rendering and UI Automation require an
unlocked interactive desktop. Hosted runners remain appropriate for compilation
and headless loopback protocol tests. The split avoids presenting a non-executing
or flaky hosted UI job as coverage.

**Alternatives considered**:

- Run UI tests on hosted runners: rejected because availability of an interactive
  desktop is not a reliable runner contract.
- Make self-hosted UI tests run on every public fork PR: rejected for runner
  security and persistent-host contamination.
- Omit CI UI execution entirely: rejected because it would leave the suite
  developer-only and prone to decay.

## Decision 7: Diagnostics and parallelism

**Decision**: Each scenario owns its port, server state, settings directory,
credential resource, and interaction log. Headless end-to-end tests may run in
parallel when isolation is proven. UI tests use an xUnit collection with
parallelization disabled and capture sanitized interaction JSON, app logs, TRX,
and a sanitized accessibility-tree snapshot on failure.

**Rationale**: OS-level singleton resources and the interactive desktop make
parallel UI execution unsafe. Protocol tests can safely parallelize with strict
scenario ownership. Explicit wait handles and interaction waiters replace sleep
polling.

**Alternatives considered**:

- Global shared fake server: rejected because state and fault leakage make
  failures order-dependent.
- Fixed ports: rejected because concurrent runs and abandoned processes collide.
- Broad retries/sleeps: rejected because they hide races and increase suite time.

## Implementation Findings

- The fake is implemented with ASP.NET Core Kestrel on an OS-assigned loopback
  port and is shared by headless journeys and process-launched UI scenarios.
- Prior validation passed 24 journey/foundation tests. Interactive validation
  separately passed seven rendered UI scenarios and the native-toast capability
  scenario. No authoritative elapsed-time measurement was retained.
- Windows UI Automation does not expose a usable tray affordance in every
  interactive environment. The tray scenario therefore probes capability and
  skips with the unsupported reason instead of passing or using coordinates.
- Hosted CI runs E2E and compiles UI automation while producing x64 and ARM64
  application builds. Rendered UI remains an interactive x64 self-hosted concern.
  The checked-in interactive workflow currently runs on trusted pushes to `main`
  and manual dispatch, not release branches.
- Failure evidence includes sanitized interaction history, app logs, scenario
  metadata, TRX, application logs, and UI accessibility data where relevant.
- All further runtime test execution is CI-only by user request. These findings
  were reconciled with static Markdown and diff inspection.

## Sources

- ASP.NET Core integration testing:
  https://learn.microsoft.com/aspnet/core/test/integration-tests
- ASP.NET Core WebSockets:
  https://learn.microsoft.com/aspnet/core/fundamentals/websockets
- WinAppDriver repository and support statement:
  https://github.com/microsoft/WinAppDriver
- FlaUI repository and UIA3 documentation:
  https://github.com/FlaUI/FlaUI
- GitHub-hosted runner reference:
  https://docs.github.com/actions/reference/runners/github-hosted-runners
- GitHub self-hosted runner security:
  https://docs.github.com/actions/hosting-your-own-runners/managing-self-hosted-runners/about-self-hosted-runners
- Windows App SDK unpackaged deployment:
  https://learn.microsoft.com/windows/apps/windows-app-sdk/deploy-unpackaged-apps
