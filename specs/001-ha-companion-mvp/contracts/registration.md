# Contract: Device Registration

Home Assistant native app integration — used by US2.

## Endpoint

`POST {BaseUrl}/api/mobile_app/registrations`

Headers: `Authorization: Bearer <long-lived-access-token>`, `Content-Type: application/json`

## Request body

```json
{
  "device_id": "<stable-guid>",
  "app_id": "io.homeassistant.windows",
  "app_name": "Home Assistant Windows Companion",
  "app_version": "0.1.0",
  "device_name": "DESKTOP-ABC123",
  "manufacturer": "Contoso",
  "model": "Windows PC",
  "os_name": "Windows",
  "os_version": "10.0.22631",
  "supports_encryption": false
}
```

## Success response (200)

```json
{
  "webhook_id": "abcdefgh",
  "secret": null,
  "cloudhook_url": null,
  "remote_ui_url": null
}
```

Persist `webhook_id` (required) and any URLs. If a 404 is returned, the
`mobile_app` integration is not loaded — surface an actionable error.

## Error handling

- 401 → AuthError (invalid/expired token).
- 404 → mobile_app integration not enabled.
- Other → transient; retry with backoff.
