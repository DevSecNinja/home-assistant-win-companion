# Tasks: Time Zone Offset Attribute

**Input**: Design documents from `specs/020-time-zone-offset/`
**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`,
`contracts/time-zone-sensor.md`, `quickstart.md`

## Phase 1: Setup

**Purpose**: Confirm the existing locale sensor and focused test surfaces.

- [x] T001 Review the existing time-zone state contract in `src/WindowsCompanion.App/Services/LocaleSensorSource.cs` and formatter tests in `tests/WindowsCompanion.Core.Tests/HardwareSensorTests.cs`

---

## Phase 2: Foundational

**Purpose**: Add a deterministic, unit-testable offset calculation shared by both
user stories.

- [x] T002 Add signed current UTC offset calculation in `src/WindowsCompanion.Core/Sensors/LocaleFormatter.cs`

**Checkpoint**: Core can calculate exact offset seconds for an explicit time zone
and instant.

---

## Phase 3: User Story 1 - Calculate Local Times (Priority: P1)

**Goal**: Expose a signed `utc_offset_seconds` attribute without changing the
existing Time Zone sensor state.

**Independent Test**: Read positive, negative, UTC, and fractional-hour offsets,
then confirm the attribute can be added directly to UTC as seconds.

### Tests for User Story 1

- [x] T003 [P] [US1] Add UTC, positive, negative, and fractional-hour offset tests in `tests/WindowsCompanion.Core.Tests/HardwareSensorTests.cs`

### Implementation for User Story 1

- [x] T004 [US1] Add `utc_offset_seconds` to Time Zone readings while preserving the existing entity contract in `src/WindowsCompanion.App/Services/LocaleSensorSource.cs`

**Checkpoint**: User Story 1 independently provides calculation-friendly current
offsets for all sign and fraction cases.

---

## Phase 4: User Story 2 - Track Seasonal Offset Changes (Priority: P2)

**Goal**: Ensure daylight-saving changes update the attribute and trigger the
existing change-driven synchronization path.

**Independent Test**: Calculate offsets for the same daylight-saving zone before
and after a transition and confirm the captured sensor state treats the offset
change as meaningful.

### Tests for User Story 2

- [x] T005 [P] [US2] Add daylight-saving transition offset tests in `tests/WindowsCompanion.Core.Tests/HardwareSensorTests.cs`

### Implementation for User Story 2

- [x] T006 [US2] Include the offset in locale source snapshot and change-gate state, and schedule the next offset transition while enabled in `src/WindowsCompanion.App/Services/LocaleSensorSource.cs`

**Checkpoint**: An offset-only seasonal change can request an immediate sensor
sync while the IANA state remains unchanged.

---

## Phase 5: Polish & Cross-Cutting Concerns

**Purpose**: Validate the complete contract and keep design evidence current.

- [x] T007 Run the focused Core tests and source build documented in `specs/020-time-zone-offset/quickstart.md`
- [x] T008 Reconcile implementation details and validation outcomes across `specs/020-time-zone-offset/spec.md`, `specs/020-time-zone-offset/contracts/time-zone-sensor.md`, and `specs/020-time-zone-offset/quickstart.md`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Phase 1 and blocks both stories.
- **User Story 1 (Phase 3)**: Depends on Phase 2 and provides the MVP.
- **User Story 2 (Phase 4)**: Depends on Phase 2; its App integration follows
  User Story 1 because both modify the same source snapshot.
- **Polish (Phase 5)**: Depends on both user stories.

### User Story Dependencies

- **User Story 1**: Starts after T002 and has no dependency on User Story 2.
- **User Story 2**: Core transition tests start after T002; source integration is
  applied after T004 to avoid concurrent edits to the same file.

### Parallel Opportunities

- T003 and T005 can be authored together after T002 because they cover independent
  offset scenarios in the same test surface.
- Documentation review for T008 can begin while T007 runs after implementation.

## Parallel Example: User Stories 1 and 2

```text
Task T003: Add sign and fractional-offset tests in HardwareSensorTests.cs
Task T005: Add daylight-saving transition tests in HardwareSensorTests.cs
```

## Implementation Strategy

### MVP First

1. Complete T001-T002.
2. Complete User Story 1 through T004.
3. Validate arithmetic with the focused tests.

### Incremental Delivery

1. Add exact current offset calculation.
2. Expose the new attribute as the independently useful MVP.
3. Extend captured state so seasonal changes synchronize immediately.
4. Run focused validation and reconcile the feature artifacts.
