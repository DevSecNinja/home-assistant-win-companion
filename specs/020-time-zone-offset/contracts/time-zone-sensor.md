# Time Zone Sensor Contract

The existing `time_zone` mobile-app sensor adds one attribute. All existing
registration and state fields remain unchanged.

## Reading

```json
{
  "unique_id": "time_zone",
  "type": "sensor",
  "name": "Time Zone",
  "state": "Europe/Berlin",
  "entity_category": "diagnostic",
  "icon": "mdi:map-clock",
  "attributes": {
    "utc_offset_seconds": 7200
  }
}
```

## Compatibility

- `unique_id`, type, name, state, category, and icon are unchanged.
- `utc_offset_seconds` is a signed integer.
- The value is the current local-minus-UTC difference at snapshot time.
- Consumers that ignore unknown attributes continue to work unchanged.
