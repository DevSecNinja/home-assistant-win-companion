# Phase 0 Research: Location Sensor

All unknowns from the plan's Technical Context were resolved through direct
codebase inspection (existing WinGet-updates and Wi-Fi-identifiers sensors) and
Microsoft Learn documentation for `Windows.Devices.Geolocation`. No
`NEEDS CLARIFICATION` markers remain.

## Windows location API surface

- **Decision**: Use `Windows.Devices.Geolocation.Geolocator` directly
  (`RequestAccessAsync` once, then `GetGeopositionAsync()` per refresh) rather
  than a lower-level NMEA/GPS driver or a third-party positioning library.
- **Rationale**: It is the documented, first-party Windows Runtime API for
  device location, already usable from a WinUI 3 desktop app, and is the same
  API family (`Windows.Devices.*`) already used by `AudioDeviceSensorSource`
  (`Windows.Devices.Enumeration`) - consistent with the project's "prefer
  first-party Microsoft packages" constraint.
- **Alternatives considered**: Rejected polling `NetworkInterface`/`ipconfig`-based
  IP geolocation (inaccurate, requires a third-party service, and would leak
  the coordinate query to an external host - conflicts with "no telemetry or
  user data leaves the machine except calls to the user's own Home Assistant
  instance"); rejected a raw GPS/serial driver integration (most Windows
  PCs/laptops have no dedicated GPS hardware, whereas the Windows location
  platform already aggregates whatever sources - Wi-Fi/IP/GPS - are available).

## Manifest capability

- **Decision**: Do not add a `location` `DeviceCapability` to
  `Package.appxmanifest`.
- **Rationale**: Per Microsoft's guidance, "for unpackaged apps, the Location
  capability is not required in a manifest. However, you must still call
  `RequestAccessAsync` to prompt the user for permission." This app ships
  unpackaged (`WindowsPackageType=None`), matching every other OS-integration
  sensor in this codebase (Wi-Fi, domain join, audio devices) that also needs
  no manifest capability. `Package.appxmanifest` only matters for the optional,
  currently-unused MSIX packaging path; adding an inert entry there would be
  speculative and untested.
- **Alternatives considered**: Adding the capability anyway "for completeness"
  was rejected - it cannot be exercised or verified without an MSIX-packaged
  build, and the constitution favors evidence-driven, verified changes over
  speculative ones.

## Permission and UI-thread constraints

- **Decision**: `WindowsLocationProvider` captures the app's
  `Microsoft.UI.Dispatching.DispatcherQueue` at construction time (during
  `ProductionAppComposition.CreateDependencies()`, which runs on the UI thread
  during app startup) and marshals both `Geolocator.RequestAccessAsync()` and
  `GetGeopositionAsync()` onto that dispatcher via `TryEnqueue` + a
  `TaskCompletionSource`, even though the periodic poll itself runs on a
  background `SensorPollLoop` tick.
- **Rationale**: Microsoft's guidance is explicit that `RequestAccessAsync`
  "must be called from the UI thread while your app is in the foreground";
  calling it from a background poll thread would throw or silently fail the
  very first time the user enables the sensor. `GetGeopositionAsync` has no
  such restriction, but keeping both calls on the same dispatcher avoids two
  separate marshaling strategies for one provider.
- **Alternatives considered**: Requesting access eagerly at app startup
  (rejected - violates "don't access location until the app requires it" and
  would prompt users who never enable the sensor); requiring the Settings-page
  toggle handler to call `RequestAccessAsync` directly (rejected - couples a UI
  event handler to a specific sensor source instead of keeping the
  `ISensorSource`/`ILocationProvider` boundary the same shape as every other
  source).

## Status/error mapping

- **Decision**: Map to the new `LocationStatus` enum as follows:
  - `GeolocationAccessStatus.Denied` or `Unspecified` → `PermissionDenied`.
  - `Geolocator.LocationStatus` of `Disabled` (Location Services off) →
    `PermissionDenied` (same remediation - open Windows location settings).
  - `NoData`, `NotAvailable`, `NotInitialized`, a `GetGeopositionAsync` timeout,
    or any exception from the call → `Unavailable`.
  - A successful `Geoposition` → `Ready`, with `Coordinate.Point.Position`
    (`Latitude`, `Longitude`) and `Coordinate.Accuracy` (meters).
- **Rationale**: `GetGeopositionAsync` "throws an exception if the app doesn't
  have location permissions or if it times out with no location data
  retrieved," so the provider must wrap the call in try/catch and translate
  both the thrown exception and the non-`Allowed` access status into the same
  two "no usable value" buckets the sensor already has to render distinctly
  (permission problem vs. transient unavailability), mirroring
  `WinGetUpdateResult`'s status enum shape.
- **Alternatives considered**: A single generic "unavailable" status was
  rejected because FR-006/User Story 3 specifically require the user to be
  able to tell "permission problem, go fix it in Settings" apart from "no fix
  yet, wait" - collapsing them would regress that requirement.

## Refresh cadence

- **Decision**: Poll on a 15-minute interval via `SensorPollLoop`, with an
  immediate first tick on `Start()` (the loop's existing behavior), the same
  mechanism `WinGetUpdateSensorSource` uses with its 6-hour interval.
- **Rationale**: FR-007/FR-008 and the Assumptions section call for a bounded,
  periodic refresh rather than continuous real-time tracking (which would mean
  subscribing to `PositionChanged`/`ReportInterval` and keeping a `Geolocator`
  instance alive indefinitely - a materially bigger resource and privacy
  footprint than every other opt-in sensor in this app). 15 minutes is frequent
  enough to reflect a PC's realistic movement between locations (home/office/
  travel) while remaining a small, disclosable "Resource Usage" cost, and reuses
  the same poll-loop/`IRefreshableSensorSource` contract as the WinGet sensor
  instead of introducing a new event-driven observation model.
- **Alternatives considered**: Subscribing to `Geolocator.PositionChanged`
  (rejected - keeps a location session open continuously while enabled, a much
  larger and harder-to-disclose privacy/battery cost than a bounded poll, and
  does not fit the existing `Start(onChanged)`/`Stop()` "hook while enabled"
  shape as directly); a much longer interval like WinGet's 6 hours (rejected -
  location is far more time-sensitive than update availability; 6 hours would
  make the sensor read as "stale" for most automation use cases in User
  Story 1).

## Reused Sensors-page UX for the unavailable/permission-denied state

- **Decision**: Reuse the existing `AppController.OpenLocationSettings()`
  method and the Sensors page's existing "Windows location access" card
  (already wired to `ms-settings:privacy-location` for the Wi-Fi SSID/BSSID
  sensors), updating only its description text to mention the Location sensor
  alongside Wi-Fi.
- **Rationale**: This exact action and card already exist and already open the
  correct Windows settings page; adding a second, sensor-specific button would
  duplicate working, already-shipped functionality for no user benefit.
- **Alternatives considered**: A per-sensor inline "Open settings" action was
  rejected as unnecessary duplication once the existing shared card was found.
