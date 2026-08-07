# Phase 1 Data Model: Home Assistant Windows Companion (MVP)

## Entities

### ServerConfig (non-secret, persisted to settings.json)

| Field         | Type     | Notes                                             |
| ------------- | -------- | ------------------------------------------------- |
| BaseUrl       | string   | Home Assistant base URL, e.g. `https://ha.local:8123` |
| DeviceId      | string   | Stable GUID for this install (registration `device_id`) |
| RemoteUiUrl   | string?  | Optional Nabu Casa remote URL                     |
| Sensors       | object   | Per-sensor enablement and the idle threshold      |

`WebhookId` and `CloudhookUrl` are held on this object in memory but are **never
serialized** — see Secrets below.

Validation: `BaseUrl` must be an absolute http/https URI. `DeviceId` is generated
once and never changed.

### Secrets (stored only in Credential Locker, never settings.json)

| Key             | Value                                        |
| --------------- | -------------------------------------------- |
| refresh_token   | OAuth2 refresh token (from `/auth/token`)    |
| webhook_id      | Capability secret from registration          |
| cloudhook_url   | Embeds the webhook id, so equally sensitive  |
| webhook_secret  | `secret` from registration (if encryption)   |

`webhook_id` is a **capability**, not an identifier: it needs no auth header, so
anyone holding it can post sensor data and open the push notification channel to
receive the user's notifications. Home Assistant classifies it the same way — its
`safe_registration` helper strips `webhook_id`, `secret` and `cloudhook_url`.

Installs created before this split carry the webhook id in settings.json.
`SessionStore` migrates it into the secret store on load and rewrites the file
without it. The migration is required: without it an existing install would look
unregistered, register again, and leave a duplicate device in Home Assistant.

### DeviceRegistrationRequest (sent to `/api/mobile_app/registrations`)

| Field               | Type   | Value                                          |
| ------------------- | ------ | ---------------------------------------------- |
| device_id           | string | ServerConfig.DeviceId                          |
| app_id              | string | `io.homeassistant.windows`                     |
| app_name            | string | `Home Assistant Windows Companion`             |
| app_version         | string | Assembly version                               |
| device_name         | string | Machine name                                   |
| manufacturer        | string | System manufacturer (or "PC")                  |
| model               | string | System model (or "Windows PC")                 |
| os_name             | string | `Windows`                                      |
| os_version          | string | Windows version string                         |
| supports_encryption | bool   | `false` (MVP)                                  |
| app_data            | object | `{ "push_websocket_channel": true }`           |

### DeviceRegistrationResponse

| Field         | Type    |
| ------------- | ------- |
| webhook_id    | string  |
| secret        | string? |
| cloudhook_url | string? |
| remote_ui_url | string? |

### Sensor

| Field               | Type    | Notes                                        |
| ------------------- | ------- | -------------------------------------------- |
| UniqueId            | string  | e.g. `battery_level`, `battery_state`        |
| Type                | string  | `sensor` or `binary_sensor`                  |
| Name                | string  | Display name                                 |
| State               | object  | number/string/bool                           |
| DeviceClass         | string? | e.g. `battery`, `enum`                        |
| Icon                | string? | `mdi:...`                                    |
| UnitOfMeasurement   | string? | e.g. `%`                                      |
| StateClass          | string? | e.g. `measurement`                            |
| EntityCategory      | string? | e.g. `diagnostic`                             |
| Attributes          | object? | extra key/values                              |

MVP sensor instances:

- **battery_level**: sensor, device_class `battery`, unit `%`, state_class
  `measurement`, entity_category `diagnostic`, state = integer 0–100.
- **battery_state**: sensor, device_class `enum`, icon `mdi:battery-charging`,
  entity_category `diagnostic`, state ∈ { `charging`, `discharging`, `full`,
  `not_charging`, `plugged in`, `unavailable` }.

### SystemStatus (from OS, transient)

| Field            | Type   | Source (GetSystemPowerStatus)                |
| ---------------- | ------ | -------------------------------------------- |
| HasBattery       | bool   | BatteryFlag != 128 (no system battery)       |
| BatteryPercent   | int    | BatteryLifePercent (255 = unknown)           |
| PowerState       | enum   | derived from ACLineStatus + BatteryFlag      |

### NotificationMessage (inbound → toast)

| Field   | Type    | Notes                                   |
| ------- | ------- | --------------------------------------- |
| Title   | string  | Toast title                             |
| Message | string  | Toast body                              |

### ConnectionState (enum)

`Disconnected` → `Connecting` → `Connected` → `Reconnecting` → back to `Connected`,
or `AuthError` (terminal until re-auth). Surfaced to UI + tray tooltip.

## Relationships

- One `ServerConfig` per app instance (MVP: single server).
- `ServerConfig` 1 → * `Sensor` (registered sensors tracked by UniqueId).
- Secrets keyed by `BaseUrl`/`DeviceId` in the Credential Locker.
