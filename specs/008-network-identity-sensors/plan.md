# Implementation Plan: Network Identity Sensors

**Branch**: `feature/008-network-identity-sensors` | **Date**: 2026-08-18 |
**Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/008-network-identity-sensors/spec.md`

## Summary

Add opt-in `ipv6_address` and `mac_address` sensors derived from a shared adapter
snapshot that also serves the existing connection type and IPv4 sensors. Route
discovery uses a connected UDP socket (no packet sent) for both IPv4 and IPv6,
preferring the physical adapter carrying the active default route. Change-driven
push with deduplication avoids unnecessary Home Assistant round trips.

## Technical Context

**Language/Version**: C# 13 / .NET 10

**Primary Dependencies**: .NET `Socket` (UDP connect for route discovery);
`System.Net.NetworkInformation` for adapter enumeration; `NetworkChange` events

**Storage**: `SensorPreferences` in
`%LOCALAPPDATA%\WindowsCompanion\settings.json`

**Testing**: xUnit unit tests for IPv6 address classification, MAC formatting,
route selection, adapter filtering, change coalescing, and handle disposal

**Target Platform**: Windows 10 build 19041+ and Windows 11, x64/ARM64

**Project Type**: Native Windows desktop application with a platform-agnostic core

**Performance Goals**: Event-driven with single capture per burst; no polling;
push only when reported values change

**Constraints**: No addresses logged; one OS subscription while enabled; prefer
physical adapters over VPN/tunnel; IPv6 excludes link-local, loopback, multicast,
6to4/Teredo, temporary (RFC 4941 preferred only as fallback)

## Project Structure

### Source Code

```text
src/WindowsCompanion.Core/Sensors/
├── AdapterSnapshot.cs           (adapter model, IPv6 classification, MAC format)
├── RouteProbe.cs                (UDP-connect route resolution)
└── NetworkChangeCoalescer.cs    (dedup burst events)

src/WindowsCompanion.App/Services/
└── NetworkIdentitySensorSource.cs  (OS adapter enumeration, event hookup)
```

### Integration Points

- `AppController` registers the source
- `SensorCatalog` manages start/stop lifecycle
- Existing connection type/IPv4 sensors refactored to share the snapshot
- `SensorPreviewGate` withholds previews until enabled

## Complexity Tracking

No constitution violations.
