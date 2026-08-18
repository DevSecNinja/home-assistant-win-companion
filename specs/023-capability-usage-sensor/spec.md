# Feature Specification: Capability Usage Sensors (Camera/Microphone)

**Status**: Shipped

**Created**: 2026-08-18

**Input**: Retroactive documentation of shipped `CapabilityUsageSensorSource` and
`CapabilityActivity` (originally part of 003-meeting-sensors).

Add opt-in `microphone` and `camera` binary sensors that report whether any
application is currently using the respective hardware capability.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Detect microphone use (Priority: P1)

As a Home Assistant user, I want to know when any application is using a
microphone so automations can show an on-air indicator.

**Independent Test**: Start and stop a microphone-using application and verify
the binary state follows within one polling interval.

**Acceptance Scenarios**:

1. **Given** the sensor is enabled and an app uses the microphone, **When** polled,
   **Then** the state is on.
2. **Given** the sensor is enabled and no app uses the microphone, **When** polled,
   **Then** the state is off.
3. **Given** a new installation, **When** the catalog is shown, **Then** this
   sensor is disabled until opt-in.

---

### User Story 2 - Detect camera use (Priority: P1)

As a Home Assistant user, I want to know when any application is using a camera
so automations can show a video-call indicator.

**Independent Test**: Start and stop a camera-using application and verify the
binary state follows within one polling interval.

**Acceptance Scenarios**:

1. **Given** the sensor is enabled and an app uses the camera, **When** polled,
   **Then** the state is on.
2. **Given** the sensor is enabled and no app uses the camera, **When** polled,
   **Then** the state is off.

### Edge Cases

- Registry entries may be missing, malformed, or inaccessible; the sensor remains
  available and reports a safe inactive state.
- Multiple applications may use the same capability; the binary sensor remains on
  while any active entry exists.
- Both HKCU and HKLM consent stores are scanned for packaged and non-packaged apps.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The catalog MUST offer a `microphone` binary sensor.
- **FR-002**: The catalog MUST offer a `camera` binary sensor.
- **FR-003**: Both sensors MUST be disabled by default and labelled sensitive.
- **FR-004**: Detection MUST read `LastUsedTimeStop` from both HKCU and HKLM
  `CapabilityAccessManager\ConsentStore` recursively.
- **FR-005**: A stop value ≤ 0 MUST be interpreted as currently active.
- **FR-006**: Registry access failures MUST be skipped per entry without failing
  the entire sensor.
- **FR-007**: Polling MUST run only while at least one sensor is enabled.
- **FR-008**: Unchanged readings MUST NOT produce Home Assistant traffic.

### Key Entities

- **Capability activity**: Whether `LastUsedTimeStop ≤ 0` exists for any app.
- **Consent store**: The Windows registry tree tracking per-app capability access.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Microphone/camera detection latency is at most 1 second after
  Windows updates its capability history.
- **SC-002**: With both sensors disabled, zero registry access occurs.
- **SC-003**: Both packaged and non-packaged application usage is detected.

## Privacy

- No application names or PIDs are exposed.
- Device names are not logged.
