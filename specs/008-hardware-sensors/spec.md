# Feature Specification: Hardware, Display, Environment and Storage Sensors

**Status**: Shipped

Add curated Windows hardware, display, environment and storage context to the
sensor catalog — the kind of system facts tools such as Fastfetch surface —
without invoking Fastfetch, parsing its output, or turning the companion into an
inventory agent.

## Sensors

| Id | Type | Default | Privacy | Source |
| --- | --- | --- | --- | --- |
| `host_model` | sensor | on | benign | SMBIOS manufacturer/product in the registry |
| `displays_count` | sensor | on | benign | `EnumDisplayMonitors` |
| `display_resolution` | sensor | off | sensitive | `EnumDisplaySettings`, `GetDpiForMonitor`, CCD paths |
| `windows_dark_mode` | binary_sensor | on | benign | `Themes\Personalize`, `SPI_GETHIGHCONTRAST` |
| `locale` | sensor | on | benign | `Control Panel\International\LocaleName` |
| `time_zone` | sensor | on | benign | `TimeZoneInfo.Local`, mapped to IANA |
| `disk_usage` | sensor | on | benign | `DriveInfo` for the system drive |
| `disk_free_space` | sensor | off | benign | `DriveInfo` for the system drive |
| `disk_used_space` | sensor | off | benign | `DriveInfo` for the system drive |

## Requirements

- Report the machine model without any unique hardware identifier. Serial numbers,
  service tags, asset tags, SKUs, UUIDs and BIOS identifiers are never read. OEM
  placeholder strings ("System manufacturer", "To Be Filled By O.E.M.") report
  `Unknown` rather than noise. The same values populate Home Assistant device
  registration, so the sensor discloses nothing new.
- Enumerate displays once per read and serve both display sensors from it. Report
  mode information only: never an EDID serial, monitor name or device path.
- Classify built-in versus external displays through the CCD path table's output
  technology, degrading to unclassified rather than guessing.
- Update display sensors from `DisplaySettingsChanged`, comparing the reading so a
  dock settling does not produce a burst of pushes.
- `display_resolution` is off by default because resolution, refresh rate and
  scaling together increase fingerprintability.
- Bound display output: at most four resolutions in the state (then "+N more") and
  eight in the attributes, so a many-output dock cannot approach Home Assistant's
  255-character limit.
- Follow Windows Personalization for dark mode, sampled at read time and pushed on
  `UserPreferenceChanged`. A high-contrast theme is reported through the `theme`,
  `system_theme` and `high_contrast` attributes rather than being flattened into a
  misleading dark/light value.
- Report `locale` as the user's **regional format** in BCP 47 (`nl-NL`), read live
  from the user's own setting so a running app does not serve a stale cached
  culture. Display language and region are attributes, not separate entities.
- Report `time_zone` preferring the IANA name Home Assistant uses, falling back to
  the Windows id. The CLDR mapping is region-canonical, so Amsterdam reports
  `Europe/Berlin`; offset and DST rules are identical.
- Report disk usage for the Windows system drive only. Never enumerate removable,
  network or additional fixed volumes. Inaccessible, BitLocker-locked, disconnected
  or nonsensical volumes report `unavailable`, never a negative or absurd value.
- Poll the volume every 10 minutes, not on the one-minute sync, and replace the
  published reading only when it moves by at least 0.5 percentage points or 1 GB,
  so Home Assistant's recorder is not filled with meaningless free-space drift.
- Use `data_size`/`GB` with `state_class: measurement` for byte sensors and `%` for
  usage; free and used space are off by default because they move constantly.
- Every new sensor is diagnostic, has a description, an icon, and a local preview.
- Disabled sensors register no hooks: display and locale event subscriptions and
  the disk poller only exist while one of their sensors is enabled.

## Focus / Do Not Disturb decision

No focus entity is added. `SHQueryUserNotificationState` reports presentation
mode, exclusive full-screen apps, the busy/app states, the lock screen and the
legacy quiet-time window that follows a new user's first sign-in. It does **not**
reflect the Windows 11 Focus / Do Not Disturb switch, which leaves the value at
`QUNS_ACCEPTS_NOTIFICATIONS`. Windows exposes no supported API for the current
focus state to an unpackaged desktop app; the known routes are undocumented WNF
state names and registry keys that shift between builds, which fails the
maintainability bar this repository sets.

The existing `user_notification_state` sensor is corrected instead: its
description states the exclusion, and it now exposes a derived
`suppresses_notifications` attribute plus an explicit
`includes_do_not_disturb: false`.

## Out of scope

- CPU/GPU temperature or utilization.
- Serial numbers, SMART data, full hardware inventory, per-process usage.
- Parsing or bundling Fastfetch; arbitrary PowerShell/WMI as a sensor framework.
- Automatically exposing every drive or per-monitor entity without user control.
  Additional fixed drives may become individually selectable later; the system
  drive is deliberately the only one today.
