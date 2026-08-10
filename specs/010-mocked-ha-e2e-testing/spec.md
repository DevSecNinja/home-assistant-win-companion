# Feature Specification: Mocked Home Assistant End-to-End Testing

**Feature Branch**: `main`

**Created**: 2026-08-10

**Status**: Implemented; further runtime validation is CI-only

**Input**: User description: "Create a fully mocked Home Assistant instance, automated end-to-end tests, and UI tests; use Spec Kit to plan before implementation and build independent workstreams in parallel."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Validate Complete Companion Journeys (Priority: P1)

As a maintainer, I can run automated tests against an isolated Home Assistant substitute so that complete companion journeys are validated without configuring a real server or using real credentials.

**Why this priority**: A deterministic server substitute is the foundation for reliable end-to-end coverage and removes the largest barrier to exercising the assembled application.

**Independent Test**: Start the isolated test environment, run the companion connection and synchronization journey, and verify registration, sensor updates, notifications, and persisted reconnection entirely with synthetic data.

**Acceptance Scenarios**:

1. **Given** a clean companion profile and a healthy test instance, **When** the connection journey completes, **Then** the companion authenticates, registers one device, synchronizes enabled sensors, and reaches a connected state.
2. **Given** a previously registered companion profile, **When** the companion restarts, **Then** it resumes the existing registration without creating a duplicate device.
3. **Given** an active connection, **When** the test instance sends a notification, **Then** the companion accepts and acknowledges the notification through the normal application path.
4. **Given** configurable authentication, network, and server rejection failures, **When** each failure is activated, **Then** the companion exposes the expected state and recovers when the failure is removed.

---

### User Story 2 - Validate Critical Windows UI Journeys (Priority: P1)

As a maintainer, I can automatically launch and interact with the Windows application so that user-visible workflows and their connection to application behavior are protected from regressions.

**Why this priority**: Core unit tests cannot detect broken window navigation, controls, bindings, dialogs, or accessibility surfaces.

**Independent Test**: Launch the application with an isolated profile and test instance, complete each critical workflow through visible controls, and verify both the displayed result and the corresponding application outcome.

**Acceptance Scenarios**:

1. **Given** a clean profile, **When** the test enters a server address and initiates sign-in, **Then** the UI progresses through connection setup and displays the connected status.
2. **Given** a connected companion, **When** the test changes sensor settings, disconnects, and reconnects, **Then** the UI and server-observed behavior remain consistent.
3. **Given** a connected companion, **When** the test removes the server and confirms the action, **Then** the UI returns to first-time setup and the saved test registration is removed.
4. **Given** an authentication or connectivity failure, **When** the failure reaches the application, **Then** the UI displays an actionable failure state and remains operable.
5. **Given** a supported interactive test environment, **When** a notification arrives or the window is hidden to the tray, **Then** automation verifies native notification delivery and restoration from the tray without relying on screen coordinates.

---

### User Story 3 - Diagnose Automated Failures (Priority: P2)

As a contributor, I receive actionable evidence when an end-to-end or UI test fails so that I can reproduce and diagnose the failure without exposing sensitive information.

**Why this priority**: Cross-process and UI failures are costly unless execution state and visual evidence are retained.

**Independent Test**: Intentionally fail one scenario and verify that the run preserves sanitized logs, test results, and relevant visual evidence with a clear scenario identity.

**Acceptance Scenarios**:

1. **Given** a failed automated scenario, **When** the run finishes, **Then** its output identifies the scenario, failing step, companion state, and test-instance interactions.
2. **Given** failure evidence, **When** it is inspected, **Then** it contains no real credentials, personal endpoints, or sensitive sensor values.

---

### User Story 4 - Run the Suites Locally and in Continuous Integration (Priority: P2)

As a contributor, I can rely on continuous integration to run the required suites consistently for proposed changes, with focused commands retained for CI diagnosis.

**Why this priority**: Tests provide lasting value only when contributors and automated checks can run them repeatably.

**Independent Test**: Inspect the documented CI jobs and their retained results, then compare the executed scenarios and outcomes across hosted and interactive runners.

**Acceptance Scenarios**:

1. **Given** a supported Windows runner, **When** CI invokes a focused or complete suite, **Then** it runs without manual Home Assistant configuration.
2. **Given** a proposed change, **When** continuous integration runs, **Then** hosted checks execute the headless end-to-end suite and compile the UI suite, while trusted interactive-runner checks execute rendered UI scenarios and preserve diagnostic evidence.

### Edge Cases

