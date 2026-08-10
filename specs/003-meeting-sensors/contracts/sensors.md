# Sensor Contract: Meeting Context

The sensors use the existing `register_sensor` and `update_sensor_states` webhook
contracts.

| ID | Name | Type | State |
| --- | --- | --- | --- |
| `user_notification_state` | Notification State | `sensor` | Stable display string |
| `microphone` | Microphone In Use | `binary_sensor` | Boolean |
| `camera` | Camera In Use | `binary_sensor` | Boolean |
| `audio_output` | Audio Output | `sensor` | Friendly endpoint name or `Not Connected` |
| `headset_connected` | Headset Connected | `binary_sensor` | Boolean |

## Enablement

- Notification State is enabled by default.
- All other sensors default off.
- A source polls only while at least one definition it owns is enabled.
- Disabling and re-enabling follows the existing explicit `register_sensor`
  `disabled` flag behavior.

## Preview

The Sensors page requests a local preview for every definition. Preview reads are
never passed to the Home Assistant client and do not start a source's background
poller.

## Update behavior

A poll updates the cached snapshot every second. The source requests an
immediate sensor sync only when the snapshot changes. The existing periodic sync
still reports enabled readings as a resilience fallback.
