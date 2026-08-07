# Phase 0 Research: Home Assistant Windows Companion (MVP)

## Decision: UI framework — WinUI 3 (Windows App SDK)

**Rationale**: User requested "Windows native UIs". WinUI 3 is Microsoft's current
native, Fluent-design UI stack, supports Mica material, system theming, and
packaged/unpackaged deployment. It pairs cleanly with WebView2 (Chromium) for
hosting the Home Assistant frontend.

**Alternatives considered**: WPF (mature but older visuals), .NET MAUI (cross-platform
overhead not needed for a Windows-only MVP). Rejected to keep the experience natively
Windows and modern.

## Decision: Authentication — long-lived access token (MVP)

**Rationale**: Home Assistant's documented app flow uses OAuth2 IndieAuth with a
custom redirect scheme, which requires app registration and redirect handling.
For an MVP, a user-provided long-lived access token (Profile → Security → Long-lived
access tokens) authenticates REST, WebSocket, and webhook calls with
`Authorization: Bearer <token>` and unblocks all three user stories.

**Alternatives considered**: Full OAuth2 IndieAuth (better UX, deferred to a later
iteration). Documented as a spec assumption.

## Decision: Dashboard hosting — WebView2 with token injection

**Rationale**: The HA frontend is a web app. Hosting it in WebView2 gives the real
dashboards for free. To avoid a second login inside the web view, inject the token
into `window.localStorage['hassTokens']` before navigation, matching how the
official companion apps seed auth. If injection fails, the user simply logs in to
the web frontend normally.

**Reference**: HA stores auth in `localStorage` under `hassTokens`
(access_token/refresh-like structure). For a long-lived token we set
`{ "access_token": "<token>", "token_type": "Bearer" }` and the frontend uses it.

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
token, and `subscribe_events` for `persistent_notification` (and optionally a
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

- OAuth2 IndieAuth login and token refresh.
- Encrypted webhook payloads (`supports_encryption=true`).
- Zeroconf discovery of the HA instance.
- Additional sensors (network, active/idle, camera/mic in-use, location).
