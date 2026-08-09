# Implementation Plan: WinGet Update Status

**Branch**: `feature/004-winget-updates` | **Date**: 2026-08-07 |
**Spec**: [spec.md](./spec.md)

## Summary

Add one opt-in cached sensor backed by the official `Microsoft.WinGet.Client`
PowerShell module. Core owns update-result parsing, cache state, scheduling and
sensor shaping. The App project owns Windows PowerShell process execution, module
detection/signature validation, and the setup-instructions UI. Package details
remain in memory and appear only in local preview text.

## Technical Context

**Language/Version**: C# 13 / .NET 9

**Primary Dependencies**: Windows PowerShell 5.1; optional official
`Microsoft.WinGet.Client` module installed from PowerShell Gallery

**Storage**: Existing sensor preference only; update details are memory-only

**Testing**: xUnit for structured JSON parsing, count/unavailable states, disabled
lifecycle, refresh scheduling, and local-only preview

**Target Platform**: Windows 10 build 19041+ and Windows 11

**Project Type**: Native Windows desktop application with platform-agnostic Core

**Performance Goals**: Maximum one automatic query per six hours; two-minute query
timeout; checks never block the UI thread

**Constraints**: No CLI-table parsing; no package details in HA payloads/logs; no
PowerShell process while disabled; no package installation/update functionality

**Scale/Scope**: One sensor and one optional per-user PowerShell module dependency

## Constitution Check

- **Native Windows Experience First**: PASS — confirmation and errors use native
  dialogs; PowerShell runs invisibly.
- **Security & Privacy**: PASS — the app executes only an explicitly installed,
  sufficiently recent Microsoft-signed module; inventory remains local and unlogged.
- **Evidence-Driven Development**: PASS — issue #21 and research record the rejected
  COM/CLI alternatives and dependency tradeoff.
- **Testable, Layered Architecture**: PASS — provider interface and parser/cache live
  in Core; process execution remains in App.
- **Resilience & Observability**: PASS — missing module, timeout, policy, source and
  malformed output become explicit local/unavailable states.

## Project Structure

```text
specs/004-winget-updates/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/sensor.md
└── tasks.md

src/
├── WindowsCompanion.Core/
│   ├── Abstractions/IWinGetUpdateProvider.cs
│   ├── Models/WinGetUpdateResult.cs
│   └── Sensors/WinGetUpdateSensorSource.cs
└── WindowsCompanion.App/Services/PowerShellWinGetUpdateProvider.cs

tests/WindowsCompanion.Core.Tests/WinGetUpdateTests.cs
```

Existing integration points:

- `IRefreshableSensorSource` and `SensorCatalog.RefreshAsync`
- `AppController.ForcePushAsync`
- `MainWindow.OnSensorToggled` for confirmation/setup
- `AppController` source registration

## Complexity Tracking

No constitution violations.
