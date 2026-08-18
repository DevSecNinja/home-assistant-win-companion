---

description: "Implementation tasks for the WireGuard status sensor"
---

# Tasks: WireGuard Sensors

**Input**: Design documents from `/specs/018-wireguard-sensors/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/sensor-contract.md

**Tests**: Required by the project constitution for new Core contracts and by the feature's measurable outcomes.

**Organization**: The clarified feature contains one independently deliverable P1 user story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel because it changes a different file and does not depend on incomplete implementation.
- **[US1]**: Maps to "See WireGuard Connection State."

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: No project initialization or dependency change is needed; existing Core, App, E2E, and xUnit infrastructure is reused.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: No shared infrastructure is required outside User Story 1; the status contract and Windows probe exist solely for that story.

**Checkpoint**: Existing sensor infrastructure is ready for story implementation.

---

## Phase 3: User Story 1 - See WireGuard Connection State (Priority: P1) MVP

**Goal**: Publish one privacy-preserving aggregate WireGuard status sensor that works unelevated with the official Windows client.

**Independent Test**: Enable only `wireguard_status`, drive fake connected, disconnected, unavailable, repeated-event, and stopped-source observations, and verify the sensor contract and callback lifecycle without using local WireGuard configuration.

### Tests for User Story 1

> **NOTE**: Write these tests first and confirm they fail before implementation.

- [x] T001 [P] [US1] Add status mapping and classification happy/failure tests in tests/WindowsCompanion.Core.Tests/WireGuardStatusTests.cs
- [x] T002 [P] [US1] Add exact service/adapter matching, missing-client, and inspection-failure tests in tests/WindowsCompanion.E2E.Tests/WindowsWireGuardStatusProbeTests.cs
- [x] T003 [P] [US1] Add sensor metadata, enabled filtering, preview, event deduplication, repeated start/stop, and callback-after-stop tests in tests/WindowsCompanion.E2E.Tests/WireGuardSensorSourceTests.cs

### Implementation for User Story 1

- [x] T004 [US1] Implement the three-state model, lowercase formatter, and privacy-safe classifier in src/WindowsCompanion.Core/Sensors/WireGuardStatus.cs
- [x] T005 [US1] Implement the injectable read-only Service Control Manager and network-interface probe with exact private name matching and narrow failure handling in src/WindowsCompanion.App/Services/WindowsWireGuardStatusProbe.cs
- [x] T006 [US1] Implement the opt-in diagnostic sensor, preview, idempotent network-change subscription, burst collapse, transition gate, and post-stop callback suppression in src/WindowsCompanion.App/Services/WireGuardSensorSource.cs
- [x] T007 [US1] Register WireGuardSensorSource in the production sensor catalog in src/WindowsCompanion.App/ProductionSensorComposition.cs
- [x] T008 [US1] Extend the production catalog contract to assert the WireGuard sensor is composed once and remains disabled by default in tests/WindowsCompanion.E2E.Tests/CompositionContractTests.cs

**Checkpoint**: User Story 1 is independently functional and satisfies the complete first-release scope.

---

## Phase 4: Polish & Cross-Cutting Concerns

**Purpose**: Confirm the implementation and evidence remain aligned across architectures.

- [x] T009 Reconcile implementation discoveries and final sensor wording across specs/018-wireguard-sensors/spec.md, specs/018-wireguard-sensors/research.md, and specs/018-wireguard-sensors/contracts/sensor-contract.md
- [x] T010 Measure repeated unelevated status observations and idle-between-event behavior against SC-006 using the procedure in specs/018-wireguard-sensors/quickstart.md
- [x] T011 Run the targeted Core and E2E tests plus the no-launch app build documented in specs/018-wireguard-sensors/quickstart.md
- [x] T012 Build the supported ARM64 Release target using src/WindowsCompanion.App/WindowsCompanion.App.csproj

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No work required.
- **Foundational (Phase 2)**: No work required.
- **User Story 1 (Phase 3)**: Starts immediately; tests T001-T003 precede implementation.
- **Polish (Phase 4)**: Depends on T001-T008.

### User Story Dependencies

- **User Story 1 (P1)**: No dependency on another story and is the complete MVP.

### Within User Story 1

- T001, T002, and T003 can be authored in parallel.
- T004 satisfies T001.
- T005 satisfies T002 and depends on the status contract from T004.
- T006 satisfies T003 and depends on T004-T005.
- T007 and T008 follow T006.

### Parallel Opportunities

- T001, T002, and T003 touch separate test files and can run in parallel.
- Documentation reconciliation in T009 can begin after behavior stabilizes while architecture validation is prepared.

---

## Parallel Example: User Story 1

```text
Task: "Add Core status tests in tests/WindowsCompanion.Core.Tests/WireGuardStatusTests.cs"
Task: "Add probe tests in tests/WindowsCompanion.E2E.Tests/WindowsWireGuardStatusProbeTests.cs"
Task: "Add source lifecycle tests in tests/WindowsCompanion.E2E.Tests/WireGuardSensorSourceTests.cs"
```

---

## Implementation Strategy

### MVP First

1. Author T001-T003 and confirm the new contract is unimplemented.
2. Implement T004-T006 from the deterministic Core outward to Windows integration.
3. Compose the source with T007-T008.
4. Stop and validate the complete status-only MVP.

### Incremental Delivery

The clarification intentionally removed traffic totals and rates. This task list
therefore has one deployable increment; future sensors require a new specification
decision rather than expanding this scope during implementation.

## Notes

- Never place real tunnel names, endpoints, addresses, keys, or configuration content in tests or diagnostics.
- Use fake service/adapter names in tests and assert they never reach published sensor data.
- Do not add `System.ServiceProcess.ServiceController`, invoke `wg.exe`, or use PowerShell from the application.
- Keep native handles bounded with safe disposal and treat incomplete enumeration as `unavailable`.

## Phase 5: Convergence

- [x] T013 Prevent disabled settings previews from observing WireGuard and cover the zero-observation preview path per FR-008 and SC-003 (partial)
- [x] T014 Roll back source and watcher lifecycle state when network watcher startup fails, with recovery tests, per FR-008 and Constitution V (partial)
