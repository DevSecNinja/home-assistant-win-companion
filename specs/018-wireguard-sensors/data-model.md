# Data Model: WireGuard Sensors

## WireGuard Status

Represents the privacy-safe result exposed outside the Windows probe.

| Value | Meaning |
|-------|---------|
| `Connected` | At least one running official tunnel service has a matching operational official adapter. |
| `Disconnected` | WireGuard is detectable, but no running service and matching operational adapter pair exists. |
| `Unavailable` | WireGuard is not detectable or required Windows state cannot be inspected safely. |

### Transition rules

- `Unavailable` → `Disconnected`: WireGuard becomes detectable without an operational tunnel.
- `Unavailable` → `Connected`: WireGuard becomes detectable with a running matched tunnel.
- `Disconnected` → `Connected`: a matched tunnel service and adapter become operational.
- `Connected` → `Disconnected`: the last matched service stops or adapter ceases to be operational.
- Any state → `Unavailable`: service or adapter inspection fails.

## Private Windows Observation

Exists only within the Windows probe and is never persisted, logged, attached to a
sensor, or returned by its public contract.

| Field | Constraint |
|-------|------------|
| WireGuard installation detectable | True when the manager service or any official tunnel service exists. |
| Running tunnel names | Service-name suffixes held only for the duration of matching. |
| Operational adapter names | Names held only for the duration of matching; description must exactly equal `WireGuard Tunnel`. |

### Validation rules

- Name matching is ordinal and case-insensitive.
- Only running tunnel services participate in a connected match.
- Only operational adapters with the exact official description participate.
- Empty or malformed service suffixes do not participate.
- Any incomplete native enumeration is an inspection failure, not a disconnected result.

## Published Sensor

| Field | Value |
|-------|-------|
| Unique ID | `wireguard_status` |
| Name | `WireGuard Status` |
| Type | `sensor` |
| State | lowercase mapped WireGuard status |
| Entity category | `diagnostic` |
| Icon | `mdi:vpn` |
| Privacy | sensitive |
| Enabled by default | false |

No attributes are published.
