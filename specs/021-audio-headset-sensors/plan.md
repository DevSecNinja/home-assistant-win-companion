# Implementation Plan: Audio/Headset Sensors

**Branch**: `feature/021-audio-headset-sensors` | **Date**: 2026-08-18 |
**Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/021-audio-headset-sensors/spec.md`

## Summary

Add an `AudioDeviceSensorSource` that exposes `audio_output` (string sensor) and
`headset_connected` (binary sensor). Deterministic headset classification lives in
Core via `HeadsetClassifier`; Windows device enumeration stays in the App project.
A 10-second poll loop with change detection ensures push only on actual state
change.

## Technical Context

**Language/Version**: C# 13 / .NET 10

**Primary Dependencies**: `Windows.Devices.Enumeration`;
`Windows.Media.Devices.MediaDevice`; .NET `PeriodicTimer`

**Storage**: `SensorPreferences` in
`%LOCALAPPDATA%\WindowsCompanion\settings.json`

**Testing**: xUnit unit tests for headset keyword classification

**Target Platform**: Windows 10 build 19041+ and Windows 11, x64/ARM64

**Project Type**: Native Windows desktop application with a platform-agnostic core

**Performance Goals**: 10-second poll interval; push only on change; no device
enumeration while disabled

**Constraints**: No third-party audio SDK; COM failures produce safe empty
readings; device names never logged

## Project Structure

### Source Code

```text
src/WindowsCompanion.Core/Sensors/
└── HeadsetClassifier.cs         (keyword matching logic)

src/WindowsCompanion.App/Services/
└── AudioDeviceSensorSource.cs   (WinRT device enumeration, poll loop)
```

### Integration Points

- `AppController` registers the source
- `SensorCatalog` manages start/stop lifecycle
- Supports async preview for device enumeration

## Complexity Tracking

No constitution violations.
