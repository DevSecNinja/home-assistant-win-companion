# Implementation Plan: Selectable Sensor Catalog

**Date**: 2026-08 (retroactive) | **Spec**: [spec.md](./spec.md)

> **Retroactive reconstruction.** This plan was written after the feature shipped,
> based on the spec and the implemented source. It was not generated before
> implementation. The spec's Delivery Notes section documents the original context.

## Summary

Introduce a user-facing catalog of available sensors with per-sensor enable/disable,
privacy classification, and change-driven push. Core owns definitions, preferences,
sync orchestration, and string truncation. App owns Windows hooks, network events,
and UI.

## Files

- `src/WindowsCompanion.Core/Sensors/SensorCatalog.cs`
- `src/WindowsCompanion.Core/Sensors/SensorDefinition.cs`
- `src/WindowsCompanion.Core/Sensors/SensorPreferences.cs`
- `src/WindowsCompanion.Core/Sensors/ISensorSource.cs`
- `src/WindowsCompanion.Core/Sensors/BatterySensorSource.cs`
- `src/WindowsCompanion.App/Services/ActiveSensorSource.cs`
- `src/WindowsCompanion.App/Services/NetworkSensorSource.cs`
- `src/WindowsCompanion.App/Services/SystemSensorSource.cs`
- `src/WindowsCompanion.App/MainWindow.xaml(.cs)` (Sensors page)
- `tests/WindowsCompanion.Core.Tests/SensorCatalogLifecycleTests.cs`
- `tests/WindowsCompanion.Core.Tests/SensorLifecycleTests.cs`
- `tests/WindowsCompanion.Core.Tests/ActiveStateTests.cs`
