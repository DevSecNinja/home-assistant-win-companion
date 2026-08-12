# Protocol and platform notes

These implementation notes capture Home Assistant and Windows behavior that is
easy to misinterpret. They supplement the formal contracts and feature
specifications under [`specs/`](../specs/).

## Home Assistant

### OAuth loopback ports are part of the client identity

Home Assistant requires the refresh grant's `client_id` to match the authorization
request. The companion's `client_id` is its loopback redirect URL, so changing an
ephemeral port between authorization and refresh breaks token renewal.

### Redirects can change HTTP methods

An HTTP URL that redirects can turn `POST /auth/token` into a `GET`, producing
`405 Method Not Allowed`. External routes therefore require HTTPS, and route
validation rejects unsafe redirects before sending credentials.

### Notifications use the local push channel

Home Assistant does not emit `persistent_notification` over the event bus.
Registration sets `app_data.push_websocket_channel = true`, making the PC a notify
target. Notifications arrive over
`mobile_app/push_notification_channel` and must be acknowledged within 10 seconds.

### Disabled sensors remain registered

Home Assistant ignores the `disabled` flag in ordinary state updates. Enable,
disable, and retirement flows use `register_sensor`. A disabled entity remains in
the entity registry and is normally hidden from standard pickers. Removing it
entirely requires deleting the Mobile App device, which invalidates registration.

### Webhook responses identify stale registrations

An unknown webhook ID receives HTTP 200 with an empty body so IDs cannot be
enumerated. A deleted registration receives HTTP 410. The companion treats both
as evidence that the instance does not host the saved registration.

The `get_config` webhook's `hass_device_id` proves that internal and external URLs
refer to the same Home Assistant device registration. Names and versions are not
unique enough for that check.

## Windows

### Do Not Disturb is not readable

`SHQueryUserNotificationState` reports presentation mode, exclusive full-screen
applications, the lock screen, and the legacy quiet-time window. It does not
report the Windows 11 Focus or Do Not Disturb switch, and Windows exposes no
supported alternative.

### Disk reporting is deliberately narrow

Disk sensors read only the Windows system drive every 10 minutes and publish a new
value after a change of at least 0.5 percentage points or 1 GB. Removable, network,
and BitLocker-locked volumes are not enumerated.

### Hardware identifiers are excluded

The Model sensor reads the SMBIOS manufacturer and product name only. It does not
read serial numbers, service tags, SKUs, UUIDs, or BIOS identifiers. Display
sensors report modes without EDID serials, monitor names, or device paths.

### Locale and time-zone naming

The `locale` sensor reports the regional format because it determines date and
number presentation. Display language and country are attributes. Windows time
zones are mapped to CLDR-canonical IANA names, so equivalent zones can use a
regional canonical name.

### Lifecycle delivery is best effort

Windows can terminate the process before shutdown, sign-out, or suspend work
finishes. Delivery uses bounded final attempts, local journaling, and recovery
after reconnection. The companion never blocks or vetoes a Windows lifecycle
transition.

See [Windows lifecycle signals](windows-lifecycle-signals.md) for the complete
behavior and limitations.
