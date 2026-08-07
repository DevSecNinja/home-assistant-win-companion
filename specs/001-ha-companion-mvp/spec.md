# Feature Specification: Home Assistant Windows Companion (MVP)

**Feature Branch**: `001-ha-companion-mvp`

**Created**: 2026-08-06

**Status**: Draft

**Input**: User description: "Windows Home Assistant companion MVP: WebView2 dashboard, system tray, toast notifications, and Windows status sensor reporting to Home Assistant"

## Overview

A native Windows desktop companion for Home Assistant, analogous to the official
iOS/macOS companion app. It lets a user connect their Windows PC to their Home
Assistant instance, view their dashboards inside the app, receive notifications
from Home Assistant as native Windows toasts, and expose the PC's status (battery,
etc.) back to Home Assistant as sensors.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Connect and view my Home Assistant dashboard (Priority: P1)

As a Home Assistant user, I want to open a native Windows app, connect it to my
Home Assistant server, and see my normal dashboards, so I can control my home from
my PC without keeping a browser tab open.

**Why this priority**: This is the core value of a companion app and the minimum
viable product. Without it, nothing else is useful. It is independently valuable:
a user gets a dockable, always-available Home Assistant window.

**Independent Test**: Launch the app with no configuration, enter a Home Assistant
URL and a long-lived access token, and confirm the Home Assistant dashboard loads
and is interactive inside the app window. Relaunch and confirm it reconnects
without re-entering credentials.

**Acceptance Scenarios**:

1. **Given** a fresh install with no saved server, **When** the user enters a
   valid Home Assistant URL and access token, **Then** the app validates the
   connection and displays the Home Assistant frontend in the main window.
2. **Given** an invalid URL or token, **When** the user submits it, **Then** the
   app shows a clear error and does not save the credentials.
3. **Given** a previously connected server, **When** the user relaunches the app,
   **Then** the dashboard loads automatically using the securely stored token.
4. **Given** the app is showing the dashboard, **When** the user navigates within
   Home Assistant (e.g., opens a different view), **Then** navigation works as it
   does in a browser.

---

### User Story 2 - Report my PC status to Home Assistant (Priority: P2)

As a Home Assistant user, I want my Windows PC to appear as a device with sensors
(battery level, charging state, online/active status), so I can build automations
(e.g., notify me when my laptop battery is low, or when my PC is active).

**Why this priority**: This differentiates a companion app from a bookmark and
enables automations. It builds on the connection from US1 but is independently
testable once a connection exists.

**Independent Test**: With a connected app, confirm a new device appears in Home
Assistant with battery-level and battery-state sensors, and that the values update
over time and match the actual PC state.

**Acceptance Scenarios**:

1. **Given** a connected app on first run, **When** registration completes, **Then**
   the PC is registered as a mobile_app device and its sensors appear in Home
   Assistant.
2. **Given** the PC is on battery and discharging, **When** sensors update, **Then**
   the battery-level sensor reflects the current percentage and the battery-state
   sensor reads "discharging".
3. **Given** the PC is plugged in, **When** sensors update, **Then** the
   battery-state sensor reads "charging" or "plugged in".
4. **Given** a desktop PC with no battery, **When** sensors update, **Then** the
   app reports a sensible value without crashing (e.g., battery unavailable/100%).

---

### User Story 3 - Receive Home Assistant notifications as Windows toasts (Priority: P3)

As a Home Assistant user, I want notifications triggered in Home Assistant to
appear as native Windows toast notifications, so I am alerted to home events at my
desk even when the app window is minimized to the tray.

**Why this priority**: High value for presence at the desk, but depends on a live
connection and is the most complex to make robust; acceptable to ship after US1/US2.

**Independent Test**: With the app connected and minimized to the tray, trigger a
notification in Home Assistant targeting this device and confirm a Windows toast
appears with the title and message.

**Acceptance Scenarios**:

1. **Given** the app is connected and running in the tray, **When** a notification
   is sent to this device from Home Assistant, **Then** a Windows toast appears
   with the notification's title and message.
2. **Given** the connection drops, **When** it is restored, **Then** the app
   resumes receiving notifications without a restart.
3. **Given** the user clicks a toast, **When** the app is in the tray, **Then** the
   main window is restored.

---

### Edge Cases

