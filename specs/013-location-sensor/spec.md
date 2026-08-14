# Feature Specification: Location Sensor

**Feature Branch**: `013-location-sensor`

**Created**: 2026-08-14

**Status**: Draft

**Input**: User description: "Can you add a sensor that passes through the location of the device?"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Track the PC's current location in Home Assistant (Priority: P1)

As a Home Assistant user, I want to opt in to a location sensor for my Windows PC
so automations (arrival/departure, presence-based scenes, "where is my laptop")
can use the device's current coordinates the same way they use other companion
sensors.

**Why this priority**: This is the entire point of the feature; without it there
is nothing to ship.

**Independent Test**: Enable the Location sensor in the Sensors page on a PC with
Windows Location Services turned on and permission granted. Confirm Home
Assistant receives a sensor update containing latitude, longitude, and accuracy
that matches the PC's real position, and that the value refreshes periodically
without further user action.

**Acceptance Scenarios**:

1. **Given** Location Services are on and permission is granted, **When** the
   user enables the Location sensor, **Then** Home Assistant receives a reading
   with latitude, longitude, and an accuracy attribute within moments.
2. **Given** the sensor is enabled, **When** the device's position changes
   meaningfully, **Then** the next refresh reports the updated coordinates.

---

### User Story 2 - Location stays private until explicitly enabled (Priority: P2)

As a privacy-conscious user, I want the location sensor to behave like the
existing Wi-Fi identifier sensors: off by default, clearly labeled as revealing
precise location, and never queried or logged while disabled.

**Why this priority**: Precise GPS/Wi-Fi-positioning data is the most sensitive
value the companion can report; shipping it enabled-by-default or leaking it
through logs would violate the project's privacy principle.

**Independent Test**: With the sensor disabled, confirm via logs/instrumentation
that no location query occurs, and that the Settings/Sensors preview shows an
"Enable to read this value" placeholder rather than a real coordinate.

**Acceptance Scenarios**:

1. **Given** a fresh install, **When** the user opens the Sensors page, **Then**
   the Location sensor is listed as disabled by default with wording that
   explains it reveals precise location.
2. **Given** the sensor is disabled, **When** the sensor sync runs, **Then** no
   location API call is made and no coordinate is written to logs.

---

### User Story 3 - Clear guidance when location access is unavailable (Priority: P3)

As a user who enables the sensor without having granted Windows location
permission (or with Location Services turned off system-wide), I want the
companion to tell me why no value is available and give me a direct way to fix
it, instead of showing a silent blank or a confusing error.

**Why this priority**: Matches the existing Wi-Fi identifiers UX and avoids
support confusion, but the feature is still useful without it if the user
already has permission granted.

**Independent Test**: Turn off Windows Location Services (or deny the app
permission), enable the sensor, and confirm the reported state clearly says
permission/Location Services is required, with an action that opens Windows
Settings to the relevant page.

**Acceptance Scenarios**:

1. **Given** Windows Location Services are off, **When** the sensor is enabled,
   **Then** the sensor reports an "unavailable/permission required" state
   instead of a stale or blank value.
2. **Given** that state, **When** the user follows the provided action, **Then**
   Windows opens directly to the Location privacy settings page.

---

### Edge Cases

- What happens when the PC has no location hardware/provider at all (typical
  desktop with no GPS or Wi-Fi-based positioning source)? The sensor must report
  an "unavailable" state rather than fail sensor sync for the whole batch.
- How does the system handle a location request that never resolves (positioning
  timeout)? It must not hang the periodic sensor sync; it should time out and
  report the previous or an "unavailable" state.
- What happens if location access is revoked while the sensor is enabled and
  running? The next refresh must reflect the new "permission required" state
  rather than keep repeating a stale coordinate.
- What happens when the user disables the sensor mid-refresh? Any in-flight
  location query must be cancelled and must not be reported afterward.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The companion MUST offer a "Location" sensor reporting the
  device's current latitude and longitude, sourced from the Windows location
  platform used for the signed-in user session.
- **FR-002**: The Location sensor MUST default to disabled and MUST be labeled
  in the Sensors page as revealing the device's precise location, consistent
  with how Wi-Fi SSID/BSSID are labeled today.
- **FR-003**: The companion MUST NOT query the location platform, observe
  location changes, or log any coordinate while the sensor is disabled.
- **FR-004**: The Location sensor state MUST include, at minimum, latitude,
  longitude, and a horizontal accuracy value (in meters) as a sensor attribute.
- **FR-005**: When Windows denies location access, when Location Services are
  turned off, or when no position can be resolved, the sensor MUST report a
  distinct "unavailable" state rather than a stale or fabricated coordinate.
- **FR-006**: The companion MUST provide a direct action from the Sensors page
  to open Windows' Location privacy settings when the sensor is in the
  "unavailable/permission required" state.
- **FR-007**: While enabled, the Location sensor MUST refresh on a bounded
  interval so it eventually reflects the device's current position without
  requiring the user to manually resync, and disabling the sensor MUST cancel
  any refresh in progress.
- **FR-008**: The reported coordinate MUST use the device's most recent
  location fix at the time of each periodic sensor sync; it is not required to
  stream continuous real-time updates between syncs.

### Key Entities

- **Location Reading**: The result of one location query - status (ready,
  unavailable, permission required), latitude, longitude, horizontal accuracy,
  and the time it was obtained. Mirrors how other opt-in sensors (e.g. WinGet
  updates) represent a point-in-time provider result.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user with location permission already granted can enable the
  sensor and see a real coordinate reported in Home Assistant within one sensor
  sync cycle, with no manual configuration beyond the toggle.
- **SC-002**: 100% of sensor syncs performed while the Location sensor is
  disabled make zero location-platform queries, verified by instrumentation/log
  review.
- **SC-003**: When location access is unavailable, the user can reach Windows'
  Location privacy settings in a single action from the Sensors page, and
  understands from the reported state alone that permission - not a bug - is
  the blocker.
- **SC-004**: Disabling the sensor while a location query is in progress leaves
  no residual coordinate reported afterward and cancels the in-flight query.

## Assumptions

- The device's location comes from Windows' own location platform (whatever mix
  of GPS, Wi-Fi, and IP-based positioning Windows itself resolves); the
  companion does not implement its own positioning logic.
- One combined "Location" sensor exposes latitude/longitude as its state with
  accuracy as an attribute, rather than separate sensors per coordinate -
  consistent with how Home Assistant device trackers/location sensors are
  typically consumed by automations.
- A periodic refresh cadence similar to other moderate-cost opt-in sensors is
  acceptable; sub-minute real-time tracking is out of scope for this feature.
- Reported coordinates are the device's actual resolved position (no artificial
  rounding/fuzzing) once the user has explicitly opted in, matching how the
  existing Wi-Fi SSID/BSSID sensors already report precise values once enabled.
- Windows Location Services being off, and the process lacking location
  permission, are the only "unavailable" causes in scope; enterprise-managed
  location policies are treated as another form of denied permission.