- The test instance receives duplicate registration attempts or repeated webhook updates.
- Authentication is rejected, expires, or is interrupted midway through setup.
- The test instance disconnects the notification channel and later becomes available.
- The companion is restarted with partially written or stale persisted test state.
- A test process, companion process, or browser handoff terminates unexpectedly.
- Multiple tests execute concurrently without sharing ports, profiles, registrations, or evidence.
- Display scaling, theme, focus, animation, and window position differ from the default environment.
- A modal confirmation or external browser handoff would otherwise block unattended execution.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The test system MUST provide an isolated Home Assistant substitute that supports every authentication, registration, webhook, server-information, and notification interaction used by the covered journeys.
- **FR-002**: The test instance MUST record received interactions and allow each scenario to control responses, delays, disconnections, and failures deterministically.
- **FR-003**: Each scenario MUST use an isolated companion profile, server state, network endpoint, and synthetic credential set.
- **FR-004**: End-to-end tests MUST exercise assembled application workflows across their real boundaries rather than replace the Home Assistant client or connection coordinator with test doubles.
- **FR-005**: End-to-end coverage MUST include first-time connection, registration, sensor synchronization, notification handling, reconnect behavior, and persisted-session restart.
- **FR-006**: Failure coverage MUST include rejected authentication, rejected Home Assistant operations, unavailable connections, interrupted notification channels, and recovery.
- **FR-007**: UI automation MUST launch the built application and drive critical workflows through user-visible controls.
- **FR-008**: UI coverage MUST include connection setup, connected status, sensor configuration, disconnect/reconnect, server removal, and actionable failure states.
- **FR-009**: Controls required by UI automation MUST have stable accessible identities and remain usable by assistive technologies.
- **FR-010**: Automated runs MUST clean up companion processes, browser handoffs, profiles, endpoints, and test-instance state even after a failed scenario.
- **FR-011**: Failed scenarios MUST preserve sanitized logs, structured interaction history, test results, and visual evidence when relevant.
- **FR-012**: No test fixture, artifact, diagnostic output, or committed file MAY contain real credentials, personal Home Assistant addresses, webhook identifiers, or sensitive sensor values.
- **FR-013**: CI MUST be able to run individual scenarios, each suite, or all automated tests using documented repository commands. Per the current validation policy, further runtime test execution is CI-only.
- **FR-014**: Hosted continuous integration MUST run headless end-to-end tests and compile UI automation; rendered UI tests MUST run on a trusted, supported interactive Windows runner and retain failure evidence.
- **FR-015**: Existing fast core tests MUST remain independently runnable without launching the application or test instance.
- **FR-016**: The automated suites MUST document their supported scenarios, environmental prerequisites, boundaries, and known limitations.
- **FR-017**: Environment-gated UI smoke coverage MUST verify native notification delivery and tray restoration when the runner provides the required Windows shell capabilities.

### Key Entities

- **Test Instance**: An isolated Home Assistant substitute with scenario-controlled protocol behavior and a record of observed interactions.
- **Test Scenario**: A deterministic setup, user or application action sequence, expected companion states, and expected server interactions.
- **Companion Test Profile**: Synthetic application settings and credentials isolated to one scenario.
- **Failure Evidence**: Sanitized logs, interaction history, results, and visual captures associated with one scenario run.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: CI can run all end-to-end scenarios from a clean supported environment without a real Home Assistant account or manual server configuration.
- **SC-002**: Automated coverage verifies 100% of the critical journeys named in FR-005 and FR-008, with at least one expected failure and recovery scenario for authentication, connectivity, and notification delivery.
- **SC-003**: Ten consecutive executions of each new suite complete without nondeterministic failures in the supported continuous integration environment.
- **SC-004**: A focused local end-to-end scenario completes within 2 minutes and the required continuous integration suites complete within 15 minutes.
- **SC-005**: Every induced test failure produces evidence that identifies the failing scenario and step, while an automated secret scan reports no real or unredacted sensitive values.
- **SC-006**: Existing core tests remain runnable on their own and retain their established coverage thresholds.
- **SC-007**: Every trusted interactive-runner execution reports native notification and tray smoke scenarios as passed, failed with evidence, or explicitly unsupported with the missing capability identified.

## Assumptions

- The initial automated UI scope targets the repository's supported Windows application and does not add cross-platform UI coverage.
- The mocked instance reproduces only Home Assistant behavior used by this companion; it is not a general-purpose Home Assistant emulator.
- External browser authentication is represented by a deterministic test-owned authorization flow rather than requiring manual credentials.
- Native toast rendering and operating-system lifecycle delivery may require a smaller environment-gated suite when they cannot be made reliable on every hosted runner.
- Parallel delivery means independently owned implementation workstreams begin only after the specification, technical plan, and dependency-ordered tasks are complete and consistent.

## Implemented Reality

- The test harness is a test-only ASP.NET Core Kestrel fake bound to an
  OS-assigned loopback port. It models the companion's OAuth, REST, registration,
  webhook, WebSocket, and notification surfaces.
- A prior runtime validation passed 24 journey and foundation tests. A separate
  interactive validation passed seven rendered UI scenarios and the native-toast
  capability scenario.
- Tray restoration is capability-gated. It is explicitly skipped with the missing
  capability when Windows UI Automation cannot expose a usable tray icon; a skip
  is not reported as a pass.
- Hosted CI runs the headless end-to-end project and compiles the UI project.
  Rendered UI execution belongs to the trusted interactive x64 self-hosted job.
- Failure paths retain sanitized scenario metadata, app and fake-server logs, TRX
  results, and UI screenshots/accessibility trees where applicable.
- Release application builds are supported for x64 and ARM64. Rendered UI
  automation remains x64 because that is the configured interactive runner.
- No measured duration was retained from the prior validation, so SC-003 and
  SC-004 remain CI validation targets rather than claimed outcomes.
- At the user's request, no additional runtime tests are to be run outside CI.
  This reconciliation used static Markdown and diff checks only.
