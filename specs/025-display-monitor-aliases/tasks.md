# Tasks: Display and Monitor Search Aliases

**Feature**: `specs/025-display-monitor-aliases`
**Generated**: 2026-09-08
**Total Tasks**: 5

## Phase 1: Setup

_(No setup tasks; the feature extends existing projects and test infrastructure.)_

## Phase 2: Foundational

- [x] T001 Add optional search alias metadata and contract coverage in `src/WindowsCompanion.Core/Sensors/SensorDefinition.cs` and `tests/WindowsCompanion.Core.Tests/SensorDefinitionTests.cs`

## Phase 3: User Story 1 - Find display sensors with familiar terminology (P1)

**Goal**: The monitor identity card matches searches for either "display" or
"monitor".

**Independent Test**: Search for each term and verify the `display_identity` card
remains visible.

- [x] T002 [US1] Include configured sensor aliases in card search metadata in `src/WindowsCompanion.App/MainWindow.Sensors.cs`
- [x] T003 [US1] Extend the sensor filter scenario with display and monitor alias searches in `tests/WindowsCompanion.UI.Tests/SensorFilterUiTests.cs`

## Phase 4: User Story 2 - See consistent sensor terminology (P2)

**Goal**: The sensor uses a consistently display-based public contract.

**Independent Test**: Inspect the sensor card and emitted reading metadata and
verify the name is `Display Identity`, the unique ID is `display_identity`, and
public states and attributes use display terminology.

- [x] T004 [US2] Rename the monitor identity public contract to `Display Identity` and `display_identity`, configure monitor aliases, and use display-based output labels in `src/WindowsCompanion.App/Services/DisplaySensorSource.cs`, `src/WindowsCompanion.Core/Sensors/DisplayCapturePolicy.cs`, and `src/WindowsCompanion.Core/Sensors/MonitorIdentity.cs`

## Phase 5: Polish & Cross-Cutting Concerns

- [x] T005 Run default tests and the targeted composition contract, compile the UI automation project, and build Release x64 and ARM64

## Dependencies

```text
T001 -> T002 -> T003
T001 -> T004
T002 + T004 -> T005
T003 -> T005
```

User Story 1 depends on the foundational alias metadata. User Story 2 can be
implemented after T001 in parallel with T002-T003, but the final validation
requires both stories.

## Parallel Opportunities

- T004 can run in parallel with T002 after T001 because the tasks modify
  different files.
- No two incomplete tasks that edit the same file should run in parallel.

## Implementation Strategy

Complete T001-T003 for the search-alias MVP, then T004 for visible terminology
consistency. Finish with the focused validation in T005.
