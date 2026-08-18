# Sensor Contract: WireGuard Status

## Definition

- **Unique ID**: `wireguard_status`
- **Display name**: `WireGuard Status`
- **Home Assistant type**: `sensor`
- **Entity category**: `diagnostic`
- **Icon**: `mdi:vpn`
- **Enabled by default**: no
- **Privacy classification**: sensitive

## State contract

| State | Contract |
|-------|----------|
| `connected` | At least one official WireGuard tunnel service is running with its matching official adapter operational. |
| `disconnected` | WireGuard is detected locally, but no service/adapter pair satisfies the connected contract. |
| `unavailable` | WireGuard is absent or local inspection cannot complete safely. |

## Privacy contract

The sensor has no attributes and never publishes or logs tunnel names, configuration,
keys, endpoints, assigned addresses, interface identifiers, or traffic statistics.
Its disabled preview is a placeholder and performs no WireGuard observation.

## Behavioral boundary

`connected` describes local tunnel readiness only. It does not assert a recent
WireGuard handshake, peer reachability, internet access, or VPN endpoint health.

## Resource contract

The source performs no observation and holds no event subscription while disabled.
While enabled, it reads on the normal sensor cycle and captures network-change events
only to request a sync when the published state genuinely changes.
