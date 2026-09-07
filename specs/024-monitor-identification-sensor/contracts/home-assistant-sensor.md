# Home Assistant Sensor Contract: Monitor Identity

## Registration

| Property | Value |
| --- | --- |
| `unique_id` | `monitor_identity` |
| `type` | `sensor` |
| `name` | `Monitors` |
| `entity_category` | `diagnostic` |
| `icon` | `mdi:monitor-off`, `mdi:monitor`, or `mdi:monitor-multiple` based on count |
| Default | Disabled |
| Privacy | Sensitive |

## State

| Capture result | State |
| --- | --- |
| Successful, zero monitors | `No monitors` |
| Successful, one monitor | Normalized model label, otherwise manufacturer and product code, otherwise `Unknown monitor` |
| Successful, multiple monitors | `{count} monitors` |
| Capture unavailable | `Unavailable` |

## Attributes

Successful captures include:

```json
{
  "count": 2,
  "monitors": [
    {
      "model": "DELL U2723QE",
      "manufacturer": "DEL",
      "product_code": "A1B2",
      "connection": "external",
      "primary": true
    },
    {
      "model": "LG ULTRAGEAR",
      "manufacturer": "GSM",
      "product_code": "5B09",
      "connection": "external",
      "primary": false
    }
  ]
}
```

Properties whose source values are unavailable are omitted. A successful
headless capture has `count: 0` and an empty `monitors` array. An unavailable
capture has no attributes. The `count` is the full active-monitor total, while
`monitors` contains at most the first eight entries in deterministic order.

## Privacy Contract

- The payload MUST NOT include monitor device paths, serial numbers, adapter
  identifiers, target identifiers, registry paths, or raw EDID bytes.
- Preview MUST return the standard gated placeholder until the user explicitly
  enables or requests this sensitive sensor.
- Logs MUST NOT contain model, manufacturer, product code, or internal
  deduplication keys.

## Change Contract

- Reordering by Windows alone does not change the payload.
- Attachment, detachment, identity change, primary-display change, or connection
  classification change does change the payload.
- Repeated identical readings do not request immediate synchronization.
