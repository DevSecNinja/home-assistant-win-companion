# Phase 0 Research: Selectable Sensor Catalog

## Decision: Mirror the official companion app's sensor identifiers

**Rationale**: The macOS/iOS companion (`home-assistant/iOS`) already defines a
sensor vocabulary that Home Assistant users, blueprints and automations expect.
Reusing the same `unique_id` values means entities are named familiarly and
community automations written for the official app work against a Windows PC.

**Canonical ids** (from `Sources/Shared/API/Webhook/WebhookSensorId.swift`):

| Sensor | `unique_id` | Type | In scope now |
| --- | --- | --- | --- |
| Active | `active` | binary_sensor | Yes |
| Connection Type | `connectivity_connection_type` | sensor | Yes |
| SSID / BSSID | `connectivity_ssid` / `connectivity_bssid` | sensor | **Dropped** (see below) |
| Last Update Trigger | `last_update_trigger` | sensor | Yes |
| Battery Level / State | `battery_level` / `battery_state` | sensor | Already shipped |
| Microphone / Camera in use | `microphone` / `camera` | binary_sensor | Deferred |
| Frontmost app | `frontmost_app` | sensor | Deferred |
| Storage / displays / audio output | `storage`, `displays_count`, … | sensor | Deferred |

Windows-specific additions (no macOS equivalent) use new ids and are namespaced
plainly: `screen_locked`, `ip_address`, `os_version`, `last_boot`.

## Decision: Model "Active" exactly like macOS, with Windows event sources

**Rationale**: macOS derives a single `active` binary sensor from a set of boolean
sub-states and exposes each as an attribute
(`Sources/Shared/Environment/ActiveStateManager.swift`):

```text
isActive = !idle && !screensaver && !locked && !sleeping
           && !screenOff && !fastUserSwitched && !terminating
```

Every sub-state has a direct Windows equivalent:

| macOS notification | Windows source |
| --- | --- |
| `com.apple.screenIsLocked` / `Unlocked` | `SystemEvents.SessionSwitch` → `SessionLock` / `SessionUnlock` |
| `NSWorkspaceSessionDidResignActive` / `BecomeActive` | `SessionSwitch` → `ConsoleDisconnect` / `ConsoleConnect` |
| `NSWorkspaceWillSleep` / `DidWake` | `SystemEvents.PowerModeChanged` → `Suspend` / `Resume` |
| `NSWorkspaceScreensDidSleep` / `DidWake` | `RegisterPowerSettingNotification(GUID_CONSOLE_DISPLAY_STATE)` |
| `com.apple.screensaver.didstart` / `didstop` | `SystemParametersInfo(SPI_GETSCREENSAVERRUNNING)` |
| idle timer + `idleTime()` | `GetLastInputInfo()` |

**Idle handling**: macOS polls every 5s and compares against a user-configurable
`minimumIdleTime` (default 5 minutes). We do the same. `GetLastInputInfo` is a
trivial syscall, and — critically — polling only computes state locally; a webhook
push happens **only when the derived state actually changes**. So the steady-state
network cost stays at the existing one batch per 60s.

**Alternatives considered**: a raw "seconds idle" numeric sensor (updates constantly,
defeats the point) — rejected in favour of the boolean + threshold.

## Decision: Per-sensor enablement, honoured on both sides

**Rationale**: Users should choose what leaves their machine. Two mechanisms combine:

1. **Local**: a disabled sensor is not collected and not sent. The OS hook backing it
   is not even registered — the macOS app does exactly this
   (`ConnectivitySensorUpdateSignaler` stops observing when all related sensors are
   disabled). This is what makes "off" mean *zero* cost, not just "not displayed".
2. **Home Assistant**: `mobile_app`'s `update_sensor_states` accepts a per-sensor
   `disabled: true` flag (verified in `homeassistant/components/mobile_app/webhook.py`,
   which sets `disabled_by = RegistryEntryDisabler.INTEGRATION`). Sending it means
   toggling a sensor off actually disables the entity in HA rather than leaving a
   stale entity stuck at its last value.

## Decision: Privacy defaults

