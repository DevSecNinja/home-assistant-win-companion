# Contract: Fake Home Assistant Protocol Surface

## General rules

- The server binds only to `127.0.0.1` on an OS-assigned port.
- Authorization headers, tokens, webhook identifiers, and configured sensitive
  sensor values are validated but redacted before recording.
- Unknown endpoints return 404.
- Scenario faults override healthy responses explicitly and are reset on disposal.

## OAuth

### `GET /auth/authorize`

Validates `client_id`, `redirect_uri`, and `state`, then redirects to the supplied
loopback URI with a scenario-generated authorization code and the unchanged state.
The redirect URI must itself be loopback.

### `POST /auth/token`

Accepts form-encoded authorization-code exchange, refresh, and revoke requests.
Healthy exchanges return synthetic bearer tokens. Rejected grants return the same
status class consumed by the production OAuth client.

## REST

### `GET /api/`

Requires the scenario access token and returns a healthy API response.

### `GET /api/config`

Requires the scenario access token and returns the scenario base URL and synthetic
version/instance information.

### `POST /api/mobile_app/registrations`

Requires the scenario access token, validates the companion registration payload,
records the device, and returns a synthetic webhook identifier. Repeated calls for
the same persisted journey are recorded as duplicates rather than hidden.

### `POST /api/webhook/{webhookId}`

Accepts the companion's `update_registration`, `register_sensor`,
`update_sensor_states`, and `get_config` payloads. It reproduces the HA behaviors
the client relies on:

- Unknown webhook: empty successful response.
- Deleted webhook: gone response.
- Sensor rejection: successful HTTP response with per-sensor failure details.
- `get_config`: synthetic instance device ID and version.

## WebSocket `/api/websocket`

1. Server sends `auth_required`.
2. Client sends `auth` with the scenario access token.
3. Server sends `auth_ok` or configured `auth_invalid`.
4. Client requests `mobile_app/push_notification_channel`.
5. Server acknowledges subscription.
6. Tests may send notification events through the scenario API.
7. Confirmation-requesting notifications must produce
   `mobile_app/push_notification_confirm`.

The scenario may close the connection at named steps and observe a later
reconnection without reusing a disposed session object.

## Interaction API

Tests use in-process typed methods, not an administrative HTTP endpoint:

- Activate or clear a fault.
- Await the next interaction matching kind and predicate.
- Read a sanitized snapshot of interactions.
- Send a notification to authenticated subscribers.
- Close active WebSocket sessions.

All waits accept cancellation and fail with a message containing the sanitized
interaction history.
