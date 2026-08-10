# Feature Specification: LAN/WLAN Identity, Gateway and DNS Sensors

**Status**: Shipped

Add opt-in `lan_mac_address`, `wlan_mac_address`, `gateway_address` and
`dns_servers` sensors alongside the existing network identity sensors, plus
`connectivity_wifi_security` and `connectivity_wifi_random_mac` next to Wi-Fi
SSID/BSSID.

## Requirements

- All new sensors default off and are labelled privacy-sensitive, consistent with
  the existing network identifier sensors.
- `lan_mac_address` and `wlan_mac_address` report the hardware address of the
  physical Ethernet or Wi-Fi adapter respectively, independent of which adapter is
  currently carrying the active route: a docked laptop reports both its Ethernet
  and its Wi-Fi hardware address. An adapter that is up is preferred over one that
  is merely present, and `Not Connected` is reported when no matching physical
  adapter exists.
- `gateway_address` and `dns_servers` are read from the same adapter snapshot as
  the existing IPv4/IPv6/MAC sensors, so they always describe the active
  connection. `dns_servers` joins multiple resolvers with `, `.
- `connectivity_wifi_security` reports the current Wi-Fi connection's security
  type (for example `WPA2-Personal`, `WPA3-Personal`, `Open`), derived from the
  native `DOT11_AUTH_ALGORITHM` Windows reports for the connection, so a legacy or
  open network stands out.
- `connectivity_wifi_random_mac` reports the randomized hardware address in use
  for the current Wi-Fi connection when Windows' per-network MAC randomization is
  switched on (read from the connected profile's XML), `Not randomized` when it is
  off, and `Not Connected`/`Unavailable` otherwise.
- All six sensors share the existing `Not Connected`, `Location permission
  required` and `Unavailable` states used by the other connectivity sensors.

## Privacy

- Nothing is enumerated unless a sensor needing it is enabled, following the same
  capture-scope rule as the existing network identity sensors.
- No hardware address, gateway, DNS server or security type is ever written to
  the log.
