# Feature Specification: Time Zone Offset Attribute

**Feature Branch**: `devsecninja-add-timezone-offset`

**Created**: 2026-08-18

**Status**: Draft

**Input**: User description: "The Time Zone sensor value is fine for the human eye but is difficult to use in time calculations. Add a standard attribute that is easy to use in calculations."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Calculate Local Times (Priority: P1)

As a Home Assistant user, I can read the Windows device's current UTC offset from the Time Zone sensor so that automations and templates can perform time arithmetic without translating a time-zone name.

**Why this priority**: This directly enables the requested calculations while preserving the existing human-readable sensor state.

**Independent Test**: Inspect the Time Zone sensor attributes for a device in a known time zone and use the numeric offset to convert between UTC and the device's local time.

**Acceptance Scenarios**:

1. **Given** the device is in a time zone two hours ahead of UTC, **When** the Time Zone sensor is read, **Then** its existing state remains unchanged and its calculation-friendly offset represents positive 7,200 seconds.
2. **Given** the device is in a time zone behind UTC, **When** the Time Zone sensor is read, **Then** the offset is negative and produces the correct local time when added to UTC.
3. **Given** the device is in a time zone with a fractional-hour offset, **When** the Time Zone sensor is read, **Then** the offset preserves the complete difference from UTC without rounding.

---

### User Story 2 - Track Seasonal Offset Changes (Priority: P2)

As a Home Assistant user, I receive the offset that applies now so that calculations remain correct when daylight-saving or other civil-time rules change.

**Why this priority**: A static base offset would silently produce incorrect calculations for many time zones during part of the year.

**Independent Test**: Evaluate the sensor on opposite sides of a known seasonal clock change and confirm that the reported current offset changes while the time-zone identifier remains stable.

**Acceptance Scenarios**:

1. **Given** the device's current time-zone rules enter or leave daylight-saving time, **When** the sensor is refreshed after the transition, **Then** the calculation-friendly offset reflects the new current UTC difference.

### Edge Cases

- UTC is represented as an offset of zero.
- Negative offsets retain their sign, and fractional-hour offsets retain their minute component.
- The reported offset reflects the instant at which the sensor reading is produced, including daylight-saving and historical rule changes applicable at that instant.
- Existing consumers that use only the Time Zone sensor state continue to receive the same value and attributes.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The Time Zone sensor MUST retain its existing human-readable state.
- **FR-002**: The Time Zone sensor MUST expose the device's current difference from UTC as a signed whole number of seconds.
- **FR-003**: A positive offset MUST indicate local time is ahead of UTC, a negative offset MUST indicate local time is behind UTC, and zero MUST indicate UTC.
- **FR-004**: The offset MUST include the full current difference from UTC without rounding to whole hours.
- **FR-005**: The offset MUST account for the civil-time rules in effect when the sensor reading is produced, including daylight-saving time.
- **FR-006**: The new attribute MUST be included wherever the existing Time Zone sensor reading is previewed or transmitted.
- **FR-007**: Existing Time Zone sensor identifiers, state, classification, and previously exposed attributes MUST remain compatible.

### Key Entities

- **Time Zone Sensor Reading**: The existing device time-zone identifier and metadata, augmented with the signed current UTC offset in seconds.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can convert between UTC and the device's current local time using one arithmetic operation and the new attribute.
- **SC-002**: Offset values are exact to the second for UTC, positive, negative, whole-hour, and fractional-hour time zones in all tested cases.
- **SC-003**: The reported offset changes correctly across 100% of tested daylight-saving transitions.
- **SC-004**: Existing Time Zone sensor state and attributes remain unchanged in 100% of compatibility tests except for the addition of the new attribute.

## Assumptions

- Signed seconds are used because they are unambiguous, preserve fractional offsets, and can be consumed directly by time-arithmetic functions.
- The requested calculation concerns the device's current offset, not an arbitrary future or historical instant.
- Adding a new sensor attribute is backward compatible for existing Home Assistant entities and automations.
