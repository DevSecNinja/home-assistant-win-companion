# Phase 1 Data Model: Location Sensor

## `LocationStatus` (enum, `WindowsCompanion.Core.Models`)

| Value | Meaning |
|-------|---------|
| `Ready` | A usable position was obtained. |
| `PermissionDenied` | Windows Location Services are off, or this app has not been granted location permission (`GeolocationAccessStatus` not `Allowed`, or `Geolocator.LocationStatus == Disabled`). |
| `Unavailable` | Access is allowed but no position could be produced right now (no data source, timeout, or an unexpected provider failure). |

State transitions are provider-driven, not modeled as a state machine: every
`GetLocationAsync` call independently resolves to one of the three statuses
based on the current OS state at call time. There is no persisted status
between calls other than what `LocationSensorSource` caches for `Read()`.

## `LocationResult` (record, `WindowsCompanion.Core.Models`)

| Field | Type | Notes |
|-------|------|-------|
| `Status` | `LocationStatus` | Required. |
| `Latitude` | `double?` | Set only when `Status == Ready`. Decimal degrees, WGS84 (as returned by `Geoposition.Coordinate.Point.Position.Latitude`). |
| `Longitude` | `double?` | Set only when `Status == Ready`. Decimal degrees, WGS84. |
| `AccuracyMeters` | `double?` | Set only when `Status == Ready`. Horizontal accuracy radius in meters (`Coordinate.Accuracy`). |
| `Timestamp` | `DateTimeOffset?` | When the reading was obtained (UTC "now" at the time of the successful call - `Geocoordinate.Timestamp` is also available but using capture time keeps this consistent with how `WinGetUpdateResult.CheckedAt` is produced). |

Validation rules:

- `Latitude`/`Longitude`/`AccuracyMeters` MUST all be null when `Status` is not
  `Ready`, and MUST all be non-null when `Status == Ready` (mirrors
  `WinGetUpdateResult`'s "packages only meaningful when Ready" shape).
- `Latitude` MUST be in `[-90, 90]`; `Longitude` MUST be in `[-180, 180]` when
  present. These are invariants of the underlying WinRT type, not re-validated
  defensively beyond a debug assertion - the provider is trusted the same way
  `WinGetUpdateResult.Parse` trusts PowerShell's structured output only after
  checking required fields are present.

Convenience factory members (mirroring `WinGetUpdateResult.Checking`/`Failure`):

- `LocationResult.Unavailable(LocationStatus status = Unavailable)` - a
  no-data result with all coordinate fields null.

## `ILocationProvider` (interface, `WindowsCompanion.Core.Abstractions`)

```csharp
public interface ILocationProvider
{
    Task<LocationResult> GetLocationAsync(CancellationToken cancellationToken = default);
}
```

One method, matching the shape of `IWinGetUpdateProvider.CheckForUpdatesAsync`.
No capability-probe method is needed (unlike WinGet) because permission state
is already expressed as part of `LocationResult.Status` on every call.

## `LocationSensorSource` (class, `WindowsCompanion.Core.Sensors`)

Implements `ISensorSource` and `IRefreshableSensorSource`, structured exactly
like `WinGetUpdateSensorSource`:

- One `SensorDefinition`: `UniqueId = "location"`, `Name = "Location"`,
  `Privacy = SensorPrivacy.Sensitive`, `EnabledByDefault = false`,
  `OptInPlaceholder = "Enable to read this device's location"`.
- Wraps an `ILocationProvider` and a `SensorPollLoop` (15-minute interval, see
  `research.md`).
- Caches the latest `LocationResult` behind a lock, exactly like
  `WinGetUpdateSensorSource._result`/`_gate`.
- `Read()` produces one `Sensor`:
  - `State`: `"{Latitude:F6},{Longitude:F6}"` when `Ready`; otherwise a short
    human string ("Unavailable" / "Location permission required") - never a
    stale coordinate once status has moved off `Ready`.
  - `Attributes`: `{"gps_accuracy": AccuracyMeters}` when `Ready`; omitted
    otherwise, following the existing "omit rather than send null/placeholder
    attributes" pattern used elsewhere (e.g. `Sensor.Attributes` is
    `JsonIgnoreCondition.WhenWritingNull`).
  - `Icon`: `"mdi:crosshairs-gps"` when `Ready`, `"mdi:crosshairs-question"`
    otherwise.
- `PreviewAsync()` returns the disabled placeholder when the sensor is off
  (via `SensorPreviewGate`, same as every other sensitive sensor), otherwise
  the same rendering as `Read()`.
- A change-gate (`ChangeGate<(double Lat, double Lng)>` rounded to ~4 decimal
  places, roughly 11 m) suppresses `onChanged` pushes for GPS jitter that
  wouldn't move the reported state, matching the "extra push only when the
  value meaningfully changes" convention used by `WinGetUpdateSensorSource`'s
  own `ChangeGate`.

## `WindowsLocationProvider` (class, `WindowsCompanion.App.Services`)

Implements `ILocationProvider` using `Windows.Devices.Geolocation.Geolocator`,
marshaled onto a captured `DispatcherQueue` (see `research.md`). Not unit
tested directly (same as `PowerShellWinGetUpdateProvider`); validated manually
per `quickstart.md`.
