# Feature Specification: Live Sensor Previews

**Feature Branch**: `devsecninja-live-sensor-previews`

**Created**: 2026-08-18

**Status**: Implemented

**Input**: User description: "On the sensors page, make sure sensor previews automatically refresh while the page remains open, including Now Playing."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Observe Live Sensor Changes (Priority: P1)

As a user viewing the Sensors page, I can see enabled sensor previews update without leaving and reopening the page, so the displayed values reflect the current state of my device.

**Why this priority**: A stale preview makes it difficult to verify that a sensor is working and is especially noticeable for frequently changing values such as Now Playing.

**Independent Test**: Open the Sensors page, change an enabled sensor's underlying value, and verify that its preview changes while the page remains open.

**Acceptance Scenarios**:

1. **Given** the Sensors page is visible and an enabled sensor value changes, **When** the next preview refresh occurs, **Then** the displayed preview shows the new value without navigation or manual action.
2. **Given** Now Playing is enabled and the active media changes, **When** the Sensors page remains visible, **Then** its preview updates within 5 seconds.
3. **Given** multiple sensor values change, **When** previews refresh, **Then** each visible enabled sensor shows its latest available value.

---

### User Story 2 - Avoid Hidden Refresh Work (Priority: P2)

As a user, I want preview refresh activity to stop when I leave the Sensors page so that the convenience does not create unnecessary background work.

**Why this priority**: Automatic refresh must preserve battery, CPU, privacy, and sensor lifecycle expectations.

**Independent Test**: Navigate away from the Sensors page and verify that page-driven preview reads stop, then return and verify that refreshing resumes.

**Acceptance Scenarios**:

1. **Given** the Sensors page is no longer visible, **When** time passes, **Then** the page does not request additional sensor previews.
2. **Given** the user returns to the Sensors page, **When** the page becomes visible, **Then** previews refresh promptly and continue updating while it remains visible.
3. **Given** the page is repeatedly opened and closed, **When** navigation completes, **Then** only the currently visible page can drive preview refreshes.

### Edge Cases

- A refresh is still in progress when the user navigates away.
- A sensor read is slow, fails, or is temporarily unavailable.
- A sensor is enabled or disabled while automatic refresh is active.
- The app window is minimized, closed to the tray, or suspended while the Sensors page was selected.
- Search filtering changes which sensors are visible during a refresh.
- A sensitive sensor remains disabled and must not be read merely to populate a preview.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The Sensors page MUST automatically refresh previews while it is visible.
- **FR-002**: A changed preview MUST become visible within 5 seconds under normal operating conditions.
- **FR-003**: The initial set of previews MUST refresh promptly whenever the Sensors page becomes visible.
- **FR-004**: Page-driven preview refreshes MUST stop when the Sensors page is no longer visible.
- **FR-005**: At most one page-driven preview refresh MUST be active at a time.
- **FR-006**: Leaving the page MUST cancel or safely disregard unfinished preview work so stale results cannot overwrite newer page state.
- **FR-007**: Automatic refresh MUST preserve sensor privacy rules and MUST NOT read disabled sensitive sensors.
- **FR-008**: A failure to preview one sensor MUST NOT prevent later refreshes or other sensor previews from updating, and existing user-facing unavailable/error behavior MUST remain intact.
- **FR-009**: Changes to enabled state or search filtering MUST be reflected by the next refresh without requiring navigation.
- **FR-010**: Automatic preview refresh MUST pause when the application is not actively presenting the Sensors page, including when minimized, closed to the tray, or suspended.
- **FR-011**: A manual refresh control is out of scope because automatic refresh satisfies the primary workflow without additional user action.

### Key Entities

- **Sensor Preview**: The user-visible current state and attributes for an enabled sensor, subject to privacy gating.
- **Refresh Session**: The bounded period during which the Sensors page is actively visible and permitted to request updated previews.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In normal use, changed Now Playing and other enabled sensor previews appear within 5 seconds while the Sensors page remains visible.
- **SC-002**: No page-driven preview reads occur after the user leaves, minimizes, closes to tray, or suspends the visible Sensors page.
- **SC-003**: Reopening the Sensors page displays current preview values within 2 seconds for sensors that respond normally.
- **SC-004**: Repeatedly opening and closing the page 20 times produces no duplicate refresh activity and no stale update after navigation.
- **SC-005**: Disabled sensitive sensors produce zero preview reads during automatic refresh.

## Assumptions

- A refresh interval of approximately 2 seconds is frequent enough for live feedback while avoiding needlessly aggressive work.
- Automatic refresh reads values only from enabled sources that explicitly expose a non-collecting cached snapshot. Other previews remain at their initial value, demo-mode previews remain static, and Home Assistant transmission frequency is unchanged.
- Existing sensor enablement, privacy, unavailable-state, and error-display behavior remains authoritative.
- Sensors that legitimately need longer than the target interval may show their latest completed value rather than overlapping reads.

## Implementation Evidence

- The Sensors page refreshes cached current-value text for enabled sensors every two seconds without rebuilding rows or changing search state.
- Preview reads are single-flight and cancelled when navigating away, hiding to tray, minimizing, or shutting down.
- Restoring or showing the window while Sensors remains selected requests an immediate fresh preview.
- Sensitive disabled sensors continue to use the catalog's privacy gate and are not read.
- Focused App tests cover single-flight cancellation and presentation gating; a Windows UI scenario observes a battery preview changing while the page remains open.
