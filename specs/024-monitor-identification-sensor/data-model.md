# Data Model: Monitor Identification Sensor

## MonitorIdentity

Represents identity information for one active physical display target.

| Field | Type | Required | Rules |
| --- | --- | --- | --- |
| InternalKey | string | Yes | Used for deduplication and ordering, never serialized or logged |
| Model | string | No | Normalized Windows friendly device name; trim whitespace/control characters and bound length |
| Manufacturer | string | No | Three uppercase EISA/PNP letters, only when EDID IDs are valid |
| ProductCode | string | No | Four uppercase hexadecimal digits, only when EDID IDs are valid |
| Connection | DisplayConnection | Yes | `Internal`, `External`, or `Unknown` |
| IsPrimary | boolean | Yes | True for the active primary desktop display |

### Validation

- All public identity fields may be absent for an attached target Windows cannot
  identify; that target remains present and displays as `Unknown display`.
- Virtual, indirect, remote-only, disconnected, and forced targets without
  identity are not represented.
- Duplicate InternalKey values collapse to one identity.
- Two different InternalKey values remain separate even when all public fields
  match.

## MonitorCaptureResult

Represents the outcome of one Windows identity capture.

| Field | Type | Required | Rules |
| --- | --- | --- | --- |
| Status | enum | Yes | `Available` or `Unavailable` |
| Monitors | ordered collection of MonitorIdentity | Yes | Empty is valid only when Status is `Available` |

### State transitions

- `Available (empty)` -> `Available (one/many)`: monitor attached.
- `Available (one/many)` -> `Available (empty)`: all physical monitors detached.
- `Available` -> `Unavailable`: Windows query failed.
- `Unavailable` -> `Available`: a later read succeeds.

## MonitorSensorReading

The Home Assistant representation derived from `MonitorCaptureResult`.

| Field | Shape |
| --- | --- |
| State | `No displays`, one monitor's display label or `Unknown display`, `{N} displays`, or `Unavailable` |
| count | Number of public display entries; omitted when unavailable |
| displays | Ordered array of display detail objects; empty for a successful headless result |

Each monitor detail object can contain:

| Attribute | Shape |
| --- | --- |
| model | Friendly model/name from Windows |
| manufacturer | Three-letter EISA/PNP manufacturer ID |
| product_code | Four-digit uppercase hexadecimal EDID product code |
| connection | `built_in`, `external`, or `unknown` |
| primary | boolean |

Unavailable optional values are omitted rather than replaced by invented text.

## Ordering

1. Primary display first.
2. Built-in before external before unknown.
3. Normalized model, manufacturer, and product code using ordinal comparison.
4. Internal key as the final tie-breaker.

The internal-key tie-breaker preserves stable output for identical monitors while
remaining absent from the public payload.

## Bounds

- The state uses only a single display label or count and remains under 255
  characters.
- The structured monitor list is bounded to the existing display-detail maximum
  of eight entries; `count` still reports the full number if Windows exposes more.
- The deterministic order decides which eight entries are retained.
