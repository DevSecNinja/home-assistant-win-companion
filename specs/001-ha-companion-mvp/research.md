# Phase 0 Research: Home Assistant Windows Companion (MVP)

## Decision: UI framework — WinUI 3 (Windows App SDK)

**Rationale**: User requested "Windows native UIs". WinUI 3 is Microsoft's current
native, Fluent-design UI stack, supports Mica material, system theming, and
packaged/unpackaged deployment. The companion is lean and tray-resident, so it does
not embed a browser; Home Assistant itself opens in the user's default browser.

**Alternatives considered**: WPF (mature but older visuals), .NET MAUI (cross-platform
overhead not needed for a Windows-only MVP). Rejected to keep the experience natively
Windows and modern.

## Decision: Authentication — OAuth2 (IndieAuth) loopback (MVP)

**Rationale**: To avoid asking users to create and paste a long-lived token, the
companion uses Home Assistant''s OAuth2 IndieAuth flow with a **loopback redirect**.
The app opens `/auth/authorize` in the default browser with
`client_id == redirect_uri == http://localhost:<fixed-port>/`; HA accepts this
because the two share an origin (verified against home-assistant/core
`indieauth.verify_redirect_uri`, which allows loopback hosts and same scheme+netloc
without fetching the client_id page). A local `TcpListener` captures the returned
`code`, which is exchanged at `/auth/token` for an access + refresh token. Access
tokens (~30 min) are refreshed on demand; the refresh token is stored in the
Credential Locker.

**Fixed port requirement**: HA''s `/auth/token` refresh grant checks
`refresh_token.client_id == client_id` (home-assistant/core auth token endpoint,
`_async_handle_refresh_token`). Because `client_id` equals the loopback redirect
URL, the port must be **fixed** across restarts, or refresh would fail. We use a
fixed dedicated port.

**Alternatives considered**: User-pasted long-lived token (worse UX, more error-prone,
rejected); ephemeral loopback port (breaks refresh due to client_id validation,
rejected).

## Decision: Home Assistant access — open in default browser (no embedded web view)

**Rationale**: A lean companion keeps the app small and avoids shipping/securing a
Chromium runtime. Instead of embedding the HA frontend, the companion exposes an
"Open Home Assistant" action (window button + tray menu) that launches the configured
base URL in the user''s default browser via `Process.Start` with `UseShellExecute`.
The browser already holds the user''s HA web session, so no token injection is needed.

## Decision: Device registration — `/api/mobile_app/registrations`

**Rationale**: Documented native app integration. POST with device/app metadata and
`supports_encryption=false` returns `{ webhook_id, secret?, cloudhook_url?,
remote_ui_url? }`. `webhook_id` is then used for all sensor traffic. We persist a
stable `device_id` (GUID) and the returned `webhook_id`.

**Key fields we send**: `device_id`, `app_id=io.homeassistant.windows`,
`app_name="Home Assistant Windows Companion"`, `app_version`, `device_name`
(machine name), `manufacturer`, `model`, `os_name="Windows"`, `os_version`,
`supports_encryption=false`.

## Decision: Sensors — webhook `register_sensor` / `update_sensor`

**Rationale**: Documented. Register once per unique_id, then send periodic
`update_sensor` batches. MVP sensors:

- `battery_level` — `type=sensor`, `device_class=battery`, `unit=%`,
  `state_class=measurement`, value 0–100.
- `battery_state` — `type=sensor`, `device_class=enum`, values like
  `charging`/`discharging`/`full`/`not_charging`/`plugged in`.

Battery data comes from Win32 `GetSystemPowerStatus` (P/Invoke), which returns AC
line status, battery flag, and battery life percent, and works for laptops; desktops
without a battery report "no system battery" → handled gracefully.

## Decision: Notifications — WebSocket subscription → Windows toast

**Rationale**: Windows has no push channel equivalent to APNS/FCM used by the mobile
apps. For the MVP, keep a live WebSocket to `/api/websocket`, authenticate with the
access token, and `subscribe_events` for `persistent_notification` (and optionally a
user-defined event). When such an event fires, render a native toast via the Windows
App SDK `AppNotification` API. Toast activation restores the main window.

**Alternatives considered**: Implementing an actual push receiver (out of scope; no
Windows push transport for HA), or polling REST (higher latency, chattier).

## Decision: Secret storage — Windows Credential Locker (PasswordVault)

**Rationale**: `Windows.Security.Credentials.PasswordVault` provides per-user
encrypted storage without extra dependencies. Wrapped behind `ISecretStore` so tests
use an in-memory fake and the constitution's no-plaintext rule is enforced.

## Decision: Tray icon — H.NotifyIcon.WinUI

**Rationale**: WinUI 3 has no built-in `NotifyIcon`. `H.NotifyIcon.WinUI` is the
widely used, maintained library for a system tray icon and context menu in WinUI 3,
enabling run-in-background with show/hide/exit and status.

## Resilience notes

- Reconnect uses exponential backoff (e.g., 1s, 2s, 4s … capped at 60s) with jitter.
- Subscribe to `SystemEvents.PowerModeChanged` / session resume to trigger immediate
  reconnect and a sensor refresh.
- Auth failures (401/invalid token) transition to an `AuthError` state that stops the
  retry loop and prompts re-authentication rather than hammering the server.

## Open items (deferred, not MVP)

- Encrypted webhook payloads (`supports_encryption=true`).
- Zeroconf discovery of the HA instance.
- Additional sensors (network, active/idle, camera/mic in-use, location).
