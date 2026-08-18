# Implementation Plan: Hardware, Display, Environment and Storage Sensors

**Date**: 2026-08 (retroactive) | **Spec**: [spec.md](./spec.md)

> **Retroactive reconstruction.** This plan was written after the feature shipped,
> based on the spec and the implemented source. It was not generated before
> implementation.

## Summary

Add host model, display topology, dark-mode theme, locale, time zone, disk usage,
and pending-reboot sensors. Keep deterministic formatting, classification, and
change detection in Core; keep OS enumeration (registry, P/Invoke, DriveInfo) in
App sensor sources.

## Files

- `src/WindowsCompanion.Core/Sensors/DisplayTopology.cs`
- `src/WindowsCompanion.Core/Sensors/DisplayCapturePolicy.cs`
- `src/WindowsCompanion.Core/Sensors/WindowsTheme.cs`
- `src/WindowsCompanion.Core/Sensors/LocaleFormatter.cs`
- `src/WindowsCompanion.Core/Sensors/DiskUsage.cs`
- `src/WindowsCompanion.Core/Sensors/PendingReboot.cs`
- `src/WindowsCompanion.App/Services/HardwareInfo.cs`
- `src/WindowsCompanion.App/Services/DisplaySensorSource.cs`
- `src/WindowsCompanion.App/Services/WindowsThemeSensorSource.cs`
- `src/WindowsCompanion.App/Services/LocaleSensorSource.cs`
- `src/WindowsCompanion.App/Services/DiskUsageSensorSource.cs`
- `src/WindowsCompanion.App/Services/PendingRebootSensorSource.cs`
- `tests/WindowsCompanion.Core.Tests/HardwareSensorTests.cs`
- `tests/WindowsCompanion.Core.Tests/PendingRebootTests.cs`
