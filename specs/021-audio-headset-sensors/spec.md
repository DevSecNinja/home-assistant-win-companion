# Feature Specification: Audio/Headset Sensors

**Status**: Shipped

**Created**: 2026-08-18

**Input**: Retroactive documentation of shipped `AudioDeviceSensorSource` and
`HeadsetClassifier` (originally part of 003-meeting-sensors).

Add opt-in `audio_output` and `headset_connected` sensors that report the current
default audio output device and whether a headset/headphones/earbuds endpoint is
present.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Know the current audio output (Priority: P1)

As a Home Assistant user, I want to see the friendly name of my default audio
output so automations can react to speaker/headset switches.

**Independent Test**: Change the default audio output and verify the sensor
reports the new device name within one poll interval.

**Acceptance Scenarios**:

1. **Given** the sensor is enabled, **When** the default output changes, **Then**
   its state reports the new device's friendly name.
2. **Given** no audio device exists, **When** the sensor reports, **Then** the
   state is `Not Connected`.
3. **Given** a new installation, **When** the sensor catalog is shown, **Then**
   this sensor is disabled until opt-in.

---

### User Story 2 - Detect headset presence (Priority: P2)

As a Home Assistant user, I want a binary on/off signal when a headset is
connected so automations can infer readiness for a call.

**Independent Test**: Connect and disconnect a headset-class endpoint and verify
the binary state follows.

**Acceptance Scenarios**:

1. **Given** the sensor is enabled, **When** a headset endpoint appears, **Then**
   the state is on.
2. **Given** the sensor is enabled, **When** no headset endpoint exists, **Then**
   the state is off.
3. **Given** multiple endpoints including a headset, **When** polled, **Then** the
   state is on.

### Edge Cases

- COM exceptions during WinRT device enumeration produce safe empty readings.
- Virtual or Bluetooth audio endpoints with unexpected names do not crash.
- Disabling both sensors stops all device enumeration and polling.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The catalog MUST offer an `audio_output` sensor reporting the
  friendly name of the default audio render endpoint.
- **FR-002**: The catalog MUST offer a `headset_connected` binary sensor
  indicating whether a headset-class audio endpoint is present.
- **FR-003**: Both sensors MUST be disabled by default and labelled sensitive.
- **FR-004**: Headset classification MUST use keyword matching (headset,
  headphone, earbud, AirPod, Jabra, Poly, Plantronics) across render and capture.
- **FR-005**: Polling MUST run only while at least one sensor is enabled.
- **FR-006**: Unchanged readings MUST NOT produce Home Assistant traffic.

### Key Entities

- **Audio endpoint**: A WinRT `DeviceInformation` with a friendly name.
- **Headset classification**: Deterministic keyword match in Core.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: State updates appear within 10 seconds of a device change.
- **SC-002**: With both sensors disabled, zero device enumeration occurs.
- **SC-003**: Connecting common headset brands is detected without vendor SDK.

## Privacy

- Device names are not logged.
- Local preview enumerates devices only while requested.
