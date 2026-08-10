# Tasks: Mocked Home Assistant End-to-End Testing

**Input**: Design documents from `specs/010-mocked-ha-e2e-testing/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: This feature is test infrastructure, so executable contract, end-to-end,
and UI tests are mandatory deliverables rather than optional follow-up work.

## Phase 1: Setup

**Purpose**: Create independently buildable test projects and solution wiring.

- [X] T001 Create `tests/WindowsCompanion.Testing/WindowsCompanion.Testing.csproj` targeting .NET 10 with the ASP.NET Core shared framework
- [X] T002 [P] Create `tests/WindowsCompanion.E2E.Tests/WindowsCompanion.E2E.Tests.csproj` with xUnit references and project references to App, Core, and Testing
- [X] T003 [P] Create `tests/WindowsCompanion.UI.Tests/WindowsCompanion.UI.Tests.csproj` targeting Windows with xUnit, FlaUI Core/UIA3, and Testing references
- [X] T004 Add all three test projects and supported platform mappings to `WindowsCompanion.sln`

---

## Phase 2: Foundational

**Purpose**: Establish production-safe composition seams and the reusable fake HA
protocol before story-specific tests are added.

**Critical**: User-story work begins only after this phase passes targeted tests.

- [X] T005 Add an injectable URI-launch contract and production shell launcher in `src/WindowsCompanion.App/Services/IUriLauncher.cs` and `src/WindowsCompanion.App/Services/ShellUriLauncher.cs`
- [X] T006 Update `src/WindowsCompanion.App/Services/OAuthLoginService.cs` to use the URI-launch contract while preserving default-browser production behavior
- [X] T007 Add explicit Credential Locker resource scoping and cleanup support without changing the production default in `src/WindowsCompanion.App/Services/WindowsSecretStore.cs`
- [X] T008 Define owned injectable app dependencies and preserve the parameterless production composition in `src/WindowsCompanion.App/AppControllerDependencies.cs` and `src/WindowsCompanion.App/AppController.cs`
- [X] T009 Add deterministic injectable platform/network/sensor/notification test seams needed by the controller in `src/WindowsCompanion.App/AppControllerDependencies.cs` and `src/WindowsCompanion.App/AppController.cs`
- [X] T010 Add tests for production defaults, URI launching, dependency ownership, and credential scoping in `tests/WindowsCompanion.E2E.Tests/CompositionContractTests.cs`
- [X] T011 [P] Implement `FakeHaScenario`, lifecycle, synthetic identifiers, and state in `tests/WindowsCompanion.Testing/FakeHaScenario.cs` and `tests/WindowsCompanion.Testing/FakeHaState.cs`
- [X] T012 [P] Implement typed faults and cancellable release handshakes in `tests/WindowsCompanion.Testing/FakeHaFaults.cs`
- [X] T013 [P] Implement sanitized interaction recording and predicate-based async waiters in `tests/WindowsCompanion.Testing/FakeHaInteraction.cs` and `tests/WindowsCompanion.Testing/FakeHaInteractionLog.cs`
- [X] T014 Implement the loopback Kestrel host and OAuth/REST/webhook endpoints from `contracts/fake-ha-protocol.md` in `tests/WindowsCompanion.Testing/FakeHomeAssistantServer.cs`
- [X] T015 Implement the HA WebSocket authentication, push subscription, notification, confirmation, close, and reconnect state machine in `tests/WindowsCompanion.Testing/FakeHaWebSocketSession.cs`
- [X] T016 Add fake-server contract tests for healthy and faulted endpoint/message sequences in `tests/WindowsCompanion.E2E.Tests/FakeHomeAssistantContractTests.cs`

**Checkpoint**: The fake server is reusable, real production composition still
works, and no test-only behavior is present in Release executable builds.

---

## Phase 3: User Story 1 - Validate Complete Companion Journeys (Priority: P1)

**Goal**: Exercise the assembled controller, real HA clients, persistence,
registration, sensor sync, notifications, and recovery against the loopback fake.

**Independent Test**: Run `ConnectionJourneyTests`; one isolated scenario signs
in, registers, syncs, receives a notification, restarts without duplicate
registration, and records only sanitized interactions.

### Tests for User Story 1

- [X] T017 [P] [US1] Create isolated settings, Credential Locker, controller, deterministic platform-source, and cleanup fixtures in `tests/WindowsCompanion.E2E.Tests/Fixtures/CompanionJourneyFixture.cs`
- [X] T018 [P] [US1] Create a non-browser redirect-following test URI launcher in `tests/WindowsCompanion.E2E.Tests/Fixtures/TestUriLauncher.cs`
- [X] T019 [US1] Add first-time OAuth, device registration, initial sensor sync, disconnect/reconnect, and persisted restart tests in `tests/WindowsCompanion.E2E.Tests/ConnectionJourneyTests.cs`
- [X] T020 [P] [US1] Add enabled/disabled sensor registration and state synchronization tests in `tests/WindowsCompanion.E2E.Tests/SensorSyncJourneyTests.cs`
- [X] T021 [P] [US1] Add WebSocket notification delivery and confirmation tests with a deterministic notification sink in `tests/WindowsCompanion.E2E.Tests/NotificationJourneyTests.cs`
- [X] T022 [P] [US1] Add authentication rejection, operation rejection, disconnect, reconnect, and recovery tests in `tests/WindowsCompanion.E2E.Tests/RecoveryJourneyTests.cs`
- [X] T023 [US1] Add a ten-run repeatability test category and enforce scenario isolation/cleanup in `tests/WindowsCompanion.E2E.Tests/RepeatabilityTests.cs`

**Checkpoint**: User Story 1 is complete and runnable without a real HA instance,
browser, or application window.

---

## Phase 4: User Story 2 - Validate Critical Windows UI Journeys (Priority: P1)

**Goal**: Launch and drive the real unpackaged WinUI application against the fake
server using stable accessibility identifiers.

**Independent Test**: On an unlocked interactive desktop, run `ConnectUiTests`;
the app launches with an isolated profile, signs in, displays Connected, and exits
without touching normal user state.

### Tests and implementation for User Story 2

- [X] T024 [P] [US2] Add stable semantic automation IDs for tested static and dynamic controls in `src/WindowsCompanion.App/MainWindow.xaml` and `src/WindowsCompanion.App/MainWindow.xaml.cs`
- [X] T025 [P] [US2] Add debug-only validated test-profile parsing and composition in `src/WindowsCompanion.App/TestAppLaunchOptions.cs` and `src/WindowsCompanion.App/App.xaml.cs`
- [X] T026 [US2] Add tests proving unsafe/non-loopback test profiles are rejected and Release composition excludes test mode in `tests/WindowsCompanion.E2E.Tests/TestAppLaunchOptionsTests.cs`
- [X] T027 [US2] Implement sequential app launch, exact-process shutdown, isolated profile, fake server, and FlaUI lifetime management in `tests/WindowsCompanion.UI.Tests/Fixtures/UiScenarioFixture.cs`
- [X] T028 [P] [US2] Implement stable-ID page objects and state-based waits in `tests/WindowsCompanion.UI.Tests/Pages/ConnectPage.cs`, `tests/WindowsCompanion.UI.Tests/Pages/StatusPage.cs`, and `tests/WindowsCompanion.UI.Tests/Pages/SensorsPage.cs`
- [X] T029 [US2] Add clean launch, URL validation, sign-in, and connected-status tests in `tests/WindowsCompanion.UI.Tests/ConnectUiTests.cs`
- [X] T030 [P] [US2] Add sensor setting, update-now, disconnect, reconnect, and restart UI tests in `tests/WindowsCompanion.UI.Tests/StatusUiTests.cs` and `tests/WindowsCompanion.UI.Tests/SensorUiTests.cs`
- [X] T031 [P] [US2] Add remove-server confirmation and authentication/connectivity failure/retry UI tests in `tests/WindowsCompanion.UI.Tests/FailureUiTests.cs`
- [X] T032 [US2] Add an `AsyncLifecycleCollection` equivalent that disables UI test parallelization in `tests/WindowsCompanion.UI.Tests/UiTestCollection.cs`
- [X] T033 [P] [US2] Add explicit interactive-shell/notification capability probing and environment-gated native toast delivery tests in `tests/WindowsCompanion.UI.Tests/NotificationUiTests.cs`
- [X] T034 [P] [US2] Add environment-gated tray hide/restore automation using stable shell identities in `tests/WindowsCompanion.UI.Tests/TrayUiTests.cs`

**Checkpoint**: User Story 2 is complete on a supported interactive Windows
desktop and all test-owned OS/application state is removed afterward.

---

## Phase 5: User Story 3 - Diagnose Automated Failures (Priority: P2)

**Goal**: Preserve actionable, sanitized evidence for failed end-to-end and UI
scenarios.

**Independent Test**: Induce one protocol and one UI assertion failure and verify
the evidence contains the scenario/step but none of the scenario's raw secret
values.

- [X] T035 [P] [US3] Implement JSON interaction export and secret/sensitive-value redaction tests in `tests/WindowsCompanion.Testing/FakeHaEvidenceWriter.cs` and `tests/WindowsCompanion.E2E.Tests/EvidenceRedactionTests.cs`
- [X] T036 [P] [US3] Implement isolated app-log collection and scenario metadata in `tests/WindowsCompanion.E2E.Tests/Fixtures/FailureEvidence.cs`
- [X] T037 [US3] Add screenshot and sanitized accessibility-tree capture on UI failure in `tests/WindowsCompanion.UI.Tests/Fixtures/UiFailureEvidence.cs`
- [X] T038 [US3] Add induced-failure evidence contract tests in `tests/WindowsCompanion.UI.Tests/FailureEvidenceTests.cs` and `tests/WindowsCompanion.E2E.Tests/EvidenceContractTests.cs`

**Checkpoint**: Failed scenarios leave useful evidence and evidence-contract tests
fail if sensitive scenario values are present.

---

## Phase 6: User Story 4 - Run Suites Locally and in CI (Priority: P2)

**Goal**: Make focused/full test execution repeatable for contributors and CI.

**Independent Test**: Run each documented command from a clean supported
environment and confirm the intended project set and artifacts.

- [X] T039 [P] [US4] Add `-EndToEnd`, `-Ui`, result-directory, and focused-filter support while preserving existing defaults/coverage behavior in `scripts/test.ps1`
- [X] T040 [P] [US4] Add hosted restore/build/end-to-end execution, UI-project compilation, summaries, and failure artifact upload in `.github/workflows/ci.yml`
- [X] T041 [P] [US4] Add trusted main/release/manual sequential UI smoke execution on `[self-hosted, windows, x64, interactive]` in `.github/workflows/ui-tests.yml`
- [X] T042 [US4] Document suite scope, local commands, interactive-runner setup, fork security, and limitations in `README.md` and `specs/010-mocked-ha-e2e-testing/quickstart.md`
- [X] T043 [US4] Verify workflow action pins, script selectors, project paths, and artifact redaction with repository lint/security tooling in `.github/workflows/ci.yml` and `.github/workflows/ui-tests.yml`

**Checkpoint**: Contributors and CI can execute the appropriate suites without a
real HA instance, and UI execution is not misrepresented on hosted runners.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [X] T044 [P] Add XML documentation for public test-harness contracts and remove duplicated fixture helpers across `tests/WindowsCompanion.Testing/` and new test projects
- [ ] T045 Run targeted Core, fake-server contract, end-to-end, and interactive UI tests using `scripts/test.ps1`, then run ten consecutive new-suite executions required by `spec.md`
- [X] T046 Build Release x64 and ARM64 app targets and confirm the Release binary has no debug test-launch composition in `src/WindowsCompanion.App/WindowsCompanion.App.csproj`
- [X] T047 Reconcile implementation discoveries, supported scenarios, measured timings, and known limitations in `specs/010-mocked-ha-e2e-testing/spec.md`, `plan.md`, `research.md`, and `quickstart.md`

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup and blocks all user stories.
- **US1 (Phase 3)**: Depends on Foundational.
- **US2 (Phase 4)**: Depends on Foundational; may proceed in parallel with US1
  after T024-T026 establish executable test composition.
- **US3 (Phase 5)**: Depends on the fixture foundations from US1 and US2.
- **US4 (Phase 6)**: Project/script scaffolding may start after Setup; final CI
  execution depends on US1-US3.
- **Polish (Phase 7)**: Depends on all user stories.

### User-story dependency graph

```text
Setup → Foundational ┬→ US1 ─┬→ US3 ─┐
                    └→ US2 ─┘       ├→ Polish
                         US4 ────────┘
