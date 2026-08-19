# Quickstart Validation: HA Version Display

## Prerequisites

- Windows machine with .NET 10 SDK
- Connected Home Assistant instance (any installation type)
- Companion app built from this branch

## Build

```powershell
.\scripts\run.ps1
```

## Validation Scenarios

### Scenario 1: HA Core version visible

1. Connect the companion to a Home Assistant instance
2. Open the settings page
3. **Expected**: Below the server hostname, a line reads `HA <version>` (e.g. "HA 2025.7.0")

### Scenario 2: HA OS version visible (HA OS install)

1. Connect the companion to a Home Assistant OS installation
2. Open the settings page
3. **Expected**: Line reads `HA <version> · OS <os_version>` (e.g. "HA 2025.7.0 · OS 14.2")

### Scenario 3: Non-OS install omits OS version

1. Connect to a Container or Core installation
2. Open the settings page
3. **Expected**: Line reads only `HA <version>` without OS portion

### Scenario 4: Disconnected state

1. Disconnect (turn off HA or disable network)
2. **Expected**: Version line disappears or is hidden

### Scenario 5: Reconnection updates version

1. Connect to instance running version A
2. Upgrade HA (or switch to different instance)
3. Wait for reconnection
4. **Expected**: Version line updates to reflect new version

## Unit Tests

```powershell
dotnet test --project tests\WindowsCompanion.Core.Tests\WindowsCompanion.Core.Tests.csproj -c Release -- --filter-query "/*/*/*VersionSummary*/*"
```
