# Implementation Plan: Location Sensor

**Branch**: `013-location-sensor` | **Date**: 2026-08-14 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/013-location-sensor/spec.md`

## Summary

Add an opt-in "Location" sensor that reports the Windows PC's current
latitude/longitude (with a horizontal-accuracy attribute) to Home Assistant,
sourced from the Windows Geolocation platform (`Windows.Devices.Geolocation`).
The sensor follows the existing opt-in, privacy-labeled, periodically-polled
sensor pattern (`WinGetUpdateSensorSource` for the poll-loop/provider split,
`WifiSensorSource`/`DomainSensorSource` for the "sensitive, off-by-default,
direct-link-to-Windows-settings-when-unavailable" UX). Core logic (result
model, poll-driven sensor source) lives in `WindowsCompanion.Core` behind an
`ILocationProvider` abstraction; the concrete Windows Geolocation query lives in
`WindowsCompanion.App`, keeping Core unit-testable without WinUI/WinRT.

## Technical Context

**Language/Version**: C# 13 / .NET 10

**Primary Dependencies**: Windows App SDK (WinUI 3) 2.3.1; `Windows.Devices.Geolocation` (Windows Runtime, via the Windows SDK projection already used for `Windows.Devices.Enumeration`/`Windows.Media.Devices` in `AudioDeviceSensorSource`)

**Storage**: N/A - no new persisted state; reuses existing `SensorPreferences`/`RegisteredSensors` enable/disable and registration bookkeeping

**Testing**: xUnit (`WindowsCompanion.Core.Tests`) for the sensor source/result model against a fake `ILocationProvider`; the real `Windows.Devices.Geolocation` call is exercised only manually/E2E, consistent with `PowerShellWinGetUpdateProvider` (untested provider shell) vs. `WinGetUpdateSensorSource` (fully unit-tested source)

**Target Platform**: Windows 10 19041+ / Windows 11 desktop, x64 and ARM64 (existing app targets)

**Project Type**: Desktop app (WinUI 3) with a platform-agnostic core library - existing `WindowsCompanion.Core` / `WindowsCompanion.App` / `WindowsCompanion.Core.Tests` / `WindowsCompanion.E2E.Tests` layout

**Performance Goals**: Not latency-sensitive; one location query per poll interval, comparable in cost to the existing WinGet-update poll (network/OS-bound). Both WinRT calls (`RequestAccessAsync`, `GetGeopositionAsync`) are marshalled onto the UI `DispatcherQueue`, since `Geolocator` requires the calling thread to be foregrounded for the permission prompt; the poll interval is long enough that this brief hop does not create UI jank.

**Constraints**: MUST NOT query location while the sensor is disabled (Core Principle II privacy); MUST NOT log a coordinate (benign vs. sensitive logging rule in `SensorDefinition.Loggable`); MUST cancel an in-flight query on disable/shutdown (existing `SensorPollLoop`/`IRefreshableSensorSource` contract already guarantees this once the source is built on top of it)

**Scale/Scope**: Single new sensor definition (`location`), one new Core abstraction, one new Core sensor source, one new App-side provider, wiring through `ProductionSensorComposition`/`ProductionAppComposition`/`TestAppComposition`, and reuse of the existing Sensors-page "open Location settings" action that already mirrors the Wi-Fi one - no appxmanifest change, since `research.md` decides against declaring a `location` capability for this unpackaged app

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Native Windows Experience First**: PASS. Uses the native Windows Geolocation
  API and existing Sensors-page UI patterns; no web/browser embedding involved.
- **II. Security & Privacy of Credentials (NON-NEGOTIABLE)**: PASS. No credentials
  involved. Applies the same privacy discipline used for Wi-Fi identifiers: the
  new sensor is `SensorPrivacy.Sensitive`, off by default, never queried or
  logged while disabled (enforced by the existing `ISensorSource`/
  `SensorPreviewGate` contracts), and TLS/network behavior is unchanged.
- **III. Evidence-Driven Development**: PASS. This spec/plan/tasks set is being
  produced before implementation, sized proportionally to a small-to-medium
  feature (similar to `007-wifi-identifiers`/`004-winget-updates`).
- **IV. Testable, Layered Architecture**: PASS. `ILocationProvider` (Core
  abstraction) + `LocationSensorSource` (Core, unit-testable with a fake
  provider) + `WindowsLocationProvider` (App, thin WinRT call) mirrors the
  `IWinGetUpdateProvider`/`WinGetUpdateSensorSource`/
  `PowerShellWinGetUpdateProvider` split exactly.
- **V. Resilience & Observability**: PASS. Reuses `SensorPollLoop` for
  single-flight, cancellable polling and graceful failure handling; unavailable
  states are surfaced as a clear sensor state rather than an exception.

No violations requiring justification; Complexity Tracking is not filled in.

## Project Structure

### Documentation (this feature)

```text
specs/013-location-sensor/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
src/
├── WindowsCompanion.Core/
│   ├── Abstractions/
│   │   └── ILocationProvider.cs        # NEW - GetLocationAsync contract
│   ├── Models/
│   │   └── LocationResult.cs           # NEW - status + lat/long/accuracy/timestamp
│   └── Sensors/
│       └── LocationSensorSource.cs     # NEW - ISensorSource + IRefreshableSensorSource
├── WindowsCompanion.App/
│   ├── Services/
│   │   └── WindowsLocationProvider.cs  # NEW - Windows.Devices.Geolocation query
│   ├── ProductionSensorComposition.cs  # EDIT - register LocationSensorSource
│   ├── ProductionAppComposition.cs     # EDIT - construct WindowsLocationProvider
│   ├── AppControllerDependencies.cs    # EDIT - add Location provider dependency
│   └── TestAppComposition.cs           # EDIT - add a no-op location provider
tests/
└── WindowsCompanion.Core.Tests/
    └── LocationSensorSourceTests.cs    # NEW - fake-provider unit tests
```

**Structure Decision**: Follows the existing single-solution layout
(`WindowsCompanion.Core` platform-agnostic library + `WindowsCompanion.App` WinUI
shell + `WindowsCompanion.Core.Tests`), adding exactly the files needed to
mirror the already-established WinGet-updates (poll loop/provider split) and
Wi-Fi identifiers (sensitive, off-by-default) patterns. The Sensors page
already renders one row per `catalog.Definitions` entry generically
(`MainWindow.Sensors.cs`), and it already has a "Windows location access" /
"Windows settings" card wired to `AppController.OpenLocationSettings()`
(`ms-settings:privacy-location`) for the Wi-Fi SSID/BSSID sensors - the Location
sensor reuses both with only a one-line copy tweak in `MainWindow.xaml` (the
card's description text) so no new Sensors-page UI or action is required. No
new projects are introduced.

## Complexity Tracking

*No Constitution Check violations - table intentionally left empty.*
