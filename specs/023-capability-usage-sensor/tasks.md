# Tasks: Capability Usage Sensors (Camera/Microphone)

**Input**: Design documents from `/specs/023-capability-usage-sensor/`

## Phase 1: Core evaluation

**Independent Test**: Active and stopped `LastUsedTimeStop` values produce the
expected boolean readings.

- [x] T001 [P] [US1,US2] Implement `CapabilityActivity.IsActive` in `src/WindowsCompanion.Core/Sensors/CapabilityActivity.cs`
- [x] T002 [P] [US1,US2] Add capability activity tests in `tests/WindowsCompanion.Core.Tests/MeetingSensorTests.cs`

## Phase 2: User Story 1 - Microphone use

**Independent Test**: Start and stop microphone-using applications and verify
the binary state follows.

- [x] T003 [US1] Add `microphone` sensor definition in `src/WindowsCompanion.App/Services/CapabilityUsageSensorSource.cs`
- [x] T004 [US1] Implement recursive registry traversal for `microphone` consent store

## Phase 3: User Story 2 - Camera use

**Independent Test**: Start and stop camera-using applications and verify the
binary state follows.

- [x] T005 [US2] Add `camera` sensor definition in `src/WindowsCompanion.App/Services/CapabilityUsageSensorSource.cs`
- [x] T006 [US2] Share registry traversal with configurable capability name (`webcam`)

## Phase 4: Integration

- [x] T007 Integrate `SensorPollLoop` and `ChangeGate<T>` for 1-second change-driven push
- [x] T008 Register sensors in `src/WindowsCompanion.App/AppController.cs`

## Dependencies

- T001–T002 are required before T003–T006.
- T003–T004 and T005–T006 can run in parallel after Phase 1.
- T007–T008 follow all sensor definitions.
