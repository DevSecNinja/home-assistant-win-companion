# Contract: WebSocket (notifications + connection liveness)

Home Assistant WebSocket API - used by US3 and connection state.

## Endpoint

`{ws|wss}://{host}/api/websocket`  (derive scheme/host from BaseUrl)

## Handshake

1. Server -> client: `{ "type": "auth_required", "ha_version": "..." }`
2. Client -> server: `{ "type": "auth", "access_token": "<access-token>" }`
3. Server -> client: `{ "type": "auth_ok" }` (or `{ "type": "auth_invalid" }` -> AuthError)

## Open the local push notification channel

Windows has no APNS/FCM equivalent, so notifications are delivered over this
authenticated WebSocket using the `mobile_app` **local push channel**.

> Requires the registration to declare `app_data.push_websocket_channel = true`
> (see `registration.md`). That flag makes Home Assistant's `supports_push()`
> return true, which is what exposes the PC as a notify target.

```json
{
  "id": 1,
  "type": "mobile_app/push_notification_channel",
  "webhook_id": "<webhook_id>",
  "support_confirm": true
}
```

Server acknowledges: `{ "id": 1, "type": "result", "success": true }`

Pushed notifications arrive as `event` messages whose `event` object *is* the
notification payload (there is no nested `data` / `event_type`):

```json
{
  "id": 1,
  "type": "event",
  "event": {
    "message": "...",
    "title": "...",
    "hass_confirm_id": "<opaque>"
  }
}
```

Map `title` / `message` -> Windows toast.

## Confirming delivery

Because we request `support_confirm: true`, every notification carrying a
`hass_confirm_id` MUST be acknowledged within 10 seconds:

```json
{
  "id": 2,
  "type": "mobile_app/push_notification_confirm",
  "webhook_id": "<webhook_id>",
  "confirm_id": "<hass_confirm_id>"
}
```

If we do not confirm in time, Home Assistant tears the channel down and falls
back to cloud push (which this app does not have), so the notification is lost
and the channel must be re-established.

## Liveness

- Send periodic `{ "id": n, "type": "ping" }`; expect `{ "type": "pong" }`.
- On socket close/error or missed pong -> transition to `Reconnecting` and
  reconnect with exponential backoff (1, 2, 4, 8... seconds, capped at 60
  seconds) plus 0-20% positive jitter, then re-auth and re-open the push channel.
- A socket that closes before 30 authenticated seconds does not reset the
  progression. This prevents a server or intermediary that accepts and immediately
  closes connections from holding the client at the shortest retry interval.
- An explicit user retry or a meaningful Windows network-profile change may bypass
  one pending delay. Duplicate events coalesce and never start a parallel attempt.
- While Windows reports no usable network, retry waits are five minutes. Returning
  online bypasses that wait once.
- `auth_invalid` -> terminal `AuthError` (stop retrying; prompt re-auth).

Sensor delivery has its own single-flight loop. Failed periodic pushes back off
from the normal sync interval to a 15 minute cap. Change-driven pushes coalesce
while healthy and do not queue during an outage. Shutdown, disconnect, route
switches and server removal cancel both loops and their pending waits.

## Notes

- Message `id` values must be a strictly increasing integer per connection.
- Home Assistant does **not** fire a `persistent_notification` event on the event
  bus (the component uses an internal dispatcher signal), so `subscribe_events`
  with that `event_type` never fires. Use the push channel above. To mirror the
  notification drawer instead, use the `persistent_notification/subscribe`
  WebSocket command.
