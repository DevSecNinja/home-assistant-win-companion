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

**Scale/Scope**: Two new sensors in one Windows source

## Constitution Check

*GATE: Passed (retroactive evaluation of shipped implementation).*

- **Native Windows Experience First**: PASS — uses native WLAN API directly.
- **Security & Privacy**: PASS — both sensors default off, labelled
  location-revealing; no values logged.
- **Evidence-Driven Development**: PASS — shipped and verified.
- **Testable, Layered Architecture**: PASS — raw string values need no Core logic.
- **Resilience & Observability**: PASS — permission-denied produces safe state.

## Project Structure

### Documentation (this feature)

```text
specs/007-wifi-identifiers/
├── spec.md
├── plan.md
└── tasks.md
```

### Source Code

```text
src/WindowsCompanion.Core/Sensors/
└── WifiConnectionInfo.cs        (state formatting, permission/unavailable mapping)

src/WindowsCompanion.App/Services/
└── WifiSensorSource.cs          (WLAN P/Invoke, event subscription)
```

### Integration Points

- `AppController` registers the source
- `SensorCatalog` manages start/stop lifecycle
- Location Settings action routed through `AppController.OpenLocationSettings()`

**Structure Decision**: Core owns state formatting and permission mapping
(`WifiConnectionInfo`); App owns WLAN P/Invoke and event lifecycle. Preserve the
existing two-project layering.

## Complexity Tracking

No constitution violations.
