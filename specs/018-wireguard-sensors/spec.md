# Feature Specification: WireGuard Sensors

**Feature Branch**: `devsecninja-wireguard-sensors`

**Created**: 2026-08-17

**Status**: Draft

**Input**: User description: "Add low-resource WireGuard client sensors that work with the default Windows client without requiring administrator permissions or additional tools."

## Clarifications

### Session 2026-08-17

- Q: Which WireGuard sensors should the first release include? → A: Status only.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See WireGuard Connection State (Priority: P1)

As a Home Assistant user, I can see whether at least one WireGuard tunnel on my Windows device is connected so that automations and dashboards can react to VPN availability.

**Why this priority**: Connection state is the primary user value and remains useful without traffic measurements.

**Independent Test**: Enable only the WireGuard status sensor, activate and deactivate a tunnel, and confirm Home Assistant receives the corresponding state without an elevation prompt.

**Acceptance Scenarios**:

1. **Given** the official WireGuard client is installed and a tunnel is running with an operational adapter, **When** the sensor is read, **Then** its state is `connected`.
2. **Given** the official WireGuard client is installed but no tunnel is operational, **When** the sensor is read, **Then** its state is `disconnected`.
3. **Given** WireGuard cannot be detected or safely inspected, **When** the sensor is read, **Then** its state is `unavailable` rather than reporting a false connection.

### Edge Cases

- The WireGuard application is installed but its manager service is stopped.
- A tunnel service reports running while its corresponding network adapter is absent, disabled, or not operational.
- Multiple WireGuard tunnels connect or disconnect between observations.
- Access to service or adapter information is denied despite the process remaining unelevated.
- The user disables the WireGuard status sensor while monitoring is active.
- A non-WireGuard adapter has a similar display name and must not be classified as WireGuard.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide an aggregate WireGuard status sensor with `connected`, `disconnected`, and `unavailable` states.
- **FR-002**: The status MUST be `connected` only when at least one WireGuard tunnel is running and a corresponding WireGuard network adapter is operational.
- **FR-003**: The feature MUST operate within the companion application's existing non-administrator process and MUST NOT request elevation.
- **FR-004**: The feature MUST work with a standard installation of the official WireGuard for Windows client and MUST NOT require users to install additional software or alter WireGuard configuration or permissions.
- **FR-005**: The feature MUST NOT read WireGuard configuration contents or expose tunnel names, peer keys, endpoints, assigned addresses, traffic counters, or other network identifiers.
- **FR-006**: The feature MUST NOT claim to verify peer reachability or a recent cryptographic handshake; its user-facing description MUST distinguish local tunnel state from end-to-end connectivity.
- **FR-007**: The system MUST aggregate multiple WireGuard tunnels into one privacy-preserving status sensor.
- **FR-008**: The system MUST stop all WireGuard discovery and event monitoring when the WireGuard status sensor is disabled.
- **FR-009**: The feature MUST avoid continuous polling and MUST update at the application's normal sensor interval, with immediate refreshes limited to meaningful connection changes.
- **FR-010**: The feature MUST surface inspection failures as unavailable data using the application's existing diagnostic behavior without logging sensitive network information.
- **FR-011**: Existing enabled-sensor preferences and Home Assistant registration behavior MUST remain compatible across application upgrades.

### Key Entities

- **WireGuard Observation**: A point-in-time aggregate containing client availability, whether any tunnel is running, whether any corresponding adapter is operational, and observation time.
- **WireGuard Status Sensor**: The stable aggregate sensor definition together with its privacy and resource-usage descriptions.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user with the official WireGuard client can enable and use the WireGuard status sensor without an administrator prompt, permission change, or additional installation.
- **SC-002**: Connection-state changes appear in Home Assistant by the next normal sensor update, and meaningful operating-system connection changes trigger at most one immediate synchronization.
- **SC-003**: With the WireGuard status sensor disabled, the feature performs zero WireGuard observations and retains no operating-system event subscriptions.
- **SC-004**: Across connected, disconnected, missing-client, inspection-failure, and multiple-tunnel tests, 100% of reported states follow the specified acceptance scenarios.
- **SC-005**: No sensor state, attribute, preview, diagnostic message, or persisted value contains tunnel names, peer keys, endpoints, or assigned addresses.
- **SC-006**: Routine monitoring adds less than 0.1% average CPU usage and no sustained background activity between samples on a representative supported Windows device.

## Assumptions

- The first release supports the official WireGuard for Windows client; third-party VPN products built on WireGuard are outside scope unless they expose the same observable Windows behavior.
- Tunnel status represents local service and adapter readiness, not peer handshake freshness or internet reachability.
- The existing sensor polling interval, enablement controls, registration lifecycle, and unavailable-state conventions are reused.
- Individual tunnel selection and tunnel-identifying attributes are intentionally outside scope to preserve privacy and avoid privileged configuration access.
- Traffic totals and transfer-rate sensors are outside scope for the first release.
