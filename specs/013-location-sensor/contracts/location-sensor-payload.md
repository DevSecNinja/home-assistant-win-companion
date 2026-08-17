# Contract: `location` Home Assistant Device Tracker

This feature uses the Home Assistant `mobile_app` webhook `update_location`
command to update the built-in device tracker entity for this companion device.
This is the same mechanism used by the iOS and Android companion apps, enabling
zone-based states (e.g. "Home", "Work") on the map rather than raw coordinates.

The location sensor still uses `LocationSensorSource` and `SensorCatalog` for
lifecycle management (start/stop, privacy gating, enable/disable), but its data
is sent via `update_location` rather than `register_sensor`/`update_sensor_states`.

## Ready payload (`update_location` webhook)

```json
{
  "type": "update_location",
  "data": {
    "gps": [47.398000, 8.545100],
    "gps_accuracy": 12
  }
}
```

- `data.gps`: `[latitude, longitude]` array of numeric doubles (WGS84).
- `data.gps_accuracy`: horizontal accuracy in meters, integer.

## Unavailable / permission-denied payload

```json
{
  "type": "update_location",
  "data": {
    "location_name": "not_home"
  }
}
```

- When no fix is available (permission denied, Location Services off, or
  positioning timeout), the companion sends `location_name` without GPS data so
  Home Assistant clears the stale position (FR-005). This prevents the device
  tracker from showing an outdated coordinate indefinitely.

## Sensor registration

The location sensor is **not** registered via `register_sensor` or updated via
`update_sensor_states`. It is excluded from the normal sensor batch in
`SensorSyncService` and sent exclusively through `update_location`.

A previously registered legacy `location` sensor entity (from older versions) is
retired on the next sync: `SensorSyncService` detects it is no longer produced by
any source and sends `disabled: true` through `register_sensor`, marking the stale
entity unavailable in Home Assistant.

## Disabled behavior

When the user disables the Location sensor toggle, `SensorCatalog` stops the
`LocationSensorSource`. No location query occurs, no `update_location` is sent,
and the device tracker retains whatever state Home Assistant last recorded (which
is standard device tracker behavior for offline devices).

## Compatibility

This is the standard `mobile_app` device tracker mechanism and requires no
Home Assistant-side configuration beyond the existing `mobile_app` integration.
