# Tasks: Meeting Context Sensors

**Input**: Design documents from `/specs/003-meeting-sensors/`

## Phase 1: Setup

- [x] T001 Add meeting-sensor test coverage skeleton in `tests/HaCompanion.Core.Tests/MeetingSensorTests.cs`

## Phase 2: Foundational

- [x] T002 Add asynchronous local preview support to `src/HaCompanion.Core/Sensors/ISensorSource.cs` and `src/HaCompanion.Core/Sensors/SensorCatalog.cs`
- [x] T003 Render preview values asynchronously in `src/HaCompanion.App/MainWindow.xaml.cs`

## Phase 3: User Story 1 - Presenting and notification state

**Independent Test**: Full-screen, presentation, and normal states map to stable
sensor values and source polling starts/stops with enablement.

- [x] T004 [P] [US1] Implement notification-state mapping in `src/HaCompanion.Core/Sensors/NotificationState.cs`
- [x] T005 [P] [US1] Add notification-state mapping tests in `tests/HaCompanion.Core.Tests/MeetingSensorTests.cs`
- [x] T006 [US1] Implement shell polling source in `src/HaCompanion.App/Services/NotificationStateSensorSource.cs`
- [x] T007 [US1] Register Notification State in `src/HaCompanion.App/AppController.cs`

## Phase 4: User Story 2 - Microphone and camera use

**Independent Test**: Active and stopped access-history entries produce the expected
binary readings across packaged and non-packaged registry branches.

- [x] T008 [P] [US2] Implement capability activity evaluation in `src/HaCompanion.Core/Sensors/CapabilityActivity.cs`
- [x] T009 [P] [US2] Add capability activity tests in `tests/HaCompanion.Core.Tests/MeetingSensorTests.cs`
- [x] T010 [US2] Implement registry polling source in `src/HaCompanion.App/Services/CapabilityUsageSensorSource.cs`
- [x] T011 [US2] Register microphone and camera sensors in `src/HaCompanion.App/AppController.cs`

## Phase 5: User Story 3 - Audio output and headset

**Independent Test**: Default output names and endpoint collections produce correct
audio-output and headset readings without vendor-specific dependencies.

- [x] T012 [P] [US3] Implement headset classification in `src/HaCompanion.Core/Sensors/HeadsetClassifier.cs`
- [x] T013 [P] [US3] Add headset classification tests in `tests/HaCompanion.Core.Tests/MeetingSensorTests.cs`
- [x] T014 [US3] Implement asynchronous audio endpoint source in `src/HaCompanion.App/Services/AudioDeviceSensorSource.cs`
- [x] T015 [US3] Register audio sensors in `src/HaCompanion.App/AppController.cs`

## Phase 6: Polish and cross-cutting

- [x] T016 Verify polling lifecycle and preview behavior in `tests/HaCompanion.Core.Tests/MeetingSensorTests.cs`
- [x] T017 Update shipped sensor documentation in `README.md` and `specs/002-sensor-catalog/spec.md`
- [x] T018 Run the feature quickstart build and unit tests from `specs/003-meeting-sensors/quickstart.md`

## Dependencies

- T002 blocks T003 and T014.
- US1, US2, and US3 can be implemented independently after Phase 2.
- T007, T011, and T015 update the same integration file and should run sequentially.
- T016-T018 follow all user-story phases.

## Parallel opportunities

- T004/T005, T008/T009, and T012/T013 operate in separate Core/test concerns.
- US1 and US2 platform sources touch different files.

## Implementation strategy

1. Complete async preview support.
2. Ship Notification State as the independently useful MVP.
3. Add capability-use sensors.
4. Add audio-equipment context.
5. Validate lifecycle, documentation, build, and tests.