```

### Parallel execution workstreams

After Phase 2:

```text
Workstream A: T017-T023  Assembled controller end-to-end journeys
Workstream B: T024-T032  Debug test composition and native UI automation
Workstream C: T037-T039  Scripts and CI scaffolding (final wiring waits for A/B)
```

Within US1, T020, T021, and T022 are independent after T017-T019 establish the
fixture. Within US2, page objects and automation IDs can proceed in parallel with
debug composition, then UI journey files can be split by page. US3 evidence
writers can be split between protocol and UI artifacts.

---

## Implementation Strategy

### MVP first

1. Complete Setup and Foundational phases.
2. Complete US1 with a healthy first-time connection/restart journey.
3. Run it repeatedly on hosted Windows CI.

This first increment replaces isolated transport mocks with a real loopback
protocol boundary and delivers value without waiting for interactive-runner
availability.

### Incremental delivery

1. Fake server contract and controller composition.
2. Complete protocol/application journeys.
3. Native Connect/Status UI smoke path.
4. Sensor, remove-server, and failure UI paths.
5. Sanitized evidence and CI policy.
6. Full repeatability, architecture builds, and documentation reconciliation.

## Format Validation

All tasks use the required checkbox, sequential task ID, optional `[P]` marker,
required user-story label in story phases, and explicit file path.
