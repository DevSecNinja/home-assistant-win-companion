# Tasks: Audio/Headset Sensors

**Input**: Design documents from `/specs/021-audio-headset-sensors/`

## Phase 1: Core classification

- [x] T001 [P] [US2] Implement headset classification in `src/WindowsCompanion.Core/Sensors/HeadsetClassifier.cs`
- [x] T002 [P] [US2] Add headset classification tests in `tests/WindowsCompanion.Core.Tests/MeetingSensorTests.cs`

## Phase 2: User Story 1 - Audio output

**Independent Test**: Change the default audio output and verify the sensor
reports the new device name.

- [x] T003 [US1] Add `audio_output` sensor definition in `src/WindowsCompanion.App/Services/AudioDeviceSensorSource.cs`
- [x] T004 [US1] Implement WinRT device enumeration and poll loop

## Phase 3: User Story 2 - Headset presence

**Independent Test**: Connect and disconnect a headset-class endpoint and verify
the binary state follows.

- [x] T005 [US2] Add `headset_connected` sensor definition with keyword-based detection
- [x] T006 [US2] Add async preview support for device enumeration

## Phase 4: Integration

- [x] T007 Register audio sensors in `src/WindowsCompanion.App/AppController.cs`

## Dependencies

- T001–T002 are required before T005.
- T003–T004 and T005–T006 can run in parallel after Phase 1.
- T007 follows all sensor definitions.
