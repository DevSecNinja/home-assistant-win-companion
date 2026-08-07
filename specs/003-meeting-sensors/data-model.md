# Data Model: Meeting Context Sensors

## NotificationState

Stable state reported by the presentation sensor:

- `Not Present`
- `Busy`
- `Full Screen`
- `Presentation`
- `Accepts Notifications`
- `Quiet Time`
- `App`
- `Unknown`

The source retains the previous mapped value so unchanged polls do not request a
push.

## CapabilityActivity

| Field | Type | Meaning |
| --- | --- | --- |
| Capability | string | `microphone` or `webcam` |
| IsActive | bool | At least one access-history entry has not stopped |

Activity is a derived snapshot only and is never persisted.

## AudioDeviceSnapshot

| Field | Type | Meaning |
| --- | --- | --- |
| DefaultOutputName | string? | Friendly name of the default render endpoint |
| EndpointNames | string collection | Active render/capture endpoint names |
| HeadsetConnected | bool | Any endpoint name is classified as headset-class |

The snapshot is cached in memory between polls. Only sensor preferences persist.

## Sensor definitions

| ID | Type | Default | Privacy |
| --- | --- | --- | --- |
| `user_notification_state` | sensor | On | Benign |
| `microphone` | binary_sensor | Off | Sensitive |
| `camera` | binary_sensor | Off | Sensitive |
| `audio_output` | sensor | Off | Sensitive |
| `headset_connected` | binary_sensor | Off | Sensitive |

All definitions use the existing `SensorDefinition`, `SensorPreferences`, and
registered-sensor retirement model.