**Rationale**: The repository is intended to become public and these sensors describe
a personal machine. Sensors are classified and defaulted accordingly:

| Sensor | Default | Why |
| --- | --- | --- |
| `active`, `screen_locked` | On | Presence/automation value, reveals no content |
| `battery_level`, `battery_state` | On | Already shipped |
| `last_update_trigger` | On | Diagnostic only |
| `os_version` | On | Low sensitivity, diagnostic |
| `ip_address` | **Off** | Network topology; low risk but not needed by default |
| `connectivity_ssid` / `connectivity_bssid` | **Off** | SSID/BSSID can infer physical location |
| `frontmost_app` (deferred) | **Off** | Window titles leak document names, URLs, private content |

Privacy-sensitive values must never be written to logs (the existing `Redactor`
policy is extended to cover SSID/BSSID and window titles).

## Constraint: Home Assistant state length

`MAX_LENGTH_STATE_STATE = 255` (`homeassistant/const.py`). Any string sensor state
must be truncated to 255 characters. This is not a problem for the sensors in this
phase but is a hard requirement for the deferred `frontmost_app` sensor.

## Constraint: `update_registration` and notify entities

Already documented in feature 001: adding sensors does not require re-registration,
but changing `app_data` does, and `update_registration` requires `app_version`,
`device_name`, `manufacturer` and `model` together.


## Decision: Disable sensors via `register_sensor`, never `update_sensor_states`

**Rationale**: Home Assistant only reads the `disabled` flag in the re-registration
branch of `webhook_register_sensor` (`homeassistant/components/mobile_app/webhook.py`),
where it sets `disabled_by = RegistryEntryDisabler.INTEGRATION`. The
`update_sensor_states` handler ignores it entirely, so a disable sent in a state
batch is silently dropped - the entity keeps showing its last value.

Two consequences:

1. Disabling a sensor must be sent as a `register_sensor` call carrying
   `disabled: true` (which also needs `name` and `type`, so the app retains those
   for everything it has registered).
2. The flag must always be sent **explicitly**. The handler treats a missing
   `disabled` as "no change", so a sensor the user switches back on would stay
   disabled in Home Assistant forever unless we send `disabled: false`.

Note that disabled is not deleted: Home Assistant keeps the registry entry and
shows it greyed out under the device. Removing it entirely is a manual action.

## Decision: Wi-Fi SSID and BSSID are out of scope

**Rationale**: Windows gates Wi-Fi network identifiers behind the Location
capability, because an SSID/BSSID can be used to locate a machine.
`WlanQueryInterface(wlan_intf_opcode_current_connection)` returns
`ERROR_ACCESS_DENIED` (5) for an unpackaged desktop app that has not been granted
location access, and there is no clean way for such an app to request it. The
alternatives - hand-marshalled `wlanapi` interop that still fails, or scraping
`netsh wlan show interfaces` output (locale-dependent and brittle) - are not worth
the fragility.

Dropped rather than worked around, and tracked in issue #2 instead.

## Decision: Health is judged on reporting, not on the socket

**Rationale**: The WebSocket can look perfectly healthy while sensor pushes fail,
so "connected" is not a sufficient signal. The companion reports itself healthy
only when it is connected, has no consecutive sync failures, and completed a
successful push within 2.5x the sync interval. The verdict is shown in the app and
in the tray tooltip, and a rolling file log
(`%LOCALAPPDATA%\HaCompanion\logs\`) is user-openable for troubleshooting.

**Note on apparent staleness**: Home Assistant only advances an entity's
`last_updated` when the state or attributes actually change - a battery sitting at
the same percentage will look "stale" for many minutes even though the companion is
reporting on schedule. `last_reported` is the attribute that tracks every report.
This is why the companion surfaces its own last-push time rather than relying on
what Home Assistant displays.

## Decision: Last update is shown in-app; the sensor reports the *reason*

**Rationale**: Home Assistant already records when an entity last updated, so a
timestamp sensor would duplicate it. The official apps instead expose
`last_update_trigger`, whose state is *why* the app reported ("registration", or a
trigger string). The companion mirrors that, and separately shows the actual last
push time in its own status view where it is genuinely not otherwise available.

