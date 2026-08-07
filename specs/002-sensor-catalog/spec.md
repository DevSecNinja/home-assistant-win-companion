# Feature Specification: Selectable Sensor Catalog

**Feature Branch**: `002-sensor-catalog`

**Created**: 2026-08-07

**Status**: Shipped

**Input**: User description: "More sensors: screen lock, WLAN/Wi-Fi, IP address, OS
version, idle, last connection/push time. Make the sensors selectable in the
companion app so users can choose what they report."

## Overview

Feature 001 shipped a single hard-wired pair of battery sensors. This feature turns
sensor reporting into a **catalog**: a set of independent sensors the user explicitly
opts into, each backed by a cheap, event-driven Windows source.

Sensor identifiers deliberately mirror the official macOS/iOS companion app so that
Home Assistant entities are named familiarly and existing community automations work
against a Windows PC. See `research.md`.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Choose which sensors are reported (Priority: P1)

As a privacy-conscious user, I want to see every sensor the companion can report,
with a plain description of what it exposes, and switch each one on or off, so that
only the data I am comfortable sharing ever leaves my PC.

**Why this priority**: This is the gate for everything else. Without it, adding more
sensors makes the app *less* acceptable, because it would ship location-inferring and
content-revealing data by default.

**Independent Test**: Open Sensors, toggle a sensor off, and confirm it stops being
reported and its entity is disabled in Home Assistant; toggle it back on and confirm
it returns. Testable with only the already-shipped battery sensors.

**Acceptance Scenarios**:

1. **Given** the Sensors page, **When** the user views it, **Then** every available
   sensor is listed with its name, a description, and an on/off toggle.
2. **Given** an enabled sensor, **When** the user switches it off, **Then** the app
   stops collecting it, stops sending it, and Home Assistant marks the entity
   disabled.
3. **Given** a disabled sensor, **When** the user switches it on, **Then** it is
   registered (if new) and begins reporting within one sync interval.
4. **Given** a privacy-sensitive sensor, **When** the user first sees it, **Then** it
   is switched off by default and labelled with what it reveals.
5. **Given** any toggle change, **When** the app is restarted, **Then** the choice is
   remembered.

---

### User Story 2 - Know whether the PC is in use (Priority: P2)

As a home automation user, I want Home Assistant to know whether I am actually at my
PC - and whether the screen is locked - so I can drive presence-based automations
(lights, "at desk" status, do-not-disturb).

**Why this priority**: This is the highest-value new signal and the main reason to
run a companion at all. It is independently useful without any other new sensor.

**Independent Test**: Lock the PC and confirm the `Active` sensor turns off and
`Screen Locked` turns on in Home Assistant within seconds; unlock and confirm both
revert. Leave the PC untouched past the idle threshold and confirm `Active` turns off.

**Acceptance Scenarios**:

1. **Given** an unlocked PC in use, **When** the user locks it, **Then** `Active`
   becomes off and `Screen Locked` becomes on.
2. **Given** a locked PC, **When** the user unlocks it, **Then** the states revert.
3. **Given** an idle threshold of N minutes, **When** there is no input for longer
   than N, **Then** `Active` becomes off; **When** input resumes, it becomes on.
4. **Given** the `Active` sensor, **When** inspected in Home Assistant, **Then** its
   attributes expose the individual sub-states (Idle, Locked, Screensaver, Sleeping,
   Fast User Switched).
5. **Given** the user changes the idle threshold, **When** it is saved, **Then** the
   new threshold takes effect without restarting the app.

---

### User Story 3 - See the PC's network and system context (Priority: P3)

As a user, I want optional sensors for my network connection, IP address, OS
version and when the companion last reported in, so I can build network-aware
automations and confirm the companion is healthy.

**Why this priority**: Useful but supporting; several are privacy-sensitive and are
therefore off by default. Independently testable once the catalog exists.

**Independent Test**: Enable Connection Type and IP Address and confirm they update
when the machine changes network; disable them and confirm their entities become
disabled.

**Acceptance Scenarios**:

1. **Given** Connection Type is enabled, **When** the PC is on Wi-Fi, Ethernet, or
   offline, **Then** the state reports that classification.
2. **Given** IP Address is enabled and no local address is available, **When** it
   reports, **Then** the state is "Not Connected" rather than an error.
3. **Given** the network changes, **When** the change occurs, **Then** enabled sensors
   update without waiting for a full sync interval.
4. **Given** the optional Last Update Time sensor, **When** the app pushes to Home
   Assistant, **Then** it reflects that most recent successful push.
5. **Given** the OS version sensor, **When** it reports, **Then** it shows the
   Windows version and build.

### Edge Cases

