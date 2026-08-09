# Feature Specification: Network Identity Sensors

**Status**: Shipped

Add opt-in `ipv6_address` and `mac_address` sensors alongside the existing
connection type and IPv4 sensors, all derived from one shared adapter snapshot.

## Requirements

- All network identity sensors default off and are labelled privacy-sensitive.
- Connection type, IPv4, IPv6 and MAC are read from a single adapter snapshot per
  read so they always describe the same connection.
- Prefer the adapter carrying the active default route. When the route resolves to
  a VPN, tunnel or virtual adapter, the physical Ethernet/Wi-Fi adapter is reported
  instead; a tunnel is only reported when no physical LAN adapter is up.
- Route discovery connects a UDP socket, which resolves the route without
  transmitting a packet, for IPv4 and IPv6 independently.
- IPv6 prefers a globally routable address, falls back to a unique local address
  (`fc00::/7`), and prefers stable addresses over RFC 4941 temporary ones so the
  entity does not churn on rotation.
- IPv6 never reports link-local, loopback, multicast, unspecified, 6to4/Teredo
  tunnel or IPv4-mapped addresses, nor deprecated or duplicate-address-detection
  failures. The state is the bare address with no prefix length or zone index.
- MAC is formatted as uppercase colon-separated bytes (`AA:BB:CC:DD:EE:FF`) and
  only from a usable EUI-48; empty and all-zero hardware addresses are rejected.
- Report `Not Connected` rather than failing when no suitable address, adapter or
  hardware address exists.
- IPv4, IPv6 and MAC are diagnostic entities with string states and no attributes,
  so no alternate or historical adapter addresses are ever exposed.
- Observe network change events only while at least one sensor is enabled; no
  polling.

## Privacy

- Nothing is enumerated unless a sensor needing it is enabled: a snapshot taken for
  connection type alone carries no addresses and no hardware address.
- The local preview shows `Enable to read network identifiers` until the specific
  sensor is switched on. Enabling one identifier never reveals another.
- No address or hardware address is ever written to the log.
