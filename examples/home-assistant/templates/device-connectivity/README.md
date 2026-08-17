# Template entities

Template entities derive new Home Assistant state from sensors that Windows
Companion already reports. They do not add client traffic.

## Windows PC connectivity

[`template.yaml`](template.yaml) creates a binary sensor
with the `connectivity` device class. It reports connected while Home Assistant
has received a recent report from a selected Windows Companion sensor and
disconnected after reports stop.

### Prerequisites

- Home Assistant 2024.4 or newer, with `State.last_reported` support.
- Windows Companion connected with at least one sensor enabled.
- Access to edit Home Assistant configuration or create a Template helper.

The device must have at least one enabled sensor. Sensor values do not need to
change: `last_reported` advances whenever Home Assistant receives a report.

### Install with YAML

1. Copy the contents of
   [`template.yaml`](template.yaml) into
   `configuration.yaml`.
2. If configuration already contains a `template:` section, merge the
   `binary_sensor` entry into that section instead of adding a second
   top-level `template:` key.
3. Replace `Replace with your device name` with this PC's exact Home Assistant
   device name, including capitalization and spaces. The name must be unique in
   Home Assistant; rename or remove duplicate device registrations first.
4. Change `windows_pc_connectivity` if another PC already uses that unique ID.
5. Check the configuration, then restart Home Assistant or reload Template
   entities.

### Install with the Template helper

Open the
[Template helper](https://my.home-assistant.io/redirect/config_flow_start?domain=template),
choose **Template a binary sensor**, and transfer the name, device class, and
state template from the YAML example. The link opens the helper flow but Home
Assistant does not prefill these values.

### Customize

| Value | Default | Purpose |
| --- | --- | --- |
| Device name | `Replace with your device name` | The PC's exact Home Assistant device name |
| `offline_after_seconds` | `180` | Maximum report age before the PC is disconnected |
| Name | `Windows PC connectivity` | Display name in Home Assistant |
| Unique ID | `windows_pc_connectivity` | Stable identifier; make it unique for each PC |

Windows Companion normally synchronizes once per minute. The three-minute
default tolerates a delayed report without making normal operation flap. Keep
the timeout comfortably longer than the companion's reporting interval.

### Expected behavior

- Regular reports to any enabled sensor on the device keep the binary sensor
  connected, even when sensor values do not change.
- An unknown device name or a device without reported sensors produces
  disconnected.
- Abrupt shutdown and network loss become visible after the configured timeout.
- A new report returns the binary sensor to connected.

Templates using `now()` are evaluated once per minute. The status can therefore
change up to one additional minute after the timeout or after reporting resumes.
A graceful shutdown message is not required and could not be guaranteed during
power or network loss.

Home Assistant resolves the device name exactly. If the device is renamed, update
`companion_device` in the template. Custom device names take precedence over
default names. Duplicate names can resolve to the wrong device.

### Remove

Remove the copied template entry from Home Assistant configuration, or delete the
Template helper under **Settings > Devices & services > Helpers**. Then reload
Template entities or restart Home Assistant.
