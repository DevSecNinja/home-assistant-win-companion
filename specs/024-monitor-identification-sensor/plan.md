# Implementation Plan: Monitor Identification Sensor

**Branch**: `devsecninja-monitor-identification-sensor` | **Date**: 2026-09-07 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/024-monitor-identification-sensor/spec.md`

## Summary

Add one opt-in `monitor_identity` sensor to the existing display sensor source.
The source will enrich the current active-display CCD query with target device
names and valid EDID manufacturer/product identifiers, then delegate
deduplication, ordering, fallback formatting, and Home Assistant attributes to
deterministic Core types. Existing display count and resolution behavior remains
unchanged, and identity data is captured only when the new sensor is enabled or
explicitly previewed.

## Technical Context

**Language/Version**: C# / .NET 10

**Primary Dependencies**: .NET BCL, Windows App SDK / WinUI 3, supported Win32
Connecting and Configuring Displays (CCD) APIs

**Storage**: Existing sensor preferences and registered-sensor persistence; no
new storage

**Testing**: xUnit Core unit tests plus x64 and ARM64 application builds

**Target Platform**: Windows 10 build 19041+ and Windows 11

**Project Type**: Native Windows desktop companion application

**Performance Goals**: Capture identity in the existing display-change/read path;
reflect topology changes within 15 seconds without continuous polling

**Constraints**: Disabled means zero identity collection; no WMI, subprocess,
network, third-party package, EDID serial, device path, or identity logging;
state remains below Home Assistant's 255-character limit

**Scale/Scope**: One sensor entity per Windows device, with a bounded structured
list supporting at least 0, 1, 2, 4, and 8 active physical displays

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Native Windows Experience First**: Pass. Uses supported Windows display APIs
  inside the existing native display source; no embedded web UI or cross-platform
  abstraction is introduced.
- **Security & Privacy of Credentials**: Pass. No credentials are involved.
  Monitor identity is opt-in, preview-gated, not logged, and excludes serial
  numbers and device paths from output.
- **Evidence-Driven Development**: Pass. This specification and its research,
  contract, data model, tasks, and quickstart record the user-visible and Windows
  protocol decisions. The older hardware-sensor specification will be corrected
  when behavior changes.
- **Testable, Layered Architecture**: Pass. Windows enumeration stays in App;
  identity modeling, normalization, ordering, deduplication, and formatting stay
  in Core with happy-path and failure/empty-path unit coverage.
- **Resilience & Observability**: Pass. Supported API failures produce an explicit
  unavailable reading rather than a false headless result, while logs contain no
  monitor identity values.
- **Post-design re-check**: Pass. Phase 1 preserves the same boundaries and adds
  no constitutional exception.

## Project Structure

### Documentation (this feature)

```text
specs/024-monitor-identification-sensor/
|-- plan.md
|-- research.md
|-- data-model.md
|-- quickstart.md
|-- contracts/
|   `-- home-assistant-sensor.md
`-- tasks.md
```

### Source Code (repository root)

```text
src/
|-- WindowsCompanion.Core/
|   `-- Sensors/
|       |-- DisplayCapturePolicy.cs
|       |-- DisplayTopology.cs
|       `-- MonitorIdentity.cs
`-- WindowsCompanion.App/
    `-- Services/
        `-- DisplaySensorSource.cs

tests/
`-- WindowsCompanion.Core.Tests/
    `-- HardwareSensorTests.cs
```

**Structure Decision**: Extend the existing display source because it already
owns display topology enumeration, privacy-scoped collection, and
`DisplaySettingsChanged` lifecycle. Add only deterministic identity and payload
logic to Core so tests require no attached monitor or Windows API access.

## Complexity Tracking

No constitutional violations require justification.
