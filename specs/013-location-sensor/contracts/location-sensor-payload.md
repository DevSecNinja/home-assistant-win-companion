# Contract: `location` Home Assistant Sensor Payload

This is the only external interface this feature adds: the JSON payload sent
to Home Assistant's `mobile_app` webhook (`register_sensor` on first
registration, `update_sensor_states` on every sync thereafter), following the
existing `WindowsCompanion.Core.Models.Sensor` shape used by every other
sensor in this app.

## Registration / update payload

```json
{
  "unique_id": "location",
  "type": "sensor",
  "name": "Location",
  "state": "47.398000,8.545100",
  "attributes": {
    "latitude": 47.398000,
    "longitude": 8.545100,
    "gps_accuracy": 12.5
  },
  "icon": "mdi:crosshairs-gps"
}
```

- `state`: `"{latitude:F6},{longitude:F6}"` (decimal degrees, WGS84, 6 decimal
  places - roughly 0.1 m of precision, which is a formatting choice, not
  evidence of that much real-world accuracy). Home Assistant sensor state must
  be a string; this keeps both coordinates in one entity, matching the
  Assumption in `spec.md` that this ships as one combined sensor rather than
  separate latitude/longitude entities.
- `attributes.latitude` / `attributes.longitude`: the same coordinate as
  numeric `double` values (not packed into a string), so a Home Assistant
  template sensor, automation trigger, or `zone` distance calculation can
  consume them directly without parsing `state`. Present only when `state`
  reflects a real fix.
- `attributes.gps_accuracy`: horizontal accuracy in meters, a `double`. Present
  only when `state` reflects a real fix.
- No `device_class`/`unit_of_measurement`/`state_class` - a lat/long pair is
  not a Home Assistant numeric sensor class; it is presented the same way a
  Wi-Fi BSSID or domain name sensor is (a plain string state with supporting
  attributes).

## Unavailable / permission-denied payload

```json
{
  "unique_id": "location",
  "type": "sensor",
  "name": "Location",
  "state": "Location permission required",
  "icon": "mdi:crosshairs-question"
}
```

- No `attributes` key when there is no fix (mirrors `Sensor.Attributes`'s
  `JsonIgnoreCondition.WhenWritingNull`; Home Assistant simply sees no
  attributes rather than nulled-out coordinate fields).
- `state` text distinguishes "permission/Location Services problem, go fix it
  in Settings" (`"Location permission required"`) from "temporarily no fix"
  (`"Unavailable"`), per FR-005/User Story 3.

## Disabled sensor payload

Unchanged from every other opt-in sensor: when the user disables a
previously-enabled Location sensor, `SensorCatalog`/`SensorSyncService` send
`"disabled": true` for `unique_id: "location"` so Home Assistant marks the
entity unavailable, exactly as for Wi-Fi SSID/BSSID today. No feature-specific
behavior is introduced here.

## Compatibility

This is an additive, purely new `unique_id`. It does not change the schema,
webhook actions, or behavior of any existing sensor, and requires no Home
Assistant-side configuration beyond the existing `mobile_app` integration
already required for every other sensor this companion reports.
