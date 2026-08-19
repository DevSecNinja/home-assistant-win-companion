# Quickstart: Validate the Time Zone Offset Attribute

## Prerequisites

- Windows with the .NET 10 SDK and repository prerequisites installed.
- A Home Assistant test instance paired with the companion app for manual
  end-to-end validation.

## Automated validation

Run the focused Core tests:

```powershell
dotnet test --project tests\WindowsCompanion.Core.Tests\WindowsCompanion.Core.Tests.csproj -c Release -- --filter-query "/*/*/HardwareSensorTests/*"
```

Expected outcomes:

- UTC produces `0`.
- Positive and negative zones produce correctly signed seconds.
- A fractional-hour zone is not rounded.
- A daylight-saving zone produces the offset active at each supplied instant.
- The next daylight-saving start and end transitions are found exactly, while a
  fixed-offset zone schedules no transition.

## Source validation

Build without launching:

```powershell
.\scripts\run.ps1 -NoLaunch
```

## End-to-end validation

1. Start the app and enable the Time Zone sensor.
2. In Home Assistant, inspect the `time_zone` entity attributes.
3. Confirm `utc_offset_seconds` matches the signed current difference from UTC.
4. Confirm the entity state remains the existing IANA-preferred name.
5. Use the attribute as a seconds duration in a template and verify that adding it
   to UTC produces the device's local time.

See [contracts/time-zone-sensor.md](contracts/time-zone-sensor.md) for the payload
shape and [data-model.md](data-model.md) for sign and precision rules.
