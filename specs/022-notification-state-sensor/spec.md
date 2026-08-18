# Feature Specification: Notification State Sensor

**Status**: Shipped

**Created**: 2026-08-18

**Input**: Retroactive documentation of shipped `NotificationStateSensorSource` and
`NotificationStateFormatter` (originally part of 003-meeting-sensors).

Add a `user_notification_state` sensor that reports whether Windows considers the
PC busy, presenting, full-screen, or ready for notifications.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Detect presenting or interruption state (Priority: P1)

As a Home Assistant user, I want my PC to report whether Windows considers me
busy, presenting, full-screen, or in quiet time so automations can avoid
interrupting me.

**Independent Test**: Enable presentation mode or a full-screen application and
verify the sensor changes state, then returns to normal afterwards.

**Acceptance Scenarios**:

1. **Given** normal desktop use, **When** the sensor reports, **Then** its state
   is `Accepts Notifications`.
2. **Given** Windows enters presentation mode, **When** the state changes,
   **Then** the sensor reports `Presentation` within one polling interval.
3. **Given** a new installation, **When** the catalog is shown, **Then** this
   sensor is enabled by default.

### Edge Cases

- `SHQueryUserNotificationState` returning an undefined enum value produces
  `Unknown` rather than crashing.
- Windows 11 Focus / Do Not Disturb is explicitly NOT covered; the attribute
  `includes_do_not_disturb` is always `false`.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The catalog MUST offer a `user_notification_state` sensor reporting
  the current Windows notification state as a human-readable string.
- **FR-002**: The sensor MUST be enabled by default as a diagnostic entity.
- **FR-003**: The sensor MUST expose a `suppresses_notifications` boolean
  attribute for automation use.
- **FR-004**: The sensor MUST expose `includes_do_not_disturb: false` to prevent
  misinterpretation.
- **FR-005**: Polling MUST run only while the sensor is enabled.
- **FR-006**: Unchanged readings MUST NOT produce Home Assistant traffic.

### Key Entities

- **Notification state**: One of the `QUERY_USER_NOTIFICATION_STATE` values from
  `SHQueryUserNotificationState`.
- **Suppression logic**: Deterministic evaluation in Core.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: State changes appear within 10 seconds of a Windows state transition.
- **SC-002**: With the sensor disabled, zero polling work occurs.
- **SC-003**: Automation authors can trigger on `suppresses_notifications` without
  parsing the string state.

## Privacy

- No sensitive data. The sensor describes system presentation context only.
