# Feature Specification: Monitor Identification Sensor

**Feature Branch**: `devsecninja-monitor-identification-sensor`

**Created**: 2026-09-07

**Status**: Shipped

**Input**: Add a sensor that reports the brand and model/type of attached
monitors, including multiple-monitor and no-monitor states.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Identify an attached monitor (Priority: P1)

As a Home Assistant user, I want Windows Companion to identify my currently
attached monitor so dashboards and automations can use its brand and model.

**Why this priority**: Identifying one attached monitor is the core value of the
feature and the most common configuration.

**Independent Test**: Attach one physical monitor with readable identity data,
enable the sensor, and verify its state and attributes identify that monitor.

**Acceptance Scenarios**:

1. **Given** one active physical monitor with known brand and model information,
   **When** the sensor reports, **Then** the state identifies that monitor using
   its brand and model.
2. **Given** one active physical monitor with incomplete identity information,
   **When** the sensor reports, **Then** it uses a stable human-readable fallback
   without failing or inventing missing details.

---

### User Story 2 - Identify multiple attached monitors (Priority: P2)

As a Home Assistant user with a multi-monitor workspace, I want one sensor to
describe every attached monitor so I can observe the complete display setup
without enabling a variable number of sensors.

**Why this priority**: Multi-monitor support prevents incomplete or ambiguous
results while preserving one stable sensor entity per Windows device.

**Independent Test**: Attach two or more physical monitors with distinct
identities and verify the sensor reports the total count and a deterministic
entry for each monitor.

**Acceptance Scenarios**:

1. **Given** two to eight active physical monitors, **When** the sensor reports,
   **Then** its state reports the monitor count and its attributes list every
   monitor's available brand and model information.
2. **Given** the same set of monitors is reported repeatedly, **When** Windows
   returns them in a different enumeration order, **Then** the sensor output
   remains ordered consistently and does not create a false change.
3. **Given** one monitor is disconnected from a multi-monitor setup, **When** the
   sensor next refreshes, **Then** the count and monitor list no longer include
   that monitor.

---

### User Story 3 - Represent a headless computer (Priority: P3)

As a Home Assistant user running a desktop without an attached monitor, I want
the sensor to report that state explicitly so automations can distinguish a
headless computer from a sensor failure.

**Why this priority**: A valid empty state is necessary for desktops, remotely
managed computers, and temporarily disconnected displays.

**Independent Test**: Run the enabled sensor with no active physical monitor and
verify it reports zero monitors with an empty monitor list.

**Acceptance Scenarios**:

1. **Given** no active physical monitor is attached, **When** the sensor reports,
   **Then** its state is `No monitors` and its monitor count is zero.
2. **Given** monitor discovery completes successfully with no results, **When**
   the sensor reports, **Then** the result is treated as a valid state rather
   than an unavailable or error state.

### Edge Cases

- A monitor exposes a manufacturer but no model, or a model but no manufacturer.
- A connected physical monitor exposes no readable identity fields and is shown
  as `Unknown monitor` rather than being omitted.
- Windows exposes duplicate records for the same physical monitor.
- Two attached monitors have the same brand and model.
- A monitor is connected or disconnected while discovery is in progress.
- More than eight active physical monitors are present.
- A virtual, indirect, disconnected, or remote-session display appears alongside
  physical displays.
- Monitor identity text contains padding, control characters, or an unknown
  manufacturer code.
- Discovery fails even though the sensor previously reported monitors.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The sensor catalog MUST offer one monitor identification sensor.
- **FR-002**: The sensor MUST be disabled by default and MUST perform no monitor
  discovery while disabled.
- **FR-003**: The sensor MUST report active physical monitors attached to the
  Windows desktop, including built-in physical panels when present.
- **FR-004**: The sensor MUST exclude displays that are disconnected, virtual,
  indirect, or present only through a remote session when they can be identified
  as such.
- **FR-005**: For exactly one monitor, the sensor state MUST use the best
  available human-readable brand and model combination, falling back to
  `Unknown monitor` when Windows confirms the physical target but exposes no
  identity fields.
- **FR-006**: For multiple monitors, the sensor state MUST report the number of
  attached monitors and attributes MUST contain one structured entry per monitor
  up to a maximum of eight entries.
- **FR-007**: For no monitors, the sensor state MUST be `No monitors`, the
  monitor count MUST be zero, and the structured monitor list MUST be empty.
- **FR-008**: Each monitor entry MUST expose the available manufacturer/brand,
  model/type, product code, connection classification, and primary-display flag
  without substituting invented values for unavailable fields.
- **FR-009**: The monitor list MUST use deterministic ordering and MUST preserve
  separate entries for physically distinct monitors with the same brand and
  model.
- **FR-009a**: When more than eight active physical monitors are present, the
  count MUST report the full total and the monitor list MUST contain the first
  eight entries from the deterministic ordering.
- **FR-010**: Duplicate records for the same physical monitor MUST be collapsed
  into one entry.
- **FR-011**: A discovery failure MUST be distinguishable from a successful
  no-monitor result and MUST follow the application's existing sensor error
  behavior.
- **FR-012**: The sensor MUST refresh often enough to report attachment or
  detachment within 15 seconds while enabled.
- **FR-013**: Unchanged monitor information MUST NOT request an immediate sensor
  synchronization.
- **FR-014**: Monitor identity values MUST NOT be written to application logs.
- **FR-015**: The local preview MUST not discover or reveal monitor identity
  details until the user explicitly requests the preview or enables the sensor.

### Key Entities

- **Monitor identity**: One active physical display, described by an internal
  deduplication key and any available manufacturer, model, and product code. The
  internal key is not included in the sensor output.
- **Monitor collection**: The deduplicated and deterministically ordered set of
  active physical monitor identities for the current Windows desktop.
- **Monitor sensor reading**: A concise state plus monitor count and the
  structured monitor collection.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With one supported monitor attached, the reported brand and model
  match the identity presented by Windows in at least 95% of tested monitor
  configurations that provide both values.
- **SC-002**: Configurations with 0, 1, 2, and 4 attached monitors produce the
  correct count and exactly one structured entry per physical monitor.
- **SC-003**: Attaching or detaching a monitor is reflected in Home Assistant
  within 15 seconds while the sensor is enabled.
- **SC-004**: Repeated readings of an unchanged monitor setup produce identical
  state and attribute ordering in 100% of test runs.
- **SC-005**: With the sensor disabled, monitor discovery, polling, and
  transmission occur zero times.
- **SC-006**: A headless computer reports `No monitors` without being presented
  to the user as a sensor failure.

## Assumptions

- "Type" means the monitor's manufacturer-provided model designation rather than
  its panel technology, physical size, or connector type.
- An attached monitor is an active physical display participating in the current
  Windows desktop; built-in laptop panels are included.
- Monitor identity is device information and is treated as sensitive enough to
  require opt-in collection and preview gating.
- One stable Home Assistant sensor with a structured monitor list is preferable
  to creating and retiring a dynamic sensor entity for each monitor.
- The existing sensor lifecycle, preview, persistence, synchronization, and error
  conventions remain authoritative.

## Out of Scope

- Monitor serial numbers, device paths, and other unique hardware identifiers in
  Home Assistant state, attributes, previews, or logs.
- Panel technology, physical dimensions, color capabilities, and supported modes.
- Creating a separate Home Assistant entity for each attached monitor.
- Identifying inactive displays that are connected but not participating in the
  current Windows desktop.
