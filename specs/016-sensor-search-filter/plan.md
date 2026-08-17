# Implementation Plan: Sensor Search Filter

**Branch**: `016-sensor-search-filter` | **Date**: 2026-08-17 | **Spec**: [spec.md](./spec.md)

## Summary

Add a search/filter text box at the top of the sensors overview panel that filters sensors by name as the user types. The implementation uses WinUI's `AutoSuggestBox` for built-in search UX and toggles `Visibility` on existing sensor card elements based on case-insensitive substring matching.

## Technical Context

**Language/Version**: C# / .NET 10

**Primary Dependencies**: Windows App SDK (WinUI 3)

**Storage**: N/A (UI-only, no persistence of filter state)

**Testing**: xUnit via `dotnet test` (Core tests); manual UI validation

**Target Platform**: Windows 10 build 19041+ / Windows 11

**Project Type**: Desktop app (WinUI 3)

**Performance Goals**: Filtering perceived as instant (<100ms) for up to 50 sensors

**Constraints**: No additional dependencies; follows existing Fluent Design patterns

**Scale/Scope**: ~30-50 sensor definitions in the list

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Native Windows Experience First | ✅ Pass | Uses WinUI AutoSuggestBox with Fluent styling |
| II. Security & Privacy | ✅ Pass | No credentials or sensitive data involved |
| III. Evidence-Driven Development | ✅ Pass | Spec and plan created under specs/ |
| IV. Testable, Layered Architecture | ✅ Pass | Filter logic is simple string matching in the UI layer; no Core changes needed |
| V. Resilience & Observability | ✅ Pass | Filter is purely UI-local; no network or lifecycle concerns |

No violations. No complexity tracking needed.

## Project Structure

### Documentation (this feature)

```text
specs/016-sensor-search-filter/
├── plan.md
├── research.md
├── quickstart.md
└── spec.md
```

### Source Code (repository root)

```text
src/WindowsCompanion.App/
├── MainWindow.xaml           # Add AutoSuggestBox to SensorsPanel header
└── MainWindow.Sensors.cs     # Add filter logic (show/hide sensor cards)
```

**Structure Decision**: This is a UI-only change in the App project. No Core library changes, no new files needed — just additions to the existing sensors panel XAML and its code-behind.