- What happens when the Home Assistant URL is reachable but the token is expired or
  revoked? -> The app surfaces an auth error and prompts to re-authenticate; it
  does not silently retry forever with a bad token.
- How does the system handle the PC going to sleep and resuming? -> On resume the
  app reconnects with backoff and resumes sensor updates and notifications.
- What happens on a machine with no battery (desktop)? -> Battery sensors report an
  unavailable/appropriate default; the app remains functional.
- What happens when Home Assistant is temporarily unreachable? -> The app shows a
  reconnecting status and retries with exponential backoff instead of erroring out.
- What happens if the user provides an `http://` (non-TLS) local URL? -> Allowed for
  local instances, but the app warns; TLS is validated for non-local hosts.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The app MUST allow the user to enter a Home Assistant base URL and a
  long-lived access token to establish a connection.
- **FR-002**: The app MUST validate the connection before saving credentials and
  show a clear, actionable error on failure.
- **FR-003**: The app MUST store the access token and derived registration secrets
  using the Windows secure credential store (never plaintext).
- **FR-004**: The app MUST display the Home Assistant frontend inside a native
  window using an embedded web view, preserving in-app navigation.
- **FR-005**: The app MUST automatically reconnect and reload the dashboard on
  subsequent launches without re-entering credentials.
- **FR-006**: The app MUST run in the background with a system tray icon that
  provides at least: show/hide window, connection status, and exit.
- **FR-007**: The app MUST register the PC with Home Assistant as a mobile_app
  device via the native app registration endpoint on first successful connection.
- **FR-008**: The app MUST register and periodically update at least a battery-level
  sensor and a battery-state sensor for the PC.
- **FR-009**: Sensor values MUST reflect the actual PC state and update on a
  reasonable interval and on relevant system events (e.g., power source change).
- **FR-010**: The app MUST receive notifications sent from Home Assistant to this
  device and display them as native Windows toast notifications.
- **FR-011**: The app MUST recover from transient network loss and Home Assistant
  restarts using automatic reconnection with backoff.
- **FR-012**: The app MUST surface connection state (connected / reconnecting /
  auth error / disconnected) to the user.
- **FR-013**: The app MUST NOT log or display secrets (tokens, webhook secrets).
- **FR-014**: The app MUST allow the user to sign out / disconnect, which removes
  stored credentials.

### Key Entities *(include if feature involves data)*

- **Server Connection**: The configured Home Assistant instance — base URL,
  connection status, and a reference to the stored access token.
- **Device Registration**: The identity of this PC within Home Assistant — the
  `webhook_id`, optional cloud/remote URLs, encryption secret, and a stable device
  identifier.
- **Sensor**: A reported PC metric — unique id, type (sensor/binary_sensor), device
  class, name, current state, unit, and attributes (e.g., battery level, battery
  state).
- **Notification**: An inbound message from Home Assistant — title, message, and
  optional data used to render a Windows toast.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A first-time user can go from launching the app to seeing their live
  dashboard in under 2 minutes given a URL and token.
- **SC-002**: After the first successful connection, the PC and its battery sensors
  appear in Home Assistant within 1 minute.
- **SC-003**: Battery-level and battery-state sensor values match the OS-reported
  values (within one update interval) at least 95% of the time during a session.
- **SC-004**: A notification sent from Home Assistant to the device appears as a
  Windows toast within 10 seconds while the app is connected.
- **SC-005**: After a simulated network drop of up to 2 minutes, the app
  automatically returns to the connected state without user intervention.
- **SC-006**: No token or secret value is ever written to log output or the on-disk
  configuration in plaintext (verified by inspection).

## Assumptions

- The user has a running Home Assistant instance reachable from the PC and can
  create a long-lived access token (Profile -> Long-lived access tokens).
- For the MVP, authentication uses a long-lived access token rather than the full
  OAuth2 IndieAuth login flow (which may be added later).
- For the MVP, notifications are delivered by the app maintaining a live connection
  to Home Assistant (WebSocket/event subscription) rather than a cloud push service,
  since Windows has no equivalent of the mobile push channel used on iOS/Android.
- Encrypted webhook payloads are optional for the MVP; the app MAY send sensor
  updates unencrypted over TLS. Encryption can be added later.
- Target OS is Windows 10 (build 19041+) and Windows 11 on x64/ARM64.
- Single Home Assistant server per app instance for the MVP (multi-server later).
