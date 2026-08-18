# Implementation Plan: Notification State Sensor

**Branch**: `feature/022-notification-state-sensor` | **Date**: 2026-08-18 |
**Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/022-notification-state-sensor/spec.md`

## Summary

Add a `NotificationStateSensorSource` that wraps the shell32
`SHQueryUserNotificationState` P/Invoke. Deterministic state formatting and
suppression evaluation live in Core (`NotificationState` enum +
`NotificationStateFormatter`). The App source polls every 10 seconds and pushes
only on state change.

## Technical Context

**Language/Version**: C# 13 / .NET 10

**Primary Dependencies**: `shell32.dll` P/Invoke; `System.Timers.Timer`

**Storage**: `SensorPreferences` in
`%LOCALAPPDATA%\WindowsCompanion\settings.json`

**Testing**: xUnit unit tests for state description and suppression evaluation

**Target Platform**: Windows 10 build 19041+ and Windows 11, x64/ARM64

**Project Type**: Native Windows desktop application with a platform-agnostic core

**Performance Goals**: 10-second poll; push only on change

**Constraints**: Does not cover Focus / Do Not Disturb (no supported API); enum
values validated with `Enum.IsDefined`; failure returns Unknown

## Project Structure

### Source Code

```text
src/WindowsCompanion.Core/Sensors/
└── NotificationState.cs         (enum, formatter, suppression logic, attributes)

src/WindowsCompanion.App/Services/
└── NotificationStateSensorSource.cs  (P/Invoke, timer, change detection)
```

### Integration Points

- `AppController` registers the source
- `SensorCatalog` manages start/stop lifecycle
- Diagnostic entity category

## Complexity Tracking

No constitution violations.
