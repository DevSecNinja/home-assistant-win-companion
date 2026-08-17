# Implementation Plan: HA Version Display

**Branch**: `016-ha-version-display` | **Date**: 2026-08-17 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/016-ha-version-display/spec.md`

## Summary

Display the Home Assistant Core version (and HA OS version when available) in the
settings page connection status card. The `get_config` webhook already returns the
HA Core version in `HaInstanceInfo.Version`. The HA OS version is not yet captured
but is present in the same response for OS installations. The feature adds an
`OsVersion` field, surfaces both through `AppController`, and renders them under
the "Connected" label.

## Technical Context

**Language/Version**: C# / .NET 10

**Primary Dependencies**: WinUI 3, Windows App SDK, System.Text.Json

**Storage**: `%LOCALAPPDATA%\WindowsCompanion\settings.json` (non-secret state)

**Testing**: xUnit via `dotnet test` (WindowsCompanion.Core.Tests)

**Target Platform**: Windows (x64, ARM64)

**Project Type**: Desktop app (WinUI 3)

**Performance Goals**: UI update within single frame; no additional network calls

**Constraints**: Version info already arrives via `get_config` webhook — no extra
HTTP round-trip. Must not block UI thread.

**Scale/Scope**: Single new property on existing model, one new UI text element,
controller property exposure.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Native Windows Experience First | ✅ Pass | Fluent text in existing card |
| II. Security & Privacy | ✅ Pass | Version strings are not sensitive |
| III. Evidence-Driven Development | ✅ Pass | Spec written, plan documents HA protocol |
| IV. Testable, Layered Architecture | ✅ Pass | Core model + App UI; unit-testable |

No violations. Complexity Tracking not required.

## Project Structure

### Documentation (this feature)

```text
specs/016-ha-version-display/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
└── tasks.md
```

### Source Code (repository root)

```text
src/WindowsCompanion.Core/
├── Models/HaInstanceInfo.cs          # Add OsVersion property
├── App/AppController.cs              # Expose InstanceVersion property
└── App/ConnectionManager.cs          # Store version after probe

src/WindowsCompanion.App/
├── MainWindow.xaml                   # New TextBlock for version
└── MainWindow.StatusPreferences.cs   # Bind version text

tests/WindowsCompanion.Core.Tests/
└── (version formatting tests)
```

**Structure Decision**: Existing layered architecture — model change in Core,
display in App shell. No new projects needed.
