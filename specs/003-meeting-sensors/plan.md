# Implementation Plan: Meeting Context Sensors

**Branch**: `feature/003-meeting-sensors` | **Date**: 2026-08-07 |
**Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/003-meeting-sensors/spec.md`

## Summary

Extend the selectable catalog with Windows notification state, microphone/camera
activity, default audio output, and headset-presence sensors. Keep Windows access in
the App project, move deterministic state mapping and classification into Core, add
asynchronous local previews for sources that enumerate devices, and ensure each
poller exists only while one of its sensors is enabled.

## Technical Context

**Language/Version**: C# 13 / .NET 9

**Primary Dependencies**: Windows App SDK 2.3.1; Windows shell APIs; Windows registry;
`Windows.Devices.Enumeration` and `Windows.Media.Devices`; .NET BCL timers

**Storage**: Existing `SensorPreferences` in
`%LOCALAPPDATA%\HaCompanion\settings.json`

**Testing**: xUnit unit tests for state mapping, capability activity evaluation,
headset classification, preview behavior, and source lifecycle

**Target Platform**: Windows 10 build 19041+ and Windows 11, x64/ARM64

**Project Type**: Native Windows desktop application with a platform-agnostic core

**Performance Goals**: Poll no more often than every 10 seconds; no polling for
fully disabled sources; push only when a cached reading changes

**Constraints**: No new third-party dependency; no Teams/Graph/vendor SDK; disabled
sensors must cost no polling work; device names remain local unless enabled

**Scale/Scope**: Five new sensors grouped into three Windows sources

## Constitution Check

*GATE: Passed before and after design.*

- **Native Windows Experience First**: PASS — uses native Windows state and device
  surfaces and extends the existing native Sensors page.
- **Security & Privacy**: PASS — microphone, camera, audio-output, and headset
  context default off; previews remain local; no values are logged.
- **Evidence-Driven Development**: PASS — issue #6 records empirical constraints;
  this spec and research capture the resulting scope.
- **Testable, Layered Architecture**: PASS — deterministic mapping/evaluation lives
  in Core with tests; registry, shell, and WinRT access remains in App sources.
- **Resilience & Observability**: PASS — source failures produce safe readings and
  cannot interrupt other sensor synchronization.

## Project Structure

### Documentation (this feature)

```text
specs/003-meeting-sensors/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/sensors.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── HaCompanion.Core/Sensors/
│   ├── CapabilityActivity.cs
│   ├── HeadsetClassifier.cs
│   └── NotificationState.cs
└── HaCompanion.App/Services/
    ├── AudioDeviceSensorSource.cs
    ├── CapabilityUsageSensorSource.cs
    └── NotificationStateSensorSource.cs

tests/HaCompanion.Core.Tests/
└── MeetingSensorTests.cs
```

Existing integration points updated:

- `ISensorSource` and `SensorCatalog` for asynchronous local preview
- `MainWindow.xaml.cs` to render preview values
- `AppController` to register the three sources

**Structure Decision**: Preserve the existing two-project layering. Core owns
testable decisions; App owns Windows API access and polling lifecycles.

## Complexity Tracking

No constitution violations.
