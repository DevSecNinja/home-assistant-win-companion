# Tasks: Location Sensor

**Input**: Design documents from `/specs/013-location-sensor/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/location-sensor-payload.md, quickstart.md

**Tests**: Included. The constitution (`IV. Testable, Layered Architecture`) requires new Core contracts to have unit tests covering a happy path and a failure path, and this repo's existing sensor sources (e.g. `WinGetUpdateTests.cs`) always ship with matching Core unit tests.

**Organization**: Tasks are grouped by user story (from `spec.md`) to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Paths are relative to the repository root

## Path Conventions

Single existing solution: `src/WindowsCompanion.Core/`, `src/WindowsCompanion.App/`, `tests/WindowsCompanion.Core.Tests/` (see `plan.md` Project Structure).

---

## Phase 1: Setup

**Purpose**: No new project/toolchain setup is needed - this feature adds files to the existing `WindowsCompanion.Core` and `WindowsCompanion.App` projects and the existing `WindowsCompanion.Core.Tests` test project. This phase only confirms the workspace builds cleanly before changes begin.

- [X] T001 Confirm a clean baseline build: run `dotnet build src\WindowsCompanion.App\WindowsCompanion.App.csproj -c Release -p:Platform=x64 -r win-x64 --nologo` and `dotnet test --project tests\WindowsCompanion.Core.Tests\WindowsCompanion.Core.Tests.csproj -c Release` before any edits, so any later failure is attributable to this feature

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core model, abstraction, and sensor source shape that every user story's tests and UX depend on. No user story is independently testable until this phase is complete, because all three stories exercise the same `LocationSensorSource`/`ILocationProvider` pair with different `LocationResult.Status` values.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T002 [P] Create `LocationStatus` enum and `LocationResult` record in `src/WindowsCompanion.Core/Models/LocationResult.cs` per `data-model.md` (`Ready`/`PermissionDenied`/`Unavailable`, `Latitude`/`Longitude`/`AccuracyMeters`/`Timestamp`, and an `Unavailable(...)` factory mirroring `WinGetUpdateResult.Failure`)
- [X] T003 [P] Create `ILocationProvider` interface in `src/WindowsCompanion.Core/Abstractions/ILocationProvider.cs` with a single `Task<LocationResult> GetLocationAsync(CancellationToken cancellationToken = default)` method, matching `IWinGetUpdateProvider`'s shape
- [X] T004 [US-shared] Implement `LocationSensorSource` in `src/WindowsCompanion.Core/Sensors/LocationSensorSource.cs`: one `SensorDefinition` (`UniqueId = "location"`, `Name = "Location"`, `Privacy = SensorPrivacy.Sensitive`, `EnabledByDefault: false`, `OptInPlaceholder = "Enable to read this device's location"`, `ResourceUsage` and `AutomationIdea` text), a `SensorPollLoop` on a 15-minute interval wrapping `ILocationProvider.GetLocationAsync`, a locked cached `LocationResult`, `Read()`/`PreviewAsync()`/`Start()`/`Stop()`/`RefreshAsync()` following the exact structure of `WinGetUpdateSensorSource` (depends on T002, T003)
- [X] T005 Implement `WindowsLocationProvider` in `src/WindowsCompanion.App/Services/WindowsLocationProvider.cs`: capture the app's `Microsoft.UI.Dispatching.DispatcherQueue` at construction, marshal `Geolocator.RequestAccessAsync()` (once) and `GetGeopositionAsync()` onto that dispatcher per call, and map `GeolocationAccessStatus`/`PositionStatus`/timeouts/exceptions to `LocationResult` per the "Status/error mapping" decision in `research.md` (depends on T002, T003)
- [X] T006 Wire the new source into production composition: add `new LocationSensorSource(location, config.Sensors)` to `src/WindowsCompanion.App/ProductionSensorComposition.cs`, add an `ILocationProvider` parameter to `CreateSources`, construct `new WindowsLocationProvider(...)` in `src/WindowsCompanion.App/ProductionAppComposition.cs` and pass it through, and add `Location` to `src/WindowsCompanion.App/AppControllerDependencies.cs` (`OwnedDependency<ILocationProvider>`, included in `OwnedValues()`) (depends on T004, T005)
- [X] T007 [P] Add a no-op `ILocationProvider` (returning `LocationResult.Unavailable(LocationStatus.Unavailable)`) to `src/WindowsCompanion.App/TestAppComposition.cs` and pass it into the test-profile `SensorSourceFactory`, following the existing `NoOpWinGetUpdateProvider` pattern (depends on T003)

**Checkpoint**: Foundation ready - the Location sensor now appears (disabled) in the Sensors page and builds/runs; user story work can begin.

---

## Phase 3: User Story 1 - Track the PC's current location in Home Assistant (Priority: P1) 🎯 MVP

**Goal**: Enabling the Location sensor makes Home Assistant receive a real, periodically-refreshed latitude/longitude/accuracy reading.

**Independent Test**: Per `quickstart.md` step 3 - with Windows Location Services on and permission granted, enable the sensor and confirm Home Assistant receives a `"lat,long"` state with a `gps_accuracy` attribute within one sync cycle, and that it updates on the next scheduled poll.

### Tests for User Story 1

- [X] T008 [P] [US1] Add `Enable_reports_ready_coordinate_with_accuracy_attribute` to a new `tests/WindowsCompanion.Core.Tests/LocationSensorSourceTests.cs`: a fake `ILocationProvider` returns a `Ready` result, `RefreshAsync()` + `Read()` produce a `"{lat:F6},{lng:F6}"` state and a `gps_accuracy` attribute matching the fixture's accuracy value
- [X] T009 [P] [US1] Add `Refresh_reports_updated_coordinate_on_next_poll` to `tests/WindowsCompanion.Core.Tests/LocationSensorSourceTests.cs`: two sequential fake provider results with different coordinates both surface correctly through successive `RefreshAsync()`/`Read()` calls

### Implementation for User Story 1

- [X] T010 [US1] Finish the `Ready`-path rendering in `LocationSensorSource.Read()` (`src/WindowsCompanion.Core/Sensors/LocationSensorSource.cs`): `State = "{Latitude:F6},{Longitude:F6}"`, `Attributes = { ["gps_accuracy"] = AccuracyMeters }`, `Icon = "mdi:crosshairs-gps"` per `data-model.md` and the `contracts/location-sensor-payload.md` registration payload shape (depends on T004)
- [X] T011 [US1] Add a `ChangeGate<(double Lat, double Lng)>` (coordinates rounded to ~4 decimal places) in `LocationSensorSource` so `onChanged` fires only on a meaningful position change, matching `WinGetUpdateSensorSource`'s own change-gate convention (depends on T010)
- [X] T012 [US1] Verify `WindowsLocationProvider.GetLocationAsync()` (`src/WindowsCompanion.App/Services/WindowsLocationProvider.cs`) returns `Latitude`/`Longitude`/`AccuracyMeters` from `Geoposition.Coordinate` on a successful `GetGeopositionAsync()` call (depends on T005)

**Checkpoint**: At this point, User Story 1 should be fully functional and testable independently - a real coordinate flows from Windows to Home Assistant.

---

## Phase 4: User Story 2 - Location stays private until explicitly enabled (Priority: P2)

**Goal**: The sensor defaults to off, is clearly labeled as revealing precise location, and makes zero queries or log entries while disabled.

**Independent Test**: Per `quickstart.md` steps 2 and 6 - a fresh install shows the sensor disabled with the opt-in placeholder text, and no location query or coordinate value ever appears in logs while it stays disabled.

### Tests for User Story 2

- [X] T013 [P] [US2] Add `Disabled_sensor_performs_no_provider_query` to `tests/WindowsCompanion.Core.Tests/LocationSensorSourceTests.cs`, mirroring `WinGetUpdateTests.Disabled_preview_performs_no_provider_query`: with the sensor disabled, `PreviewAsync()`/a `SensorCatalog.RefreshAsync()` cycle make zero calls to the fake provider and return the `OptInPlaceholder` text
- [X] T014 [P] [US2] Add `Location_definition_is_sensitive_and_off_by_default` to `tests/WindowsCompanion.Core.Tests/LocationSensorSourceTests.cs`: assert `LocationSensorSource.Definitions[0].Privacy == SensorPrivacy.Sensitive` and `EnabledByDefault == false`

### Implementation for User Story 2

- [X] T015 [US2] Confirm `LocationSensorSource.PreviewAsync()` (`src/WindowsCompanion.Core/Sensors/LocationSensorSource.cs`) routes through `SensorPreviewGate.Permitted(...)` before ever calling `Read()`/the provider, so a disabled sensor never triggers a location query even from the Settings-page preview (depends on T004)
- [X] T016 [US2] Confirm no code path in `LocationSensorSource`/`WindowsLocationProvider` logs a coordinate, latitude, or longitude value (only status/exception type may be logged), keeping `SensorDefinition.Loggable` (false for this sensor) accurate (depends on T004, T005)

**Checkpoint**: At this point, User Stories 1 AND 2 both work independently - real coordinates flow when enabled, and nothing is collected or logged while disabled.

---

## Phase 5: User Story 3 - Clear guidance when location access is unavailable (Priority: P3)

**Goal**: When Windows denies access or Location Services are off, the sensor reports a clear, distinct state and the user has a one-click path to Windows' Location settings.

**Independent Test**: Per `quickstart.md` step 4 - with Windows Location Services off, enable the sensor and confirm it reports "Location permission required" (not a stale value or crash), and that the Sensors page's existing "Windows settings" button opens `ms-settings:privacy-location`.

### Tests for User Story 3

- [X] T017 [P] [US3] Add `PermissionDenied_result_reports_actionable_state` to `tests/WindowsCompanion.Core.Tests/LocationSensorSourceTests.cs`: a fake provider returning `LocationStatus.PermissionDenied` makes `Read()` report "Location permission required" with no `Attributes`
- [X] T018 [P] [US3] Add `Unavailable_result_reports_unavailable_state` to `tests/WindowsCompanion.Core.Tests/LocationSensorSourceTests.cs`: a fake provider returning `LocationStatus.Unavailable` makes `Read()` report "Unavailable" with no `Attributes`
- [X] T019 [P] [US3] Add `Stopping_source_cancels_an_active_query` to `tests/WindowsCompanion.Core.Tests/LocationSensorSourceTests.cs`, mirroring `WinGetUpdateTests.Stopping_source_cancels_an_active_check`: `source.Stop()` while a fake provider call is blocked cancels that call's `CancellationToken`

### Implementation for User Story 3

- [X] T020 [US3] Implement the `PermissionDenied`/`Unavailable` rendering branches in `LocationSensorSource.Read()`/`PreviewAsync()` (`src/WindowsCompanion.Core/Sensors/LocationSensorSource.cs`): distinct state text per status, `Icon = "mdi:crosshairs-question"`, no `Attributes` (depends on T004, T010)
- [X] T021 [US3] Implement the `GeolocationAccessStatus`/`PositionStatus`/timeout/exception → `LocationResult` mapping in `WindowsLocationProvider` (`src/WindowsCompanion.App/Services/WindowsLocationProvider.cs`) exactly per the "Status/error mapping" table in `research.md` (depends on T005)
- [X] T022 [US3] Update the existing "Windows location access" card's description text in `src/WindowsCompanion.App/MainWindow.xaml` (the `Border` above `SensorList` wired to `OnOpenLocationSettings`) to mention the Location sensor alongside Wi-Fi SSID/BSSID, since `AppController.OpenLocationSettings()` is reused as-is (no new action/button needed)

**Checkpoint**: All three user stories are independently functional - Ready, disabled/private, and permission-denied/unavailable paths all behave and render correctly.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final repo-wide validation once all user stories are complete.

- [X] T023 [P] Run `dotnet test --project tests\WindowsCompanion.Core.Tests\WindowsCompanion.Core.Tests.csproj -c Release -- --filter-query "/*/*/LocationSensorSourceTests/*"` and confirm every new test passes
- [X] T024 Run the full `dotnet test --project tests\WindowsCompanion.Core.Tests\WindowsCompanion.Core.Tests.csproj -c Release` suite to confirm no regressions
- [X] T025 [P] Run `dotnet build src\WindowsCompanion.App\WindowsCompanion.App.csproj -c Release -p:Platform=x64 -r win-x64 --nologo` and the ARM64 equivalent to confirm both platforms still build
- [X] T026 Execute the manual validation steps in `specs/013-location-sensor/quickstart.md` (steps 1-7) on a Windows machine and record any deviations back into `spec.md`/`research.md` per the constitution's evidence-driven-development principle

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion - BLOCKS all user stories
- **User Stories (Phase 3-5)**: All depend on Foundational phase completion
  - US1, US2, and US3 all touch the same `LocationSensorSource.Read()`/`PreviewAsync()` methods, so within a single-developer flow they are best done in priority order (P1 → P2 → P3); a second developer picking up US2/US3 in parallel must coordinate edits to the same file
- **Polish (Phase 6)**: Depends on all desired user stories being complete

### User Story Dependencies

- **User Story 1 (P1)**: Can start after Foundational (Phase 2) - no dependency on US2/US3
- **User Story 2 (P2)**: Can start after Foundational (Phase 2) - independently testable via the disabled-sensor path, though it shares `LocationSensorSource.cs` with US1/US3
- **User Story 3 (P3)**: Can start after Foundational (Phase 2) - independently testable via the permission-denied/unavailable path, though it shares `LocationSensorSource.cs`/`WindowsLocationProvider.cs` with US1

### Within Each User Story

- Tests are written first and must fail before the corresponding implementation task
- `LocationResult`/`ILocationProvider` (Foundational) before `LocationSensorSource` rendering (US1/US2/US3)
- Core rendering (`LocationSensorSource`) before the App-side provider mapping that feeds it real statuses (`WindowsLocationProvider`)
- Story complete before moving to the next priority

### Parallel Opportunities

- T002 and T003 (different new files) run in parallel
- T007 runs in parallel with T004-T006 (different file, only needs the T003 interface)
- Within US1: T008 and T009 (same test file, but independent test methods - write both before implementing) can be drafted in parallel by one author before implementation
- Within US3: T017, T018, T019 (independent test methods in the same file) can be drafted in parallel before implementation
- T023 and T025 in Polish can run in parallel (test run vs. build)

---

## Parallel Example: Foundational Phase

```bash
# Launch independent foundational file creation together:
Task: "Create LocationStatus enum and LocationResult record in src/WindowsCompanion.Core/Models/LocationResult.cs"
Task: "Create ILocationProvider interface in src/WindowsCompanion.Core/Abstractions/ILocationProvider.cs"
```

## Parallel Example: User Story 3 tests

```bash
# Launch all new test methods for User Story 3 together (same file, independent methods):
Task: "PermissionDenied_result_reports_actionable_state in tests/WindowsCompanion.Core.Tests/LocationSensorSourceTests.cs"
Task: "Unavailable_result_reports_unavailable_state in tests/WindowsCompanion.Core.Tests/LocationSensorSourceTests.cs"
Task: "Stopping_source_cancels_an_active_query in tests/WindowsCompanion.Core.Tests/LocationSensorSourceTests.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (CRITICAL - blocks all stories)
3. Complete Phase 3: User Story 1
4. **STOP and VALIDATE**: Enable the sensor manually with real Windows location permission granted and confirm Home Assistant receives a coordinate (quickstart.md step 3)
5. Demo if ready - note that shipping only US1 would mean the sensor might default to enabled or lack clear unavailable-state wording, so US2/US3 should follow immediately given the constitution's privacy principle

### Incremental Delivery

1. Complete Setup + Foundational → Foundation ready (sensor exists, disabled, in the catalog)
2. Add User Story 1 → Validate real coordinates flow → (do not ship alone; see MVP note above)
3. Add User Story 2 → Validate privacy defaults/no logging → safe to ship US1+US2
4. Add User Story 3 → Validate permission/unavailable guidance → full feature ready to ship
5. Each story adds value without breaking previous stories

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- This feature intentionally ships all three stories together before merge, since Core Principle II (privacy) makes US2 non-optional for a sensor this sensitive - the phased breakdown above is for implementation/testing order, not partial release
- Commit after each task or logical group, following Conventional Commits per repo convention
- Stop at any checkpoint to validate story independently before moving on
