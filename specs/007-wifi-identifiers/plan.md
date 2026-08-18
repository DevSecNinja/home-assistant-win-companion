# Implementation Plan: Wi-Fi Identifiers

**Branch**: `feature/007-wifi-identifiers` | **Date**: 2026-08-18 |
**Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/007-wifi-identifiers/spec.md`

## Summary

Add opt-in `connectivity_ssid` and `connectivity_bssid` sensors by calling the
native Windows WLAN API from an unpackaged desktop process, handling the Location
permission gate, and observing network change events only while enabled.

## Technical Context

**Language/Version**: C# 13 / .NET 10

**Primary Dependencies**: `wlanapi.dll` P/Invoke; `NetworkChange` events

**Storage**: `SensorPreferences` in
`%LOCALAPPDATA%\WindowsCompanion\settings.json`

**Testing**: xUnit unit tests for Unicode SSID formatting, BSSID formatting, and
permission-denied states

**Target Platform**: Windows 10 build 19041+ and Windows 11, x64/ARM64

**Project Type**: Native Windows desktop application with a platform-agnostic core

**Performance Goals**: Event-driven only while enabled; no polling; push only on
actual value change

**Constraints**: Requires Windows Location permission; no fallback to
`Wlan*` without permission; disabled means zero OS interaction

## Project Structure

### Source Code

```text
src/WindowsCompanion.App/Services/
└── WifiSensorSource.cs          (WLAN P/Invoke, event subscription)

src/WindowsCompanion.Core/Sensors/
└── (no Core logic needed — raw string values)
```

### Integration Points

- `AppController` registers the source
- `SensorCatalog` manages start/stop lifecycle
- Location Settings direct action added to sensor definitions

## Complexity Tracking

No constitution violations.
