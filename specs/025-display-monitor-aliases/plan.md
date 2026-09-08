# Implementation Plan: Display and Monitor Search Aliases

**Branch**: `devsecninja-sensor-naming-consistency` | **Date**: 2026-09-08 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/025-display-monitor-aliases/spec.md`

## Summary

Rename the monitor identification sensor's public contract to "Display
Identity" with the `display_identity` identifier and display-based state and
attribute labels. Extend sensor definitions with optional local search aliases
and include those aliases in the existing sensor-card search index, allowing the
card to match both "display" and "monitor" without exposing aliases in Home
Assistant payloads. The existing removed-sensor path retires an old persisted
`monitor_identity` registration.

## Technical Context

**Language/Version**: C# / .NET 10

**Primary Dependencies**: Windows App SDK (WinUI 3), existing sensor catalog

**Storage**: Existing sensor preferences and registered-sensor state; no schema change

**Testing**: xUnit Core tests and Windows UI automation tests

**Target Platform**: Windows 10 build 19041+ and Windows 11

**Project Type**: Native Windows desktop application

**Performance Goals**: Preserve sub-100ms filtering for fewer than 100 sensor cards

**Constraints**: Aliases remain local metadata and must not trigger sensor
collection or enter reported state/attributes; the old ID must retire through
the existing persisted registration mechanism

**Scale/Scope**: One breaking sensor contract rename, one reusable alias field,
and the existing sensor list filter

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Native Windows Experience First | Pass | Extends the existing native sensor search experience. |
| II. Security & Privacy | Pass | Aliases are static metadata and collect no sensor values. |
| III. Evidence-Driven Development | Pass | Scope and compatibility decisions are recorded in this feature specification. |
| IV. Testable, Layered Architecture | Pass | Reusable metadata remains in Core; UI behavior is covered by existing UI automation patterns. |
| V. Resilience & Observability | Pass | No network, lifecycle, or logging behavior changes. |

Post-design review: all gates still pass. No new dependency, persistence format,
protocol endpoint, or background behavior is introduced.

## Project Structure

### Documentation (this feature)

```text
specs/025-display-monitor-aliases/
├── checklists/
│   └── requirements.md
├── contracts/
│   └── sensor-search.md
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
│       ├── DisplayCapturePolicy.cs
│       ├── MonitorIdentity.cs
│       └── SensorDefinition.cs
└── WindowsCompanion.App/
    ├── MainWindow.Sensors.cs
    └── Services/DisplaySensorSource.cs

tests/
├── WindowsCompanion.Core.Tests/
│   ├── HardwareSensorTests.cs
│   ├── SensorDefinitionTests.cs
│   └── SensorSyncServiceTests.cs
├── WindowsCompanion.E2E.Tests/
│   └── CompositionContractTests.cs
└── WindowsCompanion.UI.Tests/
    ├── Pages/SensorsPage.cs
    └── SensorFilterUiTests.cs
```

**Structure Decision**: Keep alias metadata on the existing Core sensor
definition contract, consume it only while building the App's local search text,
and validate the user-visible behavior through the established UI scenario.
