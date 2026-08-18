# Implementation Plan: LAN/WLAN Identity, Gateway and DNS Sensors

**Branch**: `feature/010-lan-wlan-identity-sensors` | **Date**: 2026-08-18 |
**Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/010-lan-wlan-identity-sensors/spec.md`

## Summary

Extend the network identity sensor source with six opt-in sensors:
`lan_mac_address`, `wlan_mac_address`, `gateway_address`, `dns_servers`,
`connectivity_wifi_security`, and `connectivity_wifi_random_mac`. Uses Windows IP
Helper for permanent MAC addresses, the existing adapter snapshot for gateway/DNS,
and native WLAN profile XML for security type and randomized MAC detection.

## Technical Context

**Language/Version**: C# 13 / .NET 10

**Primary Dependencies**: `iphlpapi.dll` P/Invoke (permanent physical address);
native WLAN profile XML; existing adapter snapshot infrastructure

**Storage**: `SensorPreferences` in
`%LOCALAPPDATA%\WindowsCompanion\settings.json`

**Testing**: xUnit unit tests for MAC formatting, DNS joining, security type
mapping, randomized MAC detection, and independent capture-scope gating

**Target Platform**: Windows 10 build 19041+ and Windows 11, x64/ARM64

**Project Type**: Native Windows desktop application with a platform-agnostic core

**Performance Goals**: Same event-driven lifecycle as existing network sensors;
no additional polling; push only on change

**Constraints**: Each sensor's lookup is gated independently; enabling one never
enumerates another's value; no hardware address logged; permanent MAC reported
regardless of per-network randomization

## Project Structure

### Source Code

```text
src/WindowsCompanion.Core/Sensors/
├── MacFormatter.cs              (shared MAC format utility)
└── WifiSecurityMapper.cs        (DOT11_AUTH_ALGORITHM → display string)

src/WindowsCompanion.App/Services/
├── NetworkIdentitySensorSource.cs  (extended with new definitions)
└── WlanProfileReader.cs         (profile XML parsing for security/random MAC)
```

### Integration Points

- Extends the existing `NetworkIdentitySensorSource`
- Reuses adapter snapshot and change-driven lifecycle
- Independent capture-scope per sensor, matching existing privacy model

## Complexity Tracking

No constitution violations.
