# Research: Time Zone Offset Attribute

## Offset representation

- **Decision**: Expose `utc_offset_seconds` as a signed JSON integer.
- **Rationale**: Seconds are directly usable in time arithmetic, preserve
  fractional-hour zones, have an unambiguous sign, and serialize consistently.
  The name states both the reference point and unit.
- **Alternatives considered**: An ISO 8601 string such as `+02:00` is familiar but
  requires parsing before arithmetic. Decimal hours are susceptible to fractional
  conversion mistakes. Minutes are workable but less directly compatible with
  common duration functions.

## Offset instant

- **Decision**: Calculate the offset for the instant at which the sensor snapshot
  is captured.
- **Rationale**: `TimeZoneInfo.GetUtcOffset(DateTimeOffset)` applies the active
  civil-time rule, including daylight-saving time, and avoids treating the base
  offset as the current offset.
- **Alternatives considered**: `BaseUtcOffset` is stable but wrong during
  daylight-saving periods. Calculating for an arbitrary date is outside the
  requested current-state sensor.

## Change detection

- **Decision**: Include the offset in the locale source's captured state and
  schedule a cancellable wake-up for the next offset transition while the Time
  Zone sensor is enabled.
- **Rationale**: The time-zone name can remain unchanged when daylight-saving
  changes its current offset. Windows setting events do not guarantee delivery at
  an automatic daylight-saving boundary, so the scheduled wake-up feeds the
  existing change gate without periodic polling.
- **Alternatives considered**: Relying on system events or calculating only when
  `Read` is called would return the correct value eventually but could leave Home
  Assistant stale until the next periodic full sync. Frequent polling would wake
  the app unnecessarily.

## Test boundary

- **Decision**: Put the deterministic offset-to-seconds conversion in Core and
  pass explicit zones and instants in tests.
- **Rationale**: Tests do not depend on the machine's configured zone or wall
  clock, while the App remains a thin Windows state adapter.
- **Alternatives considered**: Testing `TimeZoneInfo.Local` in App tests would be
  environment-dependent and would not reliably cover positive, negative,
  fractional, and daylight-saving cases.
