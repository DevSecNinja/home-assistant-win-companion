# Feature Specification: Home Assistant Windows Companion

**Feature Branch**: `001-ha-companion-foundation`

**Created**: 2026-08-06

**Status**: Shipped

**Input**: User description: "Windows Home Assistant companion: lean tray-resident companion (opens Home Assistant in the browser), OAuth2 loopback sign-in, system tray, toast notifications, and Windows status sensor reporting to Home Assistant"

## Overview

A native Windows desktop companion for Home Assistant, analogous to the official
iOS/macOS companion app. It lets a user connect their Windows PC to their Home
Assistant instance, quickly open Home Assistant in their browser from the app or its
tray icon, receive notifications from Home Assistant as native Windows toasts, and
expose the PC's status (battery, etc.) back to Home Assistant as sensors.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Connect and open my Home Assistant (Priority: P1)

As a Home Assistant user, I want to open a native Windows app, connect it to my
Home Assistant server once, and quickly open my Home Assistant in my browser from
the app or its tray icon, so I have a lightweight always-available companion
without keeping a browser tab pinned.

**Why this priority**: This is the core connect-and-launch value and the minimum
viable product. Without a validated connection nothing else works. It is
independently valuable: a user gets a tray-resident companion with one-click access
to their Home Assistant.

**Independent Test**: Launch the app with no configuration, enter a Home Assistant
URL, sign in through the browser, confirm the connection is established and saved,
then click "Open Home Assistant" and confirm the instance opens in the default
browser. Relaunch and confirm it resumes without re-entering credentials.

**Acceptance Scenarios**:

1. **Given** a fresh install with no saved server, **When** the user enters a
   valid Home Assistant URL and signs in via the browser, **Then** the app
   establishes the connection, securely stores the refresh token, and shows a
   connected status.
2. **Given** an invalid URL or a cancelled/failed browser sign-in, **When** the
   user submits it, **Then** the app shows a clear error and does not save any
   credentials.
3. **Given** a previously connected server, **When** the user relaunches the app,
   **Then** it resumes automatically using the securely stored refresh token.
4. **Given** the app is connected, **When** the user clicks "Open Home Assistant"
   (in the window or the tray menu), **Then** the Home Assistant instance opens in
   the user's default browser.

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
- What happens if the user provides an `http://` (non-TLS) URL? -> It is allowed
  for instances intentionally served over HTTP. HTTPS certificates are validated
  normally whenever HTTPS is used.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The app MUST allow the user to enter a Home Assistant base URL and
  sign in via Home Assistant's OAuth2 flow in their default web browser (no manual
  token creation).
- **FR-002**: The app MUST validate the connection before saving credentials and
  show a clear, actionable error on failure.
- **FR-003**: The app MUST store the OAuth refresh token (and any derived secrets)
  using the Windows secure credential store (never plaintext), and use it to
  silently obtain short-lived access tokens.
- **FR-004**: The app MUST provide a way to open the Home Assistant frontend in the
  user's default web browser (from the window and the tray menu). It MUST NOT embed
  a web view.
- **FR-005**: The app MUST automatically reconnect on subsequent launches without
  re-authenticating, by refreshing the stored refresh token.
- **FR-006**: The app MUST run in the background with a system tray icon that
  provides at least: show/hide window, connection status, and exit. Exit MUST
  remove the tray icon, close the window, stop background reporting, and terminate
  the process gracefully. Windows Restart Manager MUST be able to request that same
  graceful shutdown during installed upgrades and uninstalls.
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
- **FR-014**: The app MUST allow reporting and notifications to be paused without
  discarding credentials, and MUST provide a separate confirmed remove-server
  action that revokes the refresh token and removes stored credentials and config.

### Key Entities *(include if feature involves data)*

- **Server Connection**: The configured Home Assistant instance — base URL,
  connection status, and a reference to the stored OAuth refresh token.
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

- **SC-001**: A first-time user can go from launching the app to a connected state
  and opening Home Assistant in their browser in under 2 minutes.
- **SC-002**: After the first successful connection, the PC and its battery sensors
  appear in Home Assistant within 1 minute.
- **SC-003**: Battery-level and battery-state sensor values match the OS-reported
  values (within one update interval) at least 95% of the time during a session.
- **SC-004**: A notification sent from Home Assistant to the device appears as a
  Windows toast within 10 seconds while the app is connected.
- **SC-005**: After a simulated network drop of up to 2 minutes, the app
  automatically returns to the connected state without user intervention.
- **SC-006**: No refresh/access token or secret value is ever written to log output
  or the on-disk configuration in plaintext (verified by inspection).

## Assumptions

- The user has a running Home Assistant instance reachable from the PC and a normal
  Home Assistant user account they can log in with via the browser.
- Authentication uses Home Assistant's OAuth2 (IndieAuth) login flow with a loopback
  redirect; no long-lived access token is created or pasted by the user.
- Notifications are delivered through Home Assistant's
  `mobile_app/push_notification_channel` local-push WebSocket command. Registration
  declares `app_data.push_websocket_channel = true`, and each delivery is confirmed.
- Encrypted webhook payloads are optional for the initial release; the app MAY send sensor
  updates unencrypted over TLS. Encryption can be added later.
- Target OS is Windows 10 (build 19041+) and Windows 11 on x64/ARM64.
- Single Home Assistant server per app instance for the initial release (multi-server later).
