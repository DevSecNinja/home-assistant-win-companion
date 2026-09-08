# Research: Display and Monitor Search Aliases

## Decision: Use a display-based public sensor contract

**Decision**: Rename the unique ID to `display_identity`, use `Display Identity`
as the name, and use display terminology in public states and attributes.

**Rationale**: This makes the sensor consistent with `displays_count` and
`display_resolution`. The user explicitly accepts a breaking change, and the
existing persisted registration mechanism disables removed sensor entities so
the old `monitor_identity` entity does not remain active indefinitely.

**Alternatives considered**:

- Preserve `monitor_identity`: rejected because it leaves the public contract
  inconsistent for the lifetime of the sensor.
- Add a second sensor with a display-based ID: rejected because it duplicates
  the same reading.

## Decision: Use explicit aliases rather than description-only matching

**Decision**: Add optional search aliases to sensor definitions and include them
in the local search index.

**Rationale**: The current search happens to index descriptions, but relying on
the word "monitor" remaining in prose is fragile and makes discoverability an
accidental side effect. Explicit aliases document and preserve intended search
behavior.

**Alternatives considered**:

- Depend on the description containing both terms: rejected as implicit and
  vulnerable to future copy edits.
- Add a global monitor/display text replacement rule: rejected because global
  synonym expansion can create irrelevant matches and is harder to extend.

## Decision: Prefer "Display Identity" as the visible name

**Decision**: Use "Display Identity" in both static definition metadata and
emitted sensor metadata.

**Rationale**: "Display" matches the existing "Displays" and "Display
Resolution" labels, while "Identity" distinguishes brand/model information from
count and resolution.

**Alternatives considered**:

- "Displays": rejected because it collides with the existing count sensor.
- "Display Details": rejected because resolution is also a display detail.
- Keep "Monitors": rejected because it preserves the inconsistency.
