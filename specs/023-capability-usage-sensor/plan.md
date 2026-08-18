# Implementation Plan: Capability Usage Sensors (Camera/Microphone)

**Branch**: `feature/023-capability-usage-sensor` | **Date**: 2026-08-18 |
**Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/023-capability-usage-sensor/spec.md`

## Summary

Add a `CapabilityUsageSensorSource` that reads Windows registry
`CapabilityAccessManager\ConsentStore` entries to detect active microphone and
camera usage. Core contains `CapabilityActivity.IsActive` for deterministic
evaluation; the App source handles recursive registry traversal, error isolation,
and 1-second polling via `SensorPollLoop` with `ChangeGate<T>` deduplication.

## Technical Context

**Language/Version**: C# 13 / .NET 10

**Primary Dependencies**: `Microsoft.Win32.Registry`; `SensorPollLoop`;
`ChangeGate<T>`

**Storage**: `SensorPreferences` in
`%LOCALAPPDATA%\WindowsCompanion\settings.json`

**Testing**: xUnit unit tests for capability activity evaluation; injectable
`readCapability` delegate for testable source behavior

**Target Platform**: Windows 10 build 19041+ and Windows 11, x64/ARM64

**Project Type**: Native Windows desktop application with a platform-agnostic core

**Performance Goals**: 1-second poll for responsive on-air detection; push only on
state change; no registry access while disabled

**Constraints**: No app names exposed; registry errors skipped per entry;
`SensorPollLoop` ensures single-flight; testable via injected delegate

## Project Structure

### Source Code

```text
src/WindowsCompanion.Core/Sensors/
└── CapabilityActivity.cs        (IsActive evaluation logic)

src/WindowsCompanion.App/Services/
└── CapabilityUsageSensorSource.cs  (registry traversal, poll loop, change gate)
```

### Integration Points

- `AppController` registers the source
- `SensorCatalog` manages start/stop lifecycle
- Uses `SensorPollLoop` and `ChangeGate<T>` from the sensor infrastructure

## Complexity Tracking

No constitution violations.