- A machine with no Wi-Fi adapter must not error; the sensor reports "Not Connected".
- A desktop with no battery already reports gracefully; unchanged.
- Disabling every sensor must leave the connection healthy (notifications still work).
- A sensor enabled while offline must register once connectivity returns.
- Sensors registered by an older version that are now disabled must not be resurrected.
- Home Assistant restarting must not lose enablement state (it is owned by the app).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The app MUST expose a catalog of available sensors, each with a stable
  unique id, display name, description, and privacy classification.
- **FR-002**: The app MUST let the user enable or disable each sensor individually
  from a native Settings surface.
- **FR-003**: Sensor choices MUST persist across restarts in non-secret local config.
- **FR-004**: A disabled sensor MUST NOT be collected, MUST NOT be transmitted, and
  its backing OS hook MUST NOT be registered (off means zero cost).
- **FR-005**: Disabling a sensor MUST mark the corresponding Home Assistant entity
  disabled rather than leaving a stale entity. Enabling MUST re-enable it. (Both
  must go through `register_sensor`; `update_sensor_states` ignores the flag.)
- **FR-006**: Privacy-sensitive sensors (currently IP address) MUST default to off.
  Other optional network sensors MAY also default to off to minimize disclosure;
  presence and diagnostic sensors MAY default to on.
- **FR-007**: The app MUST report an `Active` binary sensor derived from idle, lock,
  screensaver, sleep and fast-user-switch state, exposing each
  sub-state as an attribute.
- **FR-008**: The app MUST report a `Screen Locked` binary sensor.
- **FR-009**: The idle threshold MUST be user-configurable and MUST take effect
  without restarting the app.
- **FR-010**: State changes MUST be pushed promptly (not only on the periodic sync)
  while producing no additional traffic when nothing changes.
- **FR-011**: The app MUST report optional `Connection Type`, `IP Address`,
  `OS Version` and `Last Boot` sensors. Wi-Fi SSID/BSSID are out of scope
  (Windows Location capability; see research.md).
- **FR-012**: The app MUST display the time of its most recent successful push in
  its own status view. It MAY offer an opt-in `last_update_time` timestamp sensor
  (off by default, because it writes recorder history every sync). It MUST NOT ship a
  sensor whose state is effectively constant as a liveness signal, because Home
  Assistant surfaces change time rather than report time and such a sensor reads as
  permanently stale.
- **FR-012a**: The app MUST show a health verdict (in the window and the tray
  tooltip) based on whether it is reporting on schedule, not merely connected.
- **FR-012b**: The app MUST write a rolling local log the user can open from the UI,
  containing no secrets.
- **FR-012c**: The app MUST offer an explicit "update now" action.
- **FR-012d**: Pausing reporting (Disconnect) MUST be reversible and MUST NOT
  discard credentials. Removing the server MUST be a separate, confirmed action that
  revokes the token and deletes stored configuration.
- **FR-012e**: The status view MUST show which Home Assistant server is connected.
- **FR-013**: String sensor states MUST be truncated to Home Assistant's 255
  character limit.
- **FR-014**: Sensor identifiers MUST match the official companion app where an
  equivalent sensor exists.
- **FR-015**: Privacy-sensitive values MUST NOT be written to logs.

### Key Entities

- **Sensor definition**: static metadata for a reportable sensor - unique id, name,
  type, device class, icon, description, privacy level, default enablement.
- **Sensor preference**: the user's enable/disable choice plus any per-sensor
  setting (e.g. idle threshold).
- **Sensor reading**: a produced value with optional attributes at a point in time.
- **Active state**: the composite of the boolean sub-states that derive `Active`.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can find and change any sensor's on/off state from the Sensors
  page opened from the main window.
- **SC-002**: Locking or unlocking the PC is reflected in Home Assistant within 10
  seconds.
- **SC-003**: With all optional sensors disabled, network traffic is unchanged from
  feature 001 (one batch per sync interval).
- **SC-004**: With all sensors enabled and the machine untouched, no additional
  webhook calls occur beyond the periodic sync (changes drive traffic, not polling).
- **SC-005**: No privacy-sensitive sensor is enabled without explicit user action.
- **SC-006**: No sensor value or identifier appears in logs for privacy-sensitive
  sensors (verified by inspection).

## Assumptions

- Sensors report through the existing `mobile_app` webhook; no re-registration of the
  device is required to add sensors.
- Windows session, power and network events are available to an unpackaged desktop
  app without elevation.
- Microphone/camera-in-use and meeting-context sensors are delivered separately in
  `specs/003-meeting-sensors/`. Frontmost app, storage, displays and Teams presence
  remain out of scope. Teams presence in particular would require Microsoft Graph
  and an Entra app registration.

## Delivery Notes

This feature was implemented directly from the specification and research while
Home Assistant and Windows behavior were still being discovered. No separate
`plan.md` or `tasks.md` was generated. The shipped code, tests, this corrected
specification, and `research.md` are the feature record.
