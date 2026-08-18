# Implementation Plan: Time Zone Offset Attribute

**Branch**: `devsecninja-add-timezone-offset` | **Date**: 2026-08-18 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/020-time-zone-offset/spec.md`

## Summary

Add a calculation-friendly `utc_offset_seconds` attribute to the existing Time
Zone sensor without changing its state or identity. Capture the current offset
alongside the locale and time-zone name, include it in change detection, and
schedule the next offset transition while the sensor is enabled. Keep
deterministic offset and transition calculation in Core for unit testing.

## Technical Context

**Language/Version**: C# / .NET 10

**Primary Dependencies**: .NET BCL `TimeZoneInfo`; existing WinUI 3 application services

**Storage**: N/A; the attribute is calculated for each sensor reading

**Testing**: xUnit in `WindowsCompanion.Core.Tests`

**Target Platform**: Windows 10 build 19041+ and Windows 11

**Project Type**: Windows desktop companion application with a platform-independent Core library

**Performance Goals**: Constant-time offset calculation during sensor reads and one scheduled wake-up per offset transition; no periodic polling

**Constraints**: Preserve the `time_zone` entity state and identity; use the offset at the reading instant; transmit a JSON integer

**Scale/Scope**: One new attribute on one existing diagnostic sensor

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Native Windows Experience**: PASS. The existing native sensor source remains
  event driven and gains no web UI or cross-platform abstraction.
- **Security and Privacy**: PASS. A UTC offset is benign and no secrets, network
  access, or logging are introduced.
- **Evidence-Driven Development**: PASS. The behavior and payload contract are
  recorded under `specs/020-time-zone-offset/`.
- **Testable, Layered Architecture**: PASS. Deterministic offset calculation is
  placed in Core and covered with unit tests; the App service only reads Windows
  state and maps it to a sensor.
- **Resilience and Observability**: PASS. Existing exception boundaries and
  change-driven synchronization remain intact.

Post-design re-check: PASS. The data model and contract preserve these boundaries
and introduce no constitution violation.

## Project Structure

### Documentation (this feature)

```text
specs/020-time-zone-offset/
├── checklists/
│   └── requirements.md
├── contracts/
│   └── time-zone-sensor.md
├── data-model.md
├── plan.md
├── quickstart.md
├── research.md
├── spec.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── WindowsCompanion.Core/
│   └── Sensors/
│       └── LocaleFormatter.cs
└── WindowsCompanion.App/
    └── Services/
        └── LocaleSensorSource.cs

tests/
└── WindowsCompanion.Core.Tests/
    └── HardwareSensorTests.cs
```

**Structure Decision**: Extend the existing Core locale/time-zone formatter with
the deterministic calculation and keep Windows state access in the existing App
sensor source. Add focused tests to the established hardware sensor test class.
