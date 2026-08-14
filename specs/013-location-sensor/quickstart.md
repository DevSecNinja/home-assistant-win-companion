# Quickstart: Validating the Location Sensor

## Prerequisites

- Windows 10 19041+ or Windows 11 with Location Services available.
- This repo checked out on Windows, .NET 10 SDK, matching Windows SDK, and
  Windows App Runtime 2.3 installed (see repo root instructions).
- A reachable Home Assistant instance with the `mobile_app` integration (or
  use the app's built-in demo/test-profile mode - see
  `specs/010-mocked-ha-e2e-testing`).

## Build and run

```powershell
.\scripts\run.ps1
```

## Unit tests (fast path, no Windows location hardware needed)

```powershell
dotnet test tests\WindowsCompanion.Core.Tests\WindowsCompanion.Core.Tests.csproj -c Release --filter "FullyQualifiedName~LocationSensorSourceTests"
```

Expected: all tests pass using a fake `ILocationProvider`, covering:

- Disabled sensor performs zero provider queries (mirrors
  `WinGetUpdateTests.Disabled_preview_performs_no_provider_query`).
- Enabling and refreshing reports a `"{lat:F6},{lng:F6}"` state with
  `gps_accuracy` attribute.
- A `PermissionDenied` provider result reports "Location permission required"
  and no attributes.
- An `Unavailable` provider result reports "Unavailable" and no attributes.
- Stopping the source while a query is in flight cancels it (mirrors
  `WinGetUpdateTests.Stopping_source_cancels_an_active_check`).

## Manual end-to-end validation

1. Launch the app (`.\scripts\run.ps1`), sign in to a Home Assistant instance.
2. Open **Sensors**. Confirm "Location" is listed, off by default, labeled as
   revealing precise location (`OptInPlaceholder` text visible before
   enabling).
3. With Windows Settings → Privacy & security → Location already **on** and
   this app allowed (or granted via the first-run prompt), toggle Location on.
   - Expect: within one sync, Home Assistant shows a `sensor.<device>_location`
     entity with a `"lat,long"` state and a `gps_accuracy` attribute matching
     the PC's real position.
4. Turn Windows' system Location Services **off** (Settings → Privacy &
   security → Location → toggle off), then use **Sync sensors now**.
   - Expect: the sensor state becomes "Location permission required" (or
     equivalent unavailable wording), not a stale coordinate.
   - Confirm the Sensors page's "Windows location access" card's "Windows
     settings" button opens Windows straight to the Location privacy page
     (`ms-settings:privacy-location`).
5. Turn Location Services back on, sync again, and confirm the sensor recovers
   a real coordinate.
6. Disable the Location sensor, then check the log file (via the app's "About
   & updates → open log file" action) for the sync period covering the
   disable action.
   - Expect: no coordinate, latitude, or longitude value appears anywhere in
     the log (only benign sensors are loggable, per `SensorDefinition.Loggable`).
7. Re-enable the sensor and immediately disable it again while a sync is in
   flight (toggle quickly). Confirm no error is thrown and Home Assistant does
   not receive a coordinate reading after the disable.

## Definition of done

- All items in `specs/013-location-sensor/spec.md` Acceptance Scenarios pass
  per the manual steps above.
- `dotnet test tests\WindowsCompanion.Core.Tests\WindowsCompanion.Core.Tests.csproj -c Release` passes in full (regression check).
- `dotnet build` succeeds for both `win-x64` and `win-arm64` per the repo's
  standard build commands.
