# Quickstart: Validate WireGuard Status

## Prerequisites

- Windows 10 or 11 with the official WireGuard for Windows client.
- A configured test tunnel that can be activated and deactivated.
- Run the companion normally, without elevation.
- Use only fake `.example` or `.local` endpoints if test configuration is required.

## Automated validation

Run the targeted tests:

```powershell
dotnet test --project tests\WindowsCompanion.Core.Tests\WindowsCompanion.Core.Tests.csproj -c Release -- --filter-query "/*/*/WireGuardStatusTests/*"
dotnet test --project tests\WindowsCompanion.E2E.Tests\WindowsCompanion.E2E.Tests.csproj -c Release -- --filter-query "/*/*/*WireGuard*/*" --filter-query "/*/*/CompositionContractTests/Production_composition_includes_one_opt_in_wireguard_source"
```

Build the app without launching it:

```powershell
.\scripts\run.ps1 -NoLaunch
```

Expected: classification, filtering, lifecycle, failure, privacy, and composition
tests pass; the app builds without a new dependency.

## Manual non-administrator validation

1. Start the companion normally and confirm Task Manager does not show it elevated.
2. In sensor settings, find **WireGuard Status**. Confirm it is disabled by default
   and its description states that it checks local tunnel readiness, not handshakes.
3. Enable the sensor while WireGuard is installed but the tunnel is inactive.
   Confirm the preview and Home Assistant state are `disconnected`.
4. Activate the tunnel. Confirm the state becomes `connected` without an elevation
   prompt and no later than the next normal sync.
5. Deactivate the tunnel. Confirm the state returns to `disconnected`.
6. Disable the sensor. Confirm subsequent WireGuard changes cause no immediate sensor
   synchronization.

## Privacy validation

Inspect the Home Assistant entity, companion diagnostics, and settings persistence.
Confirm none contains tunnel names, keys, endpoints, addresses, adapter identifiers,
or traffic counters. The complete published shape is defined in
[contracts/sensor-contract.md](contracts/sensor-contract.md).

## Performance validation

Run a Release-build test probe that performs at least 1,000 sequential status
observations under a non-elevated token. Record process CPU time immediately before
and after the batch, divide consumed CPU time by elapsed wall-clock time and logical
processor count, and confirm average CPU is below 0.1% at the normal sensor interval.

After the batch, leave the enabled source idle for five minutes without network
changes. Confirm it performs no additional observations and consumes no measurable
CPU beyond ordinary process noise. Disable the sensor and confirm the probe receives
no observations or callbacks after subsequent network changes.

## Failure validation

With a fake probe in the automated tests, simulate access denial and incomplete
service or adapter enumeration. Confirm the state is `unavailable`, no exception
escapes the source, and later successful observations recover normally.
