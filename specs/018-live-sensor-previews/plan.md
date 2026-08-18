# Implementation Plan: Live Sensor Previews

**Branch**: `devsecninja-live-sensor-previews` | **Date**: 2026-08-18 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/018-live-sensor-previews/spec.md`

## Summary

Refresh the existing sensor preview text in place every two seconds while the Sensors view is visible and the app window is neither hidden nor minimized. Reuse the catalog's privacy-gated preview operation and the existing preview cancellation coordinator. Keep each refresh single-flight, cancel it when presentation stops, and never rebuild the sensor controls during periodic updates.

## Technical Context

**Language/Version**: C# / .NET 10

**Primary Dependencies**: Windows App SDK 2.3, WinUI 3

**Storage**: N/A; previews are transient

**Testing**: xUnit app tests and existing Windows UI tests

**Target Platform**: Windows 10 build 19041+ and Windows 11

**Project Type**: Native Windows desktop application

**Performance Goals**: Refresh normally responding previews within 5 seconds; reopen previews within 2 seconds

**Constraints**: One preview read at a time; zero page-driven reads while hidden/minimized/off-page; preserve sensitive-sensor gating; no change to Home Assistant sync cadence

**Scale/Scope**: One Sensors page containing the existing catalog, currently tens of sensors

## Constitution Check

*GATE: Passed before research and after design.*

- **Native Windows Experience**: Uses existing native controls and window lifecycle events; no embedded web UI.
- **Security & Privacy**: Reuses `SensorCatalog.PreviewAsync`, where sensitive disabled sensors are gated before source reads.
- **Evidence-Driven Development**: Scope and measurable behavior are recorded in this feature directory.
- **Testable, Layered Architecture**: Presentation scheduling remains in App; sensor reading and privacy policy remain in Core.
- **Resilience & Observability**: Cancellation prevents stale UI updates; per-source preview failures retain existing isolated behavior.
- **Technology/Dependency constraints**: Uses existing platform APIs and adds no dependency.

## Project Structure

### Documentation (this feature)

```text
specs/018-live-sensor-previews/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── sensors-page.md
└── tasks.md
```

### Source Code (repository root)

```text
src/WindowsCompanion.App/
├── MainWindow.xaml.cs
├── MainWindow.Sensors.cs
└── Services/SensorPreviewCancellation.cs

tests/WindowsCompanion.App.Tests/
└── SensorPreviewCancellationTests.cs

tests/WindowsCompanion.UI.Tests/
├── SensorUiTests.cs
└── Pages/SensorsPage.cs
```

**Structure Decision**: Extend the existing WinUI Sensors page and its focused cancellation helper. Add unit coverage at the App service boundary and UI coverage only where the current test harness can observe preview text reliably.

## Complexity Tracking

No constitution violations.
