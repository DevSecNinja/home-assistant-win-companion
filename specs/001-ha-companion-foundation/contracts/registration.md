# Contract: Device Registration

Home Assistant native app integration — used by US2.

## Endpoint

`POST {BaseUrl}/api/mobile_app/registrations`

Headers: `Authorization: Bearer <access-token>`, `Content-Type: application/json`

## Request body

```json
{
  "device_id": "<stable-guid>",
  "app_id": "io.homeassistant.windows",
  "app_name": "Windows Companion for Home Assistant",
  "app_version": "0.1.0",
  "device_name": "DESKTOP-ABC123",
  "manufacturer": "Contoso",
  "model": "Windows PC",
  "os_name": "Windows",
  "os_version": "10.0.22631",
  "supports_encryption": false,
  "app_data": { "push_websocket_channel": true }
}
```

`app_data.push_websocket_channel` is what makes Home Assistant's `supports_push()`
return true for this device. Without it the PC registers fine and reports sensors,
but never appears as a notify target and can never receive notifications.

## Updating an existing registration

`POST {BaseUrl}/api/webhook/{webhook_id}` (no auth) with:

```json
{
  "type": "update_registration",
  "data": {
    "app_data": { "push_websocket_channel": true },
    "app_version": "0.1.0",
    "device_name": "DESKTOP-ABC123",
    "manufacturer": "Contoso",
    "model": "Windows PC",
    "os_version": "10.0.22631"
  }
}
```

`app_version`, `device_name`, `manufacturer` and `model` are **all required** by
HA's schema — omitting any one fails validation and silently leaves push disabled.

> Caveat: `update_registration` reloads the *legacy* `notify.mobile_app_<device>`
> services, but it does **not** reload the config entry. The newer notify *entity*
> (the one `notify.send_message` targets) is only created during
> `async_setup_entry`, so a device that first registered without push support needs
> a one-time reload of the Mobile App integration (or an HA restart) before it
> appears under `notify.send_message`. Fresh registrations are unaffected.

## Success response (200)

```json
{
  "webhook_id": "abcdefgh",
  "secret": null,
  "cloudhook_url": null,
  "remote_ui_url": null
}
```

Persist `webhook_id` (required) and any URLs. **`webhook_id` is a capability secret**
— it authenticates sensor and push traffic on its own — so it belongs in the platform
secret store, not in settings.json. If a 404 is returned, the
`mobile_app` integration is not loaded — surface an actionable error.

## Error handling

- 401 → AuthError (invalid/expired token).
- 404 → mobile_app integration not enabled.
- Other → transient; retry with backoff.
