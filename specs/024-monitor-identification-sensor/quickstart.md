# Quickstart: Validate Monitor Identification

## Prerequisites

- Windows 10 build 19041+ or Windows 11.
- .NET 10 SDK, matching Windows SDK, and Windows App Runtime 2.3.
- A Home Assistant test instance with the Windows Companion device registered.
- For multi-monitor scenarios, two physical monitors or a dock that exposes two
  active physical targets.

## Automated validation

Run the targeted Core tests:

```powershell
dotnet test --project tests\WindowsCompanion.Core.Tests\WindowsCompanion.Core.Tests.csproj -c Release -- --filter-query "/*/*/HardwareSensorTests/*"
```

Build the source application:

```powershell
.\scripts\run.ps1 -NoLaunch
```

The tests must prove normalization, manufacturer decoding, duplicate handling,
identical-monitor preservation, deterministic ordering, state/attribute shapes,
empty versus unavailable results, and privacy-scoped capture policy.

## Manual validation

Launch the app:

```powershell
.\scripts\run.ps1
```

1. Open sensor settings and confirm **Display Identity** is disabled by default.
2. Confirm the disabled preview does not reveal monitor identity.
3. Enable **Display Identity** and verify Home Assistant creates one diagnostic sensor.
4. With one monitor, verify the state shows its Windows model label and the
   attributes follow [the sensor contract](contracts/home-assistant-sensor.md).
5. Attach a second monitor and verify the state becomes `2 monitors`, both
   details appear once, and the primary flag is correct.
6. Disconnect and reconnect one monitor and verify the sensor changes promptly
   without duplicate entries.
7. When a monitor exposes no readable name or EDID identity, verify it remains
   counted and is shown as `Unknown display`.
8. Repeat a read without changing topology and verify no extra change-driven
   update is sent.
9. On a headless test system, verify `No displays`, `count: 0`, and an empty list.
10. In a remote session or display-API failure case, verify `Unavailable` rather
   than a false `No displays`.
11. Disable the sensor and confirm monitor identity is no longer captured while
    the existing benign display count continues to work if enabled.

## Architecture validation

- Windows interop remains in `WindowsCompanion.App`.
- Formatting and selection logic is covered in `WindowsCompanion.Core.Tests`.
- No model names, manufacturer IDs, product codes, device paths, or serials
  appear in logs or committed fixtures.
