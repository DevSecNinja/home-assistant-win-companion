# Feature Specification: HA Version Display

**Feature Branch**: `016-ha-version-display`

**Created**: 2026-08-17

**Status**: Draft

**Input**: User description: "Show the HA and if available the HA OS version under Connected"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - View Home Assistant version when connected (Priority: P1)

As a user with a connected Home Assistant instance, I want to see the Home Assistant
Core version displayed beneath the "Connected" status label so I can quickly confirm
which version I'm running without opening the HA web UI.

**Why this priority**: This is the primary ask—surface the HA version in the
connected state for at-a-glance awareness.

**Independent Test**: Can be verified by connecting the companion to a Home Assistant
instance and confirming the Core version appears in the settings card.

**Acceptance Scenarios**:

1. **Given** the companion is connected to a Home Assistant instance, **When** the
   user views the settings page, **Then** the Home Assistant Core version (e.g.
   "HA 2025.1.0") is displayed below the "Connected" status text.
2. **Given** the companion loses its connection, **When** the status changes to
   disconnected, **Then** the version information is no longer displayed.

---

### User Story 2 - View Home Assistant OS version when available (Priority: P1)

As a user running Home Assistant OS, I want to additionally see the HA OS version so
I can confirm both Core and OS are up to date.

**Why this priority**: Equally important as the Core version; HA OS users expect to
see both versions together.

**Independent Test**: Can be verified by connecting to an HA OS installation and
confirming both versions appear; connecting to a non-OS installation should show only
the Core version.

**Acceptance Scenarios**:

1. **Given** the companion is connected to a Home Assistant OS installation, **When**
   the user views the settings page, **Then** both the HA Core version and HA OS
   version are displayed (e.g. "HA 2025.1.0 · OS 14.2").
2. **Given** the companion is connected to a non-OS installation (Container, Core,
   Supervised without OS info), **When** the user views the settings page, **Then**
   only the HA Core version is displayed without errors.

---

### Edge Cases

- What happens when the HA instance returns no version info (e.g. older API)?
  Display "Connected" without version details, gracefully degrading.
- What happens when the version string is unexpectedly long? Truncate with ellipsis
  to prevent layout overflow.
- What happens during reconnection/failover? Version info updates to reflect the
  newly connected instance's version.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST retrieve the Home Assistant Core version from the connected
  instance after a successful connection is established.
- **FR-002**: System MUST retrieve the Home Assistant OS version when available (HA OS
  installations expose this; other installation types may not).
- **FR-003**: System MUST display the HA Core version in the connection status area
  of the settings page when connected.
- **FR-004**: System MUST display the HA OS version alongside the Core version when
  the OS version is available.
- **FR-005**: System MUST gracefully omit version information when it cannot be
  retrieved (no errors shown to user).
- **FR-006**: System MUST update displayed version information on reconnection or
  route failover.
- **FR-007**: System MUST clear version information when disconnected.

### Key Entities

- **HA Instance Info**: Represents the discovered metadata about the connected Home
  Assistant instance, including Core version and optional OS version.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can identify the connected HA Core version at a glance without
  navigating away from the companion settings page.
- **SC-002**: HA OS version is visible for 100% of connected HA OS installations that
  expose the version via their API.
- **SC-003**: No visual errors or layout breakage occur when version information is
  unavailable.

## Assumptions

- The Home Assistant instance exposes version information via an existing API endpoint
  or WebSocket message that is already accessible with the companion's credentials.
- The version retrieval does not require additional authentication scopes beyond what
  is already granted during device registration.
- Version display fits within the existing connection status card layout without
  requiring a redesign.
- The version string is short (typically under 30 characters combined).
