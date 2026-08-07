# Contract: Sensors (webhook)

Home Assistant native app integration — used by US2.

## Endpoint

`POST {BaseUrl}/api/webhook/{webhook_id}`

No auth header required (the webhook_id is the capability). `Content-Type: application/json`.
For the MVP payloads are sent unencrypted over TLS.

## Register a sensor (one at a time)

```json
{
  "type": "register_sensor",
  "data": {
    "unique_id": "battery_level",
    "type": "sensor",
    "name": "Battery Level",
    "state": 87,
    "device_class": "battery",
    "unit_of_measurement": "%",
    "state_class": "measurement",
    "entity_category": "diagnostic",
    "icon": "mdi:battery"
  }
}
```

Repeat for `battery_state`:

```json
{
  "type": "register_sensor",
  "data": {
    "unique_id": "battery_state",
    "type": "sensor",
    "name": "Battery State",
    "state": "discharging",
    "device_class": "enum",
    "entity_category": "diagnostic",
    "icon": "mdi:battery-charging"
  }
}
```

## Update sensors (batch)

```json
{
  "type": "update_sensor_states",
  "data": [
    { "unique_id": "battery_level", "state": 86, "icon": "mdi:battery" },
    { "unique_id": "battery_state", "state": "charging", "icon": "mdi:battery-charging" }
  ]
}
```

## Behavior

- Register each `unique_id` once before updating it.
- Disabling or re-enabling a sensor uses `register_sensor` with an explicit
  `disabled: true` or `disabled: false`; `update_sensor_states` ignores that flag.
- A disabled or retired sensor remains in Home Assistant's entity registry as a
  disabled entity. It is not deleted.
- A 200 with `{ "unique_id": { "success": true } }` (or 200 empty) indicates success.
- If an update returns that a sensor is not registered, re-register then retry.
- Update on a timer (e.g., every 60s) and immediately on power-source change.
