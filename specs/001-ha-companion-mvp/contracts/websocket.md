# Contract: WebSocket (notifications + connection liveness)

Home Assistant WebSocket API — used by US3 and connection state.

## Endpoint

`{ws|wss}://{host}/api/websocket`  (derive scheme/host from BaseUrl)

## Handshake

1. Server → client: `{ "type": "auth_required", "ha_version": "..." }`
2. Client → server: `{ "type": "auth", "access_token": "<long-lived-token>" }`
3. Server → client: `{ "type": "auth_ok" }` (or `{ "type": "auth_invalid" }` → AuthError)

## Subscribe to notification events

```json
{ "id": 1, "type": "subscribe_events", "event_type": "persistent_notification" }
```

Server acknowledges: `{ "id": 1, "type": "result", "success": true }`

Event messages arrive as:

```json
{
  "id": 1,
  "type": "event",
  "event": {
    "event_type": "persistent_notification",
    "data": { "notification_id": "...", "message": "...", "title": "..." }
  }
}
```

Map `title`/`message` → Windows toast.

## Liveness

- Send periodic `{ "id": n, "type": "ping" }`; expect `{ "type": "pong" }`.
- On socket close/error or missed pong → transition to `Reconnecting` and reconnect
  with exponential backoff, then re-auth and re-subscribe.
- `auth_invalid` → terminal `AuthError` (stop retrying; prompt re-auth).

## Notes

- Message `id` values must be a strictly increasing integer per connection.
- Optionally also subscribe to a user-configured `event_type` for custom
  notifications in a later iteration.
