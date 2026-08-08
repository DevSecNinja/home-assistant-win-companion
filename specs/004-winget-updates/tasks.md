# Tasks: WinGet Update Status

## Phase 1: Setup

- [x] T001 Add WinGet result and source tests in `tests/HaCompanion.Core.Tests/WinGetUpdateTests.cs`

## Phase 2: Foundational

- [x] T002 Add refreshable-source contract in `src/HaCompanion.Core/Sensors/IRefreshableSensorSource.cs`
- [x] T003 Add enabled-source refresh orchestration in `src/HaCompanion.Core/Sensors/SensorCatalog.cs`
- [x] T004 Refresh enabled expensive sources before manual push in `src/HaCompanion.App/AppController.cs`

## Phase 3: User Story 1 - Update count

- [x] T005 [P] [US1] Add provider contract in `src/HaCompanion.Core/Abstractions/IWinGetUpdateProvider.cs`
- [x] T006 [P] [US1] Add update result models and JSON parsing in `src/HaCompanion.Core/Models/WinGetUpdateResult.cs`
- [x] T007 [P] [US1] Test success, zero and unavailable mappings in `tests/HaCompanion.Core.Tests/WinGetUpdateTests.cs`
- [x] T008 [US1] Implement cached six-hour sensor source in `src/HaCompanion.Core/Sensors/WinGetUpdateSensorSource.cs`
- [x] T009 [US1] Implement structured PowerShell provider in `src/HaCompanion.App/Services/PowerShellWinGetUpdateProvider.cs`
- [x] T010 [US1] Register the provider and source in `src/HaCompanion.App/AppController.cs`

## Phase 4: User Story 2 - Privacy and setup

- [x] T011 [P] [US2] Test local-only package preview and disabled no-query behavior in `tests/HaCompanion.Core.Tests/WinGetUpdateTests.cs`
- [x] T012 [US2] Add supported-version and Microsoft-signature detection to `src/HaCompanion.App/Services/PowerShellWinGetUpdateProvider.cs`
- [x] T013 [US2] Add copyable setup instructions in `src/HaCompanion.App/MainWindow.xaml.cs`
- [x] T014 [US2] Keep the sensor disabled until explicit setup completes

## Phase 5: User Story 3 - Controlled checks

- [x] T015 [P] [US3] Test refresh, cache and cancellation lifecycle in `tests/HaCompanion.Core.Tests/WinGetUpdateTests.cs`
- [x] T016 [US3] Complete source cancellation and six-hour scheduling in `src/HaCompanion.Core/Sensors/WinGetUpdateSensorSource.cs`

## Phase 6: Polish

- [x] T017 Document module setup and sensor privacy in `README.md`
- [x] T018 Mark feature shipped and run `specs/004-winget-updates/quickstart.md`

## Dependencies

- Phase 2 blocks all stories.
- US1 provides the source required by US2 and US3.
- T013 depends on T012 and T014.

## Parallel opportunities

- T005-T007 can proceed in parallel.
- T011 and T015 can be authored independently after the source contract exists.

## Implementation strategy

1. Add generic refresh orchestration.
2. Deliver cached count and unavailable states.
3. Add explicit module setup guidance and local details.
4. Validate scheduling, cancellation, privacy, and runtime behavior.
