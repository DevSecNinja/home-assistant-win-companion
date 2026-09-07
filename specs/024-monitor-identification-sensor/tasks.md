# Tasks: Monitor Identification Sensor

**Input**: Design documents from `specs/024-monitor-identification-sensor/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Core unit tests are required by the repository constitution and the
feature acceptance criteria.

**Organization**: Tasks are grouped by user story so the single-monitor MVP,
multi-monitor behavior, and headless/error behavior remain independently
verifiable.

## Phase 1: Setup (Shared Preparation)

**Purpose**: Establish the new sensor contract without changing collection
behavior.

- [x] T001 Add the `monitor_identity` sensor ID and identity-aware capture scope contract in `src/WindowsCompanion.Core/Sensors/DisplayCapturePolicy.cs`
- [x] T002 [P] Add the disabled-by-default sensitive Monitors definition and resource/privacy text in `src/WindowsCompanion.App/Services/DisplaySensorSource.cs`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Add shared identity and capture primitives used by every story.

**CRITICAL**: No user story work can complete until this phase is complete.

- [x] T003 Create normalized monitor identity, capture status, manufacturer decoding, ordering, deduplication, and public attribute models in `src/WindowsCompanion.Core/Sensors/MonitorIdentity.cs`
- [x] T004 Extend identity-scoped seeding and canonical change comparison without weakening count-only privacy in `src/WindowsCompanion.Core/Sensors/DisplayCapturePolicy.cs`
- [x] T005 Extend the active CCD path query with bounded insufficient-buffer retry, target-name interop, physical-target filtering, and internal-only deduplication keys in `src/WindowsCompanion.App/Services/DisplaySensorSource.cs`

**Checkpoint**: The source can obtain a privacy-scoped `MonitorCaptureResult`, and
Core can turn identities into deterministic output.

---

## Phase 3: User Story 1 - Identify an attached monitor (Priority: P1) MVP

**Goal**: Report one attached physical monitor using its Windows-friendly model,
manufacturer code, product code, connection type, and primary flag.

**Independent Test**: Supply one valid identity snapshot and verify the sensor
state and attributes match the Home Assistant contract without exposing the
internal key.

### Tests for User Story 1

- [x] T006 [US1] Add failing tests for friendly-name normalization, EISA manufacturer decoding, product-code formatting, single-monitor state, and private-key exclusion in `tests/WindowsCompanion.Core.Tests/HardwareSensorTests.cs`

### Implementation for User Story 1

- [x] T007 [US1] Implement single-monitor state and structured attributes in `src/WindowsCompanion.Core/Sensors/MonitorIdentity.cs`
- [x] T008 [US1] Wire identity capture, sensitive preview gating, icon, and Home Assistant sensor creation into `src/WindowsCompanion.App/Services/DisplaySensorSource.cs`

**Checkpoint**: One supported attached monitor is independently reported by the
new opt-in sensor.

---

## Phase 4: User Story 2 - Identify multiple attached monitors (Priority: P2)

**Goal**: Report a stable count and one structured entry per distinct physical
monitor, including identical models.

**Independent Test**: Supply unordered snapshots containing duplicates and two
physically distinct identical monitors, then verify deterministic ordering,
deduplication by internal key, and a `2 monitors` state.

### Tests for User Story 2

- [x] T009 [US2] Add failing tests for multi-monitor count state, deterministic ordering, duplicate collapse, identical-monitor preservation, eight-entry attribute bounds, and identity change detection in `tests/WindowsCompanion.Core.Tests/HardwareSensorTests.cs`

### Implementation for User Story 2

- [x] T010 [US2] Complete multi-monitor ordering, deduplication, bounded attribute output, and canonical change signatures in `src/WindowsCompanion.Core/Sensors/MonitorIdentity.cs`
- [x] T011 [US2] Ensure one enriched display capture serves resolution, count, and identity sensors together without duplicate CCD enumeration in `src/WindowsCompanion.App/Services/DisplaySensorSource.cs`

**Checkpoint**: Multi-monitor setups remain complete and stable across Windows
enumeration order changes.

---

## Phase 5: User Story 3 - Represent a headless computer (Priority: P3)

**Goal**: Report a successful empty topology as `No monitors` while keeping
Windows discovery failures distinguishable as `Unavailable`.

**Independent Test**: Compare successful-empty and unavailable capture results
and verify they produce different states and attribute shapes.

### Tests for User Story 3

- [x] T012 [US3] Add failing tests for `No monitors`, zero/empty attributes, unavailable output, unknown attached targets, and unavailable-to-available change detection in `tests/WindowsCompanion.Core.Tests/HardwareSensorTests.cs`

### Implementation for User Story 3

- [x] T013 [US3] Implement empty and unavailable state/attribute behavior in `src/WindowsCompanion.Core/Sensors/MonitorIdentity.cs`
- [x] T014 [US3] Map CCD access, unsupported-driver, target-name, and retry-exhaustion failures to unavailable without converting a successful empty capture in `src/WindowsCompanion.App/Services/DisplaySensorSource.cs`

**Checkpoint**: Headless and failed-discovery states are independently correct.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Keep shipped documentation, privacy guarantees, and supported builds
aligned with the implementation.

- [x] T015 [P] Update the display sensor table, privacy boundary, and monitor identity behavior in `specs/008-hardware-sensors/spec.md`
- [x] T016 [P] Update final implementation discoveries and validation expectations in `specs/024-monitor-identification-sensor/spec.md`, `specs/024-monitor-identification-sensor/research.md`, and `specs/024-monitor-identification-sensor/quickstart.md`
- [x] T017 Run the HardwareSensorTests selector from `specs/024-monitor-identification-sensor/quickstart.md`
- [x] T018 Build `src/WindowsCompanion.App/WindowsCompanion.App.csproj` for Release x64 and ARM64 using the repository-supported commands
- [x] T019 Run `.\scripts\test.ps1` and confirm existing display count/resolution lifecycle behavior remains intact

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: Starts immediately.
- **Foundational (Phase 2)**: Depends on Phase 1 and blocks all user stories.
- **User Story 1 (Phase 3)**: Depends on Phase 2 and is the MVP.
- **User Story 2 (Phase 4)**: Depends on Phase 2 and integrates with the formatter
  and source created for User Story 1.
- **User Story 3 (Phase 5)**: Depends on Phase 2 and can be implemented alongside
  User Story 2 after the single-monitor contract exists.
- **Polish (Phase 6)**: Depends on the selected user stories; full validation
  depends on all three.

### User Story Dependencies

- **User Story 1 (P1)**: No dependency on another story.
- **User Story 2 (P2)**: Reuses the US1 state/attribute formatter but has
  independently testable ordering and multiplicity behavior.
- **User Story 3 (P3)**: Reuses the US1 sensor wiring but has independently
  testable empty/error behavior.

### Parallel Opportunities

- T002 can proceed while T001 defines the Core ID contract, with final compilation
  after both complete.
- After T005, US2 tests/formatting (T009-T010) and US3 tests/formatting
  (T012-T013) can proceed in parallel because they modify separate regions of the
  same test/model files only when coordinated.
- T015 and T016 can proceed in parallel after behavior stabilizes.
- x64 and ARM64 builds in T018 can run independently.

---

## Parallel Example: User Stories 2 and 3

```text
Task: "Implement and test multi-monitor ordering and deduplication in
MonitorIdentity.cs and HardwareSensorTests.cs"

Task: "Implement and test headless versus unavailable behavior in
MonitorIdentity.cs and HardwareSensorTests.cs"
```

These tasks are logically independent but must not edit the same files
concurrently in one worktree without coordination.

---

## Implementation Strategy

### MVP First

1. Complete Phase 1 and Phase 2.
2. Complete User Story 1.
3. Run the targeted tests and verify one-monitor payload privacy.
4. Continue with multi-monitor and headless support before shipping because both
   are explicit acceptance requirements.

### Incremental Delivery

1. Add the privacy-scoped sensor contract and supported Windows capture.
2. Deliver correct one-monitor identity.
3. Add deterministic multi-monitor output without changing the one-monitor state.
4. Add explicit headless and unavailable states.
5. Correct the prior hardware specification and run full validation.

## Notes

- Every task includes an exact repository path.
- Tests must fail before their corresponding implementation task is completed.
- Device paths and identity values must never appear in logs or committed test
  fixtures; use synthetic values such as `MONITOR\DEL1234\INSTANCE`.
- Existing `displays_count` and `display_resolution` behavior is compatibility
  surface and must not regress.
