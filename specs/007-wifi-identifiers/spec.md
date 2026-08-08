# Feature Specification: Wi-Fi Identifiers

**Status**: Shipped

Add opt-in `connectivity_ssid` and `connectivity_bssid` sensors through Windows'
native WLAN API.

## Requirements

- Both sensors default off and are labelled as location-revealing.
- Query from the companion process, not a PowerShell test host.
- Report `Not Connected` when no Wi-Fi connection exists.
- Report `Location permission required` when Windows denies connection attributes.
- Provide a direct action to open Windows Location settings.
- Observe network changes only while either sensor is enabled.
