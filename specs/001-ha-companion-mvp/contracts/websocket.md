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
  reconnect with exponential backoff, then re-auth and re-open the push channel.
- `auth_invalid` -> terminal `AuthError` (stop retrying; prompt re-auth).

## Notes

- Message `id` values must be a strictly increasing integer per connection.
- Home Assistant does **not** fire a `persistent_notification` event on the event
  bus (the component uses an internal dispatcher signal), so `subscribe_events`
  with that `event_type` never fires. Use the push channel above. To mirror the
  notification drawer instead, use the `persistent_notification/subscribe`
  WebSocket command.
