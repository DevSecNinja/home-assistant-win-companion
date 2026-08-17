# Feature Specification: Home Assistant Examples

**Feature Branch**: `devsecninja-offline-automation-examples`

**Created**: 2026-08-17

**Status**: Draft

**Input**: User description: "Provide ready-to-use Home Assistant examples, beginning with an offline device status template, and organize them so future importable automation examples have a clear location."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Add Offline Device Status (Priority: P1)

A Windows Companion user can find a ready-to-use example that derives whether
their PC is connected from the time Home Assistant last received a regular sensor
report.

**Why this priority**: Home Assistant otherwise retains the final reported sensor
values after a PC shuts down, which can misleadingly make the device appear online.

**Independent Test**: Follow the example using an existing companion device,
stop reports from the PC, and confirm the derived connectivity status changes
after the documented timeout without the client sending timestamps.

**Acceptance Scenarios**:

1. **Given** a companion device with a sensor that reports regularly, **When** the example is configured using its device name, **Then** Home Assistant shows the PC as connected while reports remain fresh.
2. **Given** the configured companion sensor stops reporting, **When** the timeout elapses, **Then** Home Assistant shows the PC as disconnected.
3. **Given** reporting resumes, **When** Home Assistant receives a new report, **Then** the connectivity status returns to connected.

---

### User Story 2 - Discover Compatible Examples (Priority: P2)

A user can distinguish reusable entity templates from automations and quickly
identify how each example is installed and customized.

**Why this priority**: Clear categories prevent users from applying the wrong
installation method and leave room for future automation imports.

**Independent Test**: Starting from the project documentation, locate both the
entity-template and automation categories and determine the installation method
for each without consulting external guidance.

**Acceptance Scenarios**:

1. **Given** the examples index, **When** a user looks for device connectivity, **Then** they can locate the offline status example and its prerequisites.
2. **Given** a future automation example, **When** it is added, **Then** it has a dedicated category separate from entity templates.

---

### User Story 3 - Contribute Future Automations (Priority: P3)

A contributor can add future Home Assistant automation examples without
reorganizing the existing example library.

**Why this priority**: A stable organization keeps links durable and makes future
importable content predictable.

**Independent Test**: Place a sample automation in the documented automation
category and confirm its purpose and installation path fit the guidance.

**Acceptance Scenarios**:

1. **Given** a new automation idea, **When** a contributor follows the examples guidance, **Then** the automation has an unambiguous destination and documentation expectations.

### Edge Cases

- The chosen device name does not resolve or the device has no reported sensors.
- Home Assistant restarts while the companion PC is offline.
- Device sensors report the same states repeatedly without changing value.
- A user selects a timeout shorter than the companion's normal reporting interval.
- Older Home Assistant versions do not expose the report timestamp required by the example.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The project MUST provide a user-facing Home Assistant examples index.
- **FR-002**: Examples MUST be grouped by Home Assistant artifact type, with entity templates separate from automations.
- **FR-003**: The initial entity-template example MUST derive connected status from the newest server-recorded report among the companion device's sensor entities.
- **FR-004**: The offline status example MUST NOT require the client to transmit a timestamp or an additional high-frequency heartbeat.
- **FR-005**: The offline status example MUST let users choose both the companion device by name and the offline timeout without requiring an entity ID.
- **FR-006**: Every example MUST state its prerequisites, installation procedure, customization points, expected behavior, and removal procedure.
- **FR-007**: The examples index MUST reserve and document a destination for future importable automation examples.
- **FR-008**: Examples MUST use placeholders rather than personal server addresses, device identifiers, or sensor values.
- **FR-009**: The project documentation MUST link users to the examples index.
- **FR-010**: The offline status example MUST explain that loss detection is delayed by the chosen timeout and that graceful shutdown delivery is not guaranteed.

### Key Entities

- **Example**: A reusable Home Assistant configuration idea with a purpose,
  prerequisites, installation method, customization points, and removal steps.
- **Example category**: The Home Assistant artifact type that determines where an
  example is found and how users install it, initially entity templates or
  automations.
- **Companion device**: The Home Assistant device whose sensor report times
  represent recent communication from the PC.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can locate the offline status example and identify all required customization in under two minutes.
- **SC-002**: Following the example produces a connectivity entity that changes to disconnected within one Home Assistant evaluation interval after the configured timeout.
- **SC-003**: The connectivity entity returns to connected within one Home Assistant evaluation interval after sensor reporting resumes.
- **SC-004**: All published examples include all five required documentation elements: prerequisites, installation, customization, expected behavior, and removal.
- **SC-005**: A contributor can classify a future automation example without changing or relocating any existing example.

## Assumptions

- Users have a Home Assistant version that exposes the latest report time for an entity.
- At least one enabled sensor on the companion device reports on the application's regular synchronization cycle.
- The initial example is manually configured because Home Assistant does not provide a fully pre-populated import flow for template helpers.
- Future automation examples may use Home Assistant-supported import mechanisms when those mechanisms fit the artifact.
- Client-side provisioning of Home Assistant helpers is outside this feature's scope.
