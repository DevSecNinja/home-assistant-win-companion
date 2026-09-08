# Feature Specification: Display and Monitor Search Aliases

**Feature Branch**: `devsecninja-sensor-naming-consistency`

**Created**: 2026-09-08

**Status**: Implemented

**Input**: User description: "Use consistent Display terminology for sensors while allowing users to find display-related sensors by searching for either monitor or display."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Find display sensors with familiar terminology (Priority: P1)

As a user browsing the sensor catalog, I want display-related sensors to appear
when I search for either "display" or "monitor" so terminology differences do
not prevent me from finding the sensor I need.

**Why this priority**: Search discoverability is the immediate usability problem.
Users may naturally use either common term and should receive the same relevant
results.

**Independent Test**: Search the sensor list for "display" and then "monitor";
verify that the monitor identification sensor is visible for both searches.

**Acceptance Scenarios**:

1. **Given** the sensor catalog contains the monitor identification sensor,
   **When** the user searches for "display", **Then** that sensor is shown.
2. **Given** the sensor catalog contains the monitor identification sensor,
   **When** the user searches for "monitor", **Then** that sensor is shown.
3. **Given** the user searches using different capitalization or a partial term,
   **When** the term matches a sensor name or search alias, **Then** the sensor is
   shown.

---

### User Story 2 - See consistent sensor terminology (Priority: P2)

As a user reviewing display-related sensors, I want their visible names to use
consistent terminology so the catalog feels coherent and predictable.

**Why this priority**: Consistent labels reduce confusion, but discoverability
through both terms delivers the primary value even before every visible label is
aligned.

**Independent Test**: Open the sensor list and verify that the monitor identity
sensor uses the same "Display" terminology as the other display-related sensors.

**Acceptance Scenarios**:

1. **Given** display-related sensors are listed together, **When** the user reads
   their visible names, **Then** the monitor identity sensor uses "Display"
   terminology.
2. **Given** an existing Home Assistant installation already registered the
   old `monitor_identity` sensor, **When** the application is upgraded and
   synchronizes, **Then** the old entity is disabled and the replacement uses
   the `display_identity` identifier.

### Edge Cases

- Search text contains only whitespace.
- Search text combines an alias with other unmatched characters.
- A future sensor uses "monitor" in its visible name but has no explicit aliases.
- A sensor has multiple aliases that differ only by capitalization.
- An existing installation has the old sensor enabled when it upgrades.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The sensor catalog MUST present the monitor identification sensor
  using visible "Display" terminology consistent with related sensors.
- **FR-002**: The sensor MUST use `display_identity` as its unique identifier.
- **FR-003**: Sensor search MUST match the visible sensor name and any configured
  search aliases.
- **FR-004**: The monitor identification sensor MUST be discoverable through
  both "display" and "monitor" search terms.
- **FR-005**: Search matching MUST remain case-insensitive and support partial
  terms.
- **FR-006**: Sensors without search aliases MUST continue to be searchable by
  visible name with no behavior change.
- **FR-007**: Empty or whitespace-only search text MUST continue to show every
  sensor.
- **FR-008**: Search aliases MUST affect only local catalog discoverability and
  MUST NOT be sent as sensor state or attributes.
- **FR-009**: An old persisted `monitor_identity` registration MUST follow the
  existing removed-sensor retirement behavior and be disabled on synchronization.
- **FR-010**: Public empty, multiple-item, fallback, and structured attribute
  labels MUST use "display" terminology.

### Key Entities

- **Sensor display name**: The user-visible name shown in the catalog and
  registration metadata.
- **Sensor identifier**: The registration and preference identity for the
  current sensor contract.
- **Search alias**: An additional user-facing term that can match a sensor during
  local catalog filtering without changing its identifier or reported data.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Searching for either "display" or "monitor" reveals the monitor
  identification sensor in 100% of catalog searches.
- **SC-002**: Current sensor definitions and readings use `display_identity`;
  `monitor_identity` is emitted only when disabling a persisted predecessor.
- **SC-003**: Users can locate the sensor with either common term in under three
  seconds.
- **SC-004**: Every sensor without configured aliases produces the same search
  results as before the change.

## Assumptions

- "Display" is the preferred visible catalog terminology because existing
  related sensors already use it.
- "Monitor" remains a useful synonym and should be retained as a search alias.
- Breaking the recently introduced `monitor_identity` contract is acceptable
  because the application currently has one known user.
- Internal Windows API and physical-device models may continue to use "monitor"
  where that is the precise platform term.

## Out of Scope

- Rewriting user-customized Home Assistant entity names.
- Adding general stemming, fuzzy matching, or synonym expansion for unrelated
  sensor categories.
