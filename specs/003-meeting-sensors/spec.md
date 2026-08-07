# Feature Specification: Meeting Context Sensors

**Feature Branch**: `feature/003-meeting-sensors`

**Created**: 2026-08-07

**Status**: Shipped

**Input**: User description: "Add presenting/do-not-disturb, microphone/camera in
use, headset connected, and default audio output sensors from issue #6."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Detect presenting or interruption state (Priority: P1)

As a Home Assistant user, I want my PC to report whether Windows considers me busy,
presenting, full-screen, or in quiet time so automations can avoid interrupting me.

**Why this priority**: It covers the main "busy light" use case without depending on
a specific meeting application or exposing user content.

**Independent Test**: Enable presentation mode, quiet time, or a full-screen
application and verify that the sensor changes to the corresponding state, then
returns to normal afterwards.

**Acceptance Scenarios**:

1. **Given** normal desktop use, **When** the sensor reports, **Then** its state
   indicates that notifications are accepted.
2. **Given** Windows enters presentation, quiet-time, busy, or full-screen mode,
   **When** the state changes, **Then** the sensor reports the corresponding
   user-readable state within one polling interval.
3. **Given** a new installation, **When** the sensor catalog is first shown,
   **Then** this sensor is enabled by default.

---

### User Story 2 - Detect microphone and camera use (Priority: P2)

As a Home Assistant user, I want optional microphone-in-use and camera-in-use
signals so automations can infer that I may be on a call without integrating with
Teams, Zoom, or another vendor.

**Why this priority**: These signals apply across meeting applications and provide
more direct context than application-specific presence.

**Independent Test**: Enable each sensor, start and stop an application using the
device, and verify that the corresponding binary state follows within one polling
interval.

**Acceptance Scenarios**:

1. **Given** the microphone sensor is enabled, **When** any desktop or packaged
   application begins or ends microphone use, **Then** its state updates.
2. **Given** the camera sensor is enabled, **When** any desktop or packaged
   application begins or ends camera use, **Then** its state updates.
3. **Given** a new installation, **When** the sensor catalog is first shown,
   **Then** both sensors are disabled until the user opts in.

---

### User Story 3 - Detect meeting audio equipment (Priority: P3)

As a Home Assistant user, I want to know the current audio output and whether a
headset is connected so automations can combine that context with microphone use.

**Why this priority**: It improves meeting detection without vendor-specific
hardware support, but it is supporting context rather than a direct busy signal.

**Independent Test**: Connect and disconnect a headset and change the default audio
output, verifying that the enabled sensors update and show friendly device names.

**Acceptance Scenarios**:

1. **Given** the audio-output sensor is enabled, **When** the default output device
   changes, **Then** the sensor reports its friendly name.
2. **Given** the headset sensor is enabled, **When** a headset-class audio endpoint
   appears or disappears, **Then** the binary state updates.
3. **Given** no usable output device, **When** the sensor reports, **Then** it
   returns an unavailable or not-connected state rather than failing.

### Edge Cases

- Access-history entries may be missing, malformed, or inaccessible; microphone and
  camera sensors must remain available and report a safe inactive state.
- Multiple applications or device instances may use the same capability; the
  binary sensor remains on while any active entry exists.
- Audio endpoints may be disabled, disconnected, virtual, Bluetooth, or expose
  unexpected names; enumeration must not prevent other sensors from reporting.
- If only one sensor from a shared source is enabled, disabling another sensor must
  not stop the source required by the remaining sensor.
- Disabling every sensor from a polling source must stop its polling activity.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The catalog MUST offer a presenting/do-not-disturb sensor that reports
  the current Windows notification state using stable, user-readable values.
- **FR-002**: The presenting/do-not-disturb sensor MUST be enabled by default.
- **FR-003**: The catalog MUST offer separate microphone-in-use and camera-in-use
  binary sensors.
- **FR-004**: Microphone and camera use MUST account for both packaged and
  non-packaged desktop applications and both machine-wide and per-user records.
- **FR-005**: Microphone and camera sensors MUST be disabled by default.
- **FR-006**: The catalog MUST offer an `audio_output` sensor reporting the friendly
  name of the default audio output, matching the official companion identifier.
- **FR-007**: The catalog MUST offer a `headset_connected` binary sensor indicating
  whether a headset-class audio endpoint is present.
- **FR-008**: Audio device sensors MUST work across vendors and MUST NOT require a
  hardware-vendor SDK.
- **FR-009**: Every new sensor MUST show its current local value in the Sensors page
  before or after enablement; previewing MUST NOT transmit the value.
- **FR-010**: Polling MUST run only while at least one sensor backed by that polling
  source is enabled and MUST stop after its last sensor is disabled.
- **FR-011**: A changed reading MUST trigger an update promptly, while an unchanged
  reading MUST produce no additional Home Assistant traffic.
- **FR-012**: Sensor enablement choices MUST persist across restarts and disabled
  entities MUST follow the existing register-as-disabled behavior.
- **FR-013**: Sensor values and device names MUST obey the existing privacy and
  state-length protections.
- **FR-014**: Teams presence, Microsoft Graph integration, and HID telephony state
  are out of scope.

### Key Entities

- **Meeting context reading**: A presenting, privacy-device, or audio-equipment
  state with a stable sensor identifier and current value.
- **Capability activity**: Whether at least one application is currently using the
  microphone or camera.
- **Audio endpoint**: An available render or capture device with a friendly name
  and enough classification to identify headset-class equipment.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Presenting, microphone, camera, headset, and default-output changes
  appear in Home Assistant within 15 seconds while their sensors are enabled.
- **SC-002**: With all new sensors disabled, the app performs no polling work for
  them and produces no additional network traffic.
- **SC-003**: An unchanged machine state produces no more Home Assistant updates
  than the existing periodic sync.
- **SC-004**: A user can preview and enable any new sensor from the Sensors page in
  under 30 seconds.
- **SC-005**: Microphone and camera activity is detected for both a packaged
  application and a traditional desktop application during manual verification.
- **SC-006**: Connecting common headset brands requires no vendor-specific setup or
  additional software.

## Assumptions

- A 10-second polling interval is sufficient for meeting-context automations.
- Audio output names are potentially identifying and therefore default to off;
  `headset_connected` also defaults to off as part of this opt-in context cluster.
- A friendly-name heuristic is sufficient for initial headset classification;
  mute and off-hook telephony controls remain explicitly out of scope.
- Safe inactive or unavailable readings are preferable to surfacing access errors
  that would interrupt the rest of sensor synchronization.
