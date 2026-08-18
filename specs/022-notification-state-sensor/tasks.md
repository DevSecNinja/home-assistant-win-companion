# Tasks: Notification State Sensor

**Input**: Design documents from `/specs/022-notification-state-sensor/`

## Phase 1: Core state mapping

**Independent Test**: Every defined notification state maps to the expected
description and suppression evaluation.

- [x] T001 [P] [US1] Implement `NotificationState` enum in `src/WindowsCompanion.Core/Sensors/NotificationState.cs`
- [x] T002 [P] [US1] Implement `NotificationStateFormatter` with `Describe`, `SuppressesNotifications`, and `BuildAttributes`
- [x] T003 [P] [US1] Add Core tests for state description and suppression in `tests/WindowsCompanion.Core.Tests/MeetingSensorTests.cs`

## Phase 2: User Story 1 - Windows notification state

**Independent Test**: Full-screen, presentation, and normal states produce correct
sensor values and polling starts/stops with enablement.

- [x] T004 [US1] Implement `NotificationStateSensorSource` with P/Invoke and timer in `src/WindowsCompanion.App/Services/NotificationStateSensorSource.cs`
- [x] T005 [US1] Register sensor in `src/WindowsCompanion.App/AppController.cs`

## Dependencies

- T001–T003 are required before T004.
- T005 follows T004.
