# Implementation Plan: Internal and External Home Assistant URLs

**Date**: 2026-08 (retroactive) | **Spec**: [spec.md](./spec.md)

> **Retroactive reconstruction.** This plan was written after the feature shipped,
> based on the spec and the implemented source. It was not generated before
> implementation.

## Summary

Support one default URL with opt-in dual-address routing, same-instance validation,
credential-safe probing, route selection from trusted-network rules, failover with
flap protection, and lifecycle serialization. Core owns RouteValidator, RouteSelector,
RouteSupervisor, ConnectionLifecycle, and the probe pipeline. App owns the HTTP
client, Windows network enumeration, and Settings UI.

## Files

- `src/WindowsCompanion.Core/App/RouteValidator.cs`
- `src/WindowsCompanion.Core/App/RouteSelector.cs`
- `src/WindowsCompanion.Core/App/RouteSupervisor.cs`
- `src/WindowsCompanion.Core/App/RouteProbe.cs`
- `src/WindowsCompanion.Core/App/RouteUrlPolicy.cs`
- `src/WindowsCompanion.Core/App/ConnectionLifecycle.cs`
- `src/WindowsCompanion.Core/Sensors/NetworkRouteProbe.cs`
- `src/WindowsCompanion.App/AppController.cs` (route switch wiring)
- `src/WindowsCompanion.App/MainWindow.xaml(.cs)` (Connection settings panel)
- `tests/WindowsCompanion.Core.Tests/RouteValidatorTests.cs`
- `tests/WindowsCompanion.Core.Tests/RouteSelectorTests.cs`
- `tests/WindowsCompanion.Core.Tests/RouteSupervisorTests.cs`
- `tests/WindowsCompanion.Core.Tests/RouteUrlPolicyTests.cs`
- `tests/WindowsCompanion.Core.Tests/HttpRouteProbeTests.cs`
- `tests/WindowsCompanion.Core.Tests/ConnectionLifecycleTests.cs`
- `tests/WindowsCompanion.Core.Tests/TrustedNetworkCidrTests.cs`
- `tests/WindowsCompanion.Core.Tests/TrustedNetworkSettingsTests.cs`
