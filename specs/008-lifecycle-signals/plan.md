# Implementation Plan: Lifecycle Signals

**Date**: 2026-08 (retroactive) | **Spec**: [spec.md](./spec.md)

> **Retroactive reconstruction.** This plan was written after the feature shipped,
> based on the spec and the implemented source. It was not generated before
> implementation.

## Summary

Detect sleep, sign-out and shutdown from Windows messages, record transitions in a
local journal before attempting delivery, and replay unacknowledged transitions after
reconnection. Keep the state machine, deduplication, journal and recovery in Core;
keep the WM_POWERBROADCAST/WM_ENDSESSION hook thin in App.

## Files

- `src/WindowsCompanion.Core/Lifecycle/LifecycleSignal.cs`
- `src/WindowsCompanion.Core/Lifecycle/LifecycleTransition.cs`
- `src/WindowsCompanion.Core/Lifecycle/LifecycleTracker.cs`
- `src/WindowsCompanion.Core/Lifecycle/LifecycleJournal.cs`
- `src/WindowsCompanion.Core/Lifecycle/LifecycleCoordinator.cs`
- `src/WindowsCompanion.Core/Lifecycle/LifecycleSensorSource.cs`
- `src/WindowsCompanion.Core/Lifecycle/LifecycleSensorAdvisory.cs`
- `src/WindowsCompanion.Core/Lifecycle/ILifecycleSignalSource.cs`
- `src/WindowsCompanion.App/Services/WindowsLifecycleSignalSource.cs`
- `tests/WindowsCompanion.Core.Tests/LifecycleTests.cs`
- `tests/WindowsCompanion.Core.Tests/WindowsLifecycleSignalSourceTests.cs`
