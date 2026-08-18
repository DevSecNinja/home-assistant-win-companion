# Implementation Plan: WireGuard Sensors

**Branch**: `devsecninja-wireguard-sensors` | **Date**: 2026-08-17 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/018-wireguard-sensors/spec.md`

## Summary

Add one opt-in diagnostic sensor that reports aggregate local WireGuard status as
`connected`, `disconnected`, or `unavailable`. The Windows shell will query the
Service Control Manager and network-interface inventory directly, matching running
`WireGuardTunnel$...` services to operational adapters whose driver description is
exactly `WireGuard Tunnel`. The source will reuse the existing network-change watcher,
deduplicate event bursts, and expose no tunnel names or configuration data.

## Technical Context

**Language/Version**: C# / .NET 10

**Primary Dependencies**: .NET BCL, Windows Service Control Manager (`advapi32`), existing sensor catalog and network-change watcher

**Storage**: Existing sensor preferences and registered-sensor persistence; no new storage

**Testing**: xUnit Core and Windows E2E test projects

**Target Platform**: Windows 10 build 17763+ and Windows 11, x64 and ARM64

**Project Type**: Native Windows desktop application with a platform-independent Core library

**Performance Goals**: Less than 0.1% average CPU, no work while disabled, no sustained work between normal reads

**Constraints**: No elevation, subprocess, additional package, configuration access, sensitive output, or handshake claim

**Scale/Scope**: One aggregate status sensor across all official WireGuard for Windows tunnels

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Native Windows Experience First**: PASS. The feature uses native Windows state and the existing settings/sensor UI.
- **Security & Privacy**: PASS. It reads no configuration, credentials, endpoints, addresses, or keys and emits no tunnel identity.
- **Evidence-Driven Development**: PASS. Local non-administrator tests and official WireGuard behavior are recorded in [research.md](research.md).
- **Testable, Layered Architecture**: PASS. Deterministic state and lifecycle behavior remain independently testable; Windows enumeration stays behind an injected probe.
- **Resilience & Observability**: PASS. Access failures map to `unavailable`; diagnostics contain no sensitive values.
- **Dependency Constraint**: PASS. The design uses the BCL and Windows APIs only.

Post-design re-check: PASS. The data model and sensor contract preserve all gates without exceptions.

## Project Structure

### Documentation (this feature)

```text
specs/018-wireguard-sensors/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── sensor-contract.md
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── WindowsCompanion.Core/
│   └── Sensors/
│       └── WireGuardStatus.cs
└── WindowsCompanion.App/
    ├── ProductionSensorComposition.cs
    └── Services/
        ├── WindowsWireGuardStatusProbe.cs
        └── WireGuardSensorSource.cs

tests/
├── WindowsCompanion.Core.Tests/
│   └── WireGuardStatusTests.cs
└── WindowsCompanion.E2E.Tests/
    └── WireGuardSensorSourceTests.cs
```

**Structure Decision**: Keep classification and public status values in Core. Keep
Service Control Manager interop, network-interface enumeration, source composition,
and Windows lifecycle tests in App/E2E. No project or dependency is added.

## Complexity Tracking

No constitution violations require justification.
