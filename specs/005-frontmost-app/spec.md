# Feature Specification: Frontmost Application Sensor

**Feature Branch**: `feature/005-frontmost-app`

**Created**: 2026-08-07

**Status**: Draft

**Input**: User description: "Add privacy-sensitive, debounced frontmost app
reporting from issue #15."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Report the active application safely (Priority: P1)

As a privacy-conscious user, I want an optional sensor showing which application is
active without exposing document or browser-tab titles by default.

**Independent Test**: Enable the sensor, switch between two applications, wait for
the settle period, and verify Home Assistant receives only the executable name.

**Acceptance Scenarios**:

1. A new installation shows the sensor disabled and labels it as sensitive.
2. Enabling it reports the active application name without the window title.
3. Disabling it releases foreground-window observation entirely.

---

### User Story 2 - Explicitly opt into full window titles (Priority: P2)

As a user who accepts the privacy risk, I want to choose full-title mode for richer
automations.

**Independent Test**: Select full-title mode, focus a titled window, and verify the
local preview and entity use the title while never exposing an untruncated copy.

**Acceptance Scenarios**:

1. Changing to full-title mode clearly warns that titles may contain private data.
2. The choice persists across restarts.
3. Values longer than 255 characters are truncated with no full-value attribute.

---

### User Story 3 - Avoid noisy updates (Priority: P3)

As a Home Assistant operator, I want rapid window switching to settle locally so it
does not create excessive webhook traffic.

**Independent Test**: Switch windows repeatedly within five seconds and verify the
source retains only the final settled value and does not request an immediate push.

**Acceptance Scenarios**:

1. Foreground changes are debounced for four seconds.
2. Identical consecutive values do not change the cached reading.
3. The settled value rides the existing periodic or manual sensor batch.

### Edge Cases

- The foreground window disappears during inspection.
- The owning process exits or cannot be queried.
- A window has no title.
- Secure/system windows deny process access.
- The mode changes while a debounce timer is pending.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Provide one `frontmost_app` sensor, disabled by default.
- **FR-002**: Default to application-name-only mode.
- **FR-003**: Full-title mode MUST require an explicit persisted choice and warning.
- **FR-004**: Observe foreground changes without polling and release the hook while
  disabled.
- **FR-005**: Debounce changes for four seconds and deduplicate identical values.
- **FR-006**: Foreground events MUST NOT request immediate Home Assistant pushes.
- **FR-007**: Values MUST be limited to 255 characters with no untruncated attribute.
- **FR-008**: Values MUST NOT be written to logs.
- **FR-009**: Local preview MUST show the value for the selected mode without
  transmitting it.

### Key Entities

- **Frontmost app mode**: Application name or full window title.
- **Foreground snapshot**: Current local application name and title.
- **Settled value**: Debounced, deduplicated value used by sensor reads.

## Success Criteria *(mandatory)*

- **SC-001**: The final value after rapid switching settles within five seconds.
- **SC-002**: Rapid switching produces no additional webhook call before the normal
  sync or Update now action.
- **SC-003**: No reported state exceeds 255 characters.
- **SC-004**: Disabled operation registers no foreground hook.
- **SC-005**: Full window titles appear only after explicit user selection.

## Assumptions

- Four seconds balances responsiveness and churn.
- Application names include the executable extension where available.
- An unavailable process/title is reported as `Unavailable`.
