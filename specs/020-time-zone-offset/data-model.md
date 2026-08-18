# Data Model: Time Zone Offset Attribute

## Time Zone Sensor Reading

An existing Home Assistant diagnostic sensor reading augmented with its current
UTC offset.

| Field | Type | Rules |
|-------|------|-------|
| `unique_id` | String | Remains `time_zone`. |
| `state` | String | Existing IANA-preferred time-zone name remains unchanged. |
| `attributes.utc_offset_seconds` | Signed integer | Local time minus UTC, in whole seconds, for the snapshot instant. |

## Validation rules

- Positive values mean local time is ahead of UTC.
- Negative values mean local time is behind UTC.
- UTC is `0`.
- Fractional-hour offsets retain all minute and second precision supplied by the
  platform.
- The value is calculated from the active time-zone rules for the captured
  instant, not from the zone's base offset.

## Lifecycle

1. Capture one current instant and the current local time zone.
2. Derive the IANA-preferred state and current signed offset from that snapshot.
3. Seed or compare the complete locale/time-zone/offset state in the existing
   change gate.
4. Include `utc_offset_seconds` whenever the Time Zone sensor is read.
5. Request an immediate sensor sync when the offset changes, even if the zone name
   does not.
