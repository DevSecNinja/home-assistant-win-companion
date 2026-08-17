# Research: HA Version Display

## Decision 1: Source of HA Core version

**Decision**: Use the existing `HaInstanceInfo.Version` property from the
`get_config` webhook response, which is already called during route probing.

**Rationale**: The `get_config` webhook already returns a `version` field containing
the HA Core version (e.g. `"2025.1.0"`). This is already deserialized into
`HaInstanceInfo.Version`. No additional network call is needed.

**Alternatives considered**:
- `GET /api/config` (requires access token, already available via `HaConfigInfo.Version`) —
  would work but `get_config` webhook is already called on every route probe, making it
  the zero-cost option.

## Decision 2: Source of HA OS version

**Decision**: Add an `OsVersion` field to `HaInstanceInfo`, mapping the
`ha_os_version` JSON property from the `get_config` webhook response.

**Rationale**: Home Assistant's `get_config` webhook returns `ha_os_version` only on
HA OS installations. On Container, Core, or Supervised installs without OS, the field
is absent or null. This maps directly to the spec requirement of "if available".

**Alternatives considered**:
- `GET /api/config` returns `os_version` which is the *host OS* version (e.g. Windows
  build), not the HA OS version. Confusingly named. The webhook's `ha_os_version` is
  the correct field for the HA OS layer.

## Decision 3: Where to store and expose version info

**Decision**: Store the version strings on a lightweight value exposed from
`AppController` (e.g. a `InstanceVersionSummary` string property) that the UI reads
during `RefreshPreferencesSummary()`. Clear it on disconnect.

**Rationale**: Matches the existing pattern: `AppController` already exposes
`BaseUrl`, `RouteSummary`, `State` etc. for the UI to read. No new pub/sub needed.

**Alternatives considered**:
- Persist to `ServerConfig` — unnecessary; version is ephemeral and changes between
  HA updates. Re-reading on each connect is correct.

## Decision 4: Display format

**Decision**: Show as a subtitle below the server hostname:
`"HA 2025.1.0"` or `"HA 2025.1.0 · OS 14.2"` when OS version available.

**Rationale**: Compact, scannable, follows the existing three-line card layout
(status → server → route). Can be added to the existing `SettingsServerText` or as
a new line. Adding a new `TextBlock` keeps concerns separate.

**Alternatives considered**:
- Append to existing server hostname text — clutters a single line.
- Show in tooltip — not discoverable enough for the user's intent.
