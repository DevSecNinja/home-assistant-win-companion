# Data Model: HA Version Display

## Modified Entity: HaInstanceInfo

Existing model at `src/WindowsCompanion.Core/Models/HaInstanceInfo.cs`.

| Field | Type | JSON Property | Source | Notes |
|-------|------|---------------|--------|-------|
| DeviceId | string? | `hass_device_id` | Existing | Identity check |
| Version | string? | `version` | Existing | HA Core version |
| **OsVersion** | **string?** | **`ha_os_version`** | **New** | HA OS version, null on non-OS installs |
| RemoteUiUrl | string? | `remote_ui_url` | Existing | Cloud remote UI |
| CloudhookUrl | string? | `cloudhook_url` | Existing | Cloud webhook |

## New Value: InstanceVersionSummary (AppController)

A computed `string?` property on `AppController`:

- `null` when disconnected or version not yet known
- `"HA {Version}"` when only Core version available
- `"HA {Version} · OS {OsVersion}"` when both available

Cleared on disconnect/teardown; updated after successful route probe.

## State Transitions

```
Disconnected  → (version = null)
Connecting    → (version = null)
Connected     → (version = formatted from HaInstanceInfo)
Reconnecting  → (previous version retained until new probe)
Disconnected  → (version = null)
```
