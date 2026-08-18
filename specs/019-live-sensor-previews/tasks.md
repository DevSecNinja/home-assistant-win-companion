# Tasks: Live Sensor Previews

## Phase 1: Setup

- [X] T001 Confirm current Sensors page preview and window lifecycle integration points in src/WindowsCompanion.App/MainWindow.Sensors.cs and src/WindowsCompanion.App/MainWindow.xaml.cs

## Phase 2: Foundational

- [X] T002 Extend preview cancellation coordination for repeated single-flight list refreshes in src/WindowsCompanion.App/Services/SensorPreviewCancellation.cs
- [X] T003 [P] Add cancellation lifecycle tests in tests/WindowsCompanion.App.Tests/SensorPreviewCancellationTests.cs

## Phase 3: User Story 1 - Observe Live Sensor Changes (P1)

**Goal**: Update current-value text automatically without rebuilding the page.

**Independent Test**: Keep Sensors open, change Now Playing, and observe the new value within five seconds without navigation.

- [X] T004 [US1] Add a two-second automatic preview timer and in-place preview update method in src/WindowsCompanion.App/MainWindow.Sensors.cs
- [X] T005 [US1] Start an immediate refresh after the initial Sensors view is displayed in src/WindowsCompanion.App/MainWindow.Sensors.cs
- [X] T006 [P] [US1] Expose current-value automation identifiers and lookup support in src/WindowsCompanion.App/MainWindow.Sensors.cs and tests/WindowsCompanion.UI.Tests/Pages/SensorsPage.cs
- [X] T007 [US1] Add a UI scenario proving a preview changes while the page remains open in tests/WindowsCompanion.UI.Tests/SensorUiTests.cs

## Phase 4: User Story 2 - Avoid Hidden Refresh Work (P2)

**Goal**: Stop and cancel page refresh activity whenever Sensors is not actively presented.

**Independent Test**: Navigate away, hide, and minimize while a refresh is active; verify it stops and resumes immediately only when Sensors is actively presented again.

- [X] T008 [US2] Track selected view and gate refresh scheduling through ShowView in src/WindowsCompanion.App/MainWindow.xaml.cs
- [X] T009 [US2] Handle window visibility and minimized presenter changes in src/WindowsCompanion.App/MainWindow.xaml.cs
- [X] T010 [US2] Cancel refresh during tray close and shutdown paths in src/WindowsCompanion.App/MainWindow.xaml.cs
- [X] T011 [US2] Add focused lifecycle scheduling tests in tests/WindowsCompanion.App.Tests/SensorPreviewCancellationTests.cs

## Phase 5: Polish & Cross-Cutting Concerns

- [X] T012 Run targeted App and UI sensor tests using specs/019-live-sensor-previews/quickstart.md
- [X] T013 Build the supported x64 application target using scripts/run.ps1 -NoLaunch
- [X] T014 Update specs/019-live-sensor-previews/spec.md with any implementation evidence or corrected lifecycle behavior

## Dependencies

- Phase 2 depends on Phase 1.
- User Story 1 depends on Phase 2 and is the MVP.
- User Story 2 depends on User Story 1's timer/update path.
- Polish depends on both user stories.

## Parallel Opportunities

- T003 can proceed alongside MainWindow design after T002's intended contract is known.
- T006 can proceed alongside T004 because it touches the same page only at stable automation metadata locations and a separate test page object.

## Implementation Strategy

1. Establish cancellation and single-flight ownership.
2. Deliver the visible-page automatic refresh MVP.
3. Add window presentation gating and cancellation.
4. Validate lifecycle, privacy, and supported build behavior.
