# Implementation Plan: Mocked Home Assistant End-to-End Testing

**Branch**: `010-mocked-ha-e2e-testing` | **Date**: 2026-08-10 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/010-mocked-ha-e2e-testing/spec.md`

## Summary

The implementation adds a reusable loopback Kestrel Home Assistant test server
with real HTTP and WebSocket transport and exercises the production OAuth,
registration, sensor, notification, reconnect, and persistence stack. A separate
native UI suite launches the unpackaged WinUI app through a debug-only isolated
test composition and drives stable accessibility identifiers with UI Automation.
Headless end-to-end tests run on hosted Windows CI and the UI suite is compiled
there; rendered UI, notification, and tray tests run sequentially on an
explicitly interactive self-hosted Windows runner for trusted changes.

## Technical Context

**Language/Version**: C# / .NET 10

**Primary Dependencies**: ASP.NET Core shared framework for the loopback test
server; xUnit and Microsoft.NET.Test.Sdk matching the existing test stack; FlaUI
Core/UIA3 for native UI automation; existing Windows App SDK 2.3 application
dependencies

**Storage**: Scenario-scoped temporary settings directories plus uniquely scoped
Windows Credential Locker entries for restart tests; no committed or retained
secrets

**Testing**: Existing Core unit tests; new protocol/application end-to-end xUnit
tests; new sequential Windows UI xUnit smoke tests; sanitized TRX, interaction
logs, application logs, and accessibility trees on failure

**Target Platform**: Windows 10 build 19041+ and Windows 11 for the application;
Windows GitHub-hosted runners for build/Core/end-to-end checks; interactive x64
Windows self-hosted runner for UI automation

**Project Type**: Native desktop application with platform-independent Core,
Windows shell, reusable test server, and separate end-to-end/UI test projects

**Performance Goals**: Fake server starts in under 1 second; one focused
end-to-end scenario finishes within 2 minutes; hosted end-to-end suite and
interactive UI smoke suite each finish within 15 minutes

**Constraints**: Real loopback sockets are required because the application uses
standard `HttpClient` and `ClientWebSocket` across process boundaries; UI
automation requires an unlocked interactive desktop; tests must never use real
credentials or personal endpoints; UI tests run sequentially because the app
owns singleton OS resources

**Scale/Scope**: One protocol-accurate fake for the companion's used HA surface;
24 journey/foundation tests passed in prior validation, and seven rendered UI
scenarios plus the toast-capability scenario passed separately. This is not a
general Home Assistant emulator or visual-regression suite.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

- **Native Windows Experience First — PASS**: UI automation exercises the actual
  unpackaged WinUI application; no embedded web UI is introduced.
- **Security & Privacy of Credentials — PASS**: All values are synthetic,
  scenario-scoped, sanitized from diagnostics, and persisted only through
  isolated temporary settings and Credential Locker scopes.
- **Evidence-Driven Development — PASS**: Specification, research, contracts,
  plan, quickstart, and tasks precede implementation; protocol behavior is
  recorded explicitly.
- **Testable, Layered Architecture — PASS**: The fake server and test harness are
  test-only projects. Production changes are limited to dependency seams,
  accessibility metadata, and debug-only test composition; Core remains UI-free.
- **Resilience & Observability — PASS**: Fault injection covers network,
  authentication, webhook, and WebSocket recovery. Failure artifacts are
  structured and sanitized.
- **Additional Constraints — PASS**: The design stays on .NET 10, uses the
  documented `mobile_app` and WebSocket protocol, prefers the first-party
  ASP.NET Core shared framework, and justifies FlaUI as the only added
  third-party test dependency.

**Post-design re-check**: PASS. Contracts keep production and test-only behavior
separate, use real protocol boundaries, and do not weaken TLS or credential
storage in shipped builds.

## Project Structure

### Documentation (this feature)

```text
specs/010-mocked-ha-e2e-testing/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── fake-ha-protocol.md
│   ├── test-host.md
│   └── ui-automation.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── WindowsCompanion.Core/
│   └── [existing production contracts and logic]
└── WindowsCompanion.App/
    ├── App.xaml.cs
    ├── AppController.cs
    ├── MainWindow.xaml
    └── Services/
        ├── OAuthLoginService.cs
        ├── WindowsSecretStore.cs
        └── [small composition/URI-launch seams]

tests/
├── WindowsCompanion.Core.Tests/
├── WindowsCompanion.Testing/
│   ├── FakeHomeAssistantServer.cs
│   ├── FakeHaScenario.cs
│   ├── FakeHaState.cs
│   ├── FakeHaFaults.cs
│   ├── FakeHaInteractionLog.cs
│   └── FakeHaWebSocketSession.cs
├── WindowsCompanion.E2E.Tests/
│   ├── Fixtures/
│   ├── ConnectionJourneyTests.cs
│   ├── SensorSyncJourneyTests.cs
│   ├── NotificationJourneyTests.cs
│   └── RecoveryJourneyTests.cs
└── WindowsCompanion.UI.Tests/
    ├── Fixtures/
    ├── Pages/
    ├── ConnectUiTests.cs
    ├── StatusUiTests.cs
    ├── SensorUiTests.cs
    └── FailureUiTests.cs

scripts/
└── test.ps1

.github/workflows/
├── ci.yml
└── ui-tests.yml
```

**Structure Decision**: Keep all protocol simulation and automation harness code
under `tests/`. Add narrow constructor interfaces and accessibility metadata to
the app rather than moving platform behavior into Core. The end-to-end suite
references the app composition without creating a window; the UI suite launches
the built executable and owns one isolated scenario at a time.

## Validation and Delivery Reality

- Hosted Windows CI runs headless E2E tests and compiles, but does not execute,
  rendered UI tests. It produces x64 and ARM64 Release application builds.
- Rendered UI tests execute sequentially on the trusted interactive x64
  self-hosted runner. The tray smoke test skips with a capability reason when UIA
  cannot expose the tray affordance; native toast capability passed previously.
- Failures preserve sanitized TRX, scenario metadata, app logs, fake-server
  interactions, application logs, and UI accessibility evidence where relevant.
- Prior runtime validation passed 24 journey/foundation tests and seven rendered
  UI scenarios. No reliable elapsed-time measurement was retained.
- Further runtime test execution is CI-only by user request. Remaining validation
  and timing goals must therefore be established by CI; this reconciliation is
  limited to static Markdown and diff checks.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| Third-party FlaUI test dependency | Stable native UIA3 discovery, interaction, retries, and accessibility inspection | Raw COM UI Automation duplicates substantial mature plumbing; WinAppDriver is an unmaintained external service and adds more failure modes |
| Interactive self-hosted UI runner | WinUI rendering and UI Automation require an unlocked interactive desktop | Hosted service sessions can build and run headless tests but cannot reliably render or inspect the native desktop |
