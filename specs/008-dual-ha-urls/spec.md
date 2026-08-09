# Feature Specification: Internal and External Home Assistant URLs

**Status**: Shipped
**Supersedes**: [006-change-server-url](../006-change-server-url/spec.md)

Use one Home Assistant address by default. Users whose internal and external
addresses differ can explicitly enable route selection and failover without
re-registering the device or losing the refresh token, webhook, entities or history.

## Requirements

- Keep the signed-in address as the default single-URL configuration.
- Reveal internal/external addresses, trusted networks and connection modes only
  after the user opts into separate URLs.
- Offer five modes: Automatic, Prefer internal, Prefer external, Internal only,
  External only.
- Prove both addresses reach the same Home Assistant instance before saving.
- Select a route from the current network, never from the hostname.
- Fail over to the other address when the active one stops working.
- Show which address is in use and why, without revealing network identifiers.
- Migrate a single-URL install without interrupting the connection.
- Keep the refresh token, webhook id, device and registered sensors across a
  route switch.

## Configuration model

`settings.json` (`%LOCALAPPDATA%\HaCompanion\settings.json`):

| Field | Purpose |
| --- | --- |
| `BaseUrl` | The address in use right now. Kept in step with the active route. |
| `UseSeparateUrls` | Explicit opt-in to internal/external route selection; false by default. |
| `InternalUrl` | Address used on the user's own network. May be plain HTTP. |
| `ExternalUrl` | Address used from anywhere else. HTTPS only. |
| `ConnectionMode` | `Automatic`, `PreferInternal`, `PreferExternal`, `InternalOnly`, `ExternalOnly`. |
| `TrustedNetworks` | SSIDs, optional BSSIDs, and the wired/unknown-network switches. |
| `LastSuccessfulRoute` / `LastSuccessfulRouteAt` | The route that last carried a validated connection. |
| `InstanceDeviceId` | Home Assistant's device-registry id for this registration. |
| `RouteAssignmentPending` | Legacy compatibility field; cleared during migration. |

`BaseUrl` stays authoritative so anything that only needs "where is Home
Assistant right now" keeps working. Modes and routes serialize as strings, so the
file stays readable and stable across versions.

Secrets are unaffected: the refresh token, webhook id and cloudhook URL remain in
the Windows Credential Locker and are never written to `settings.json`.
`InstanceDeviceId` is not a credential - it names a device row inside the user's
own Home Assistant and grants nothing on its own.

## Same-instance validation

Two addresses may only be saved together once both prove they are the *same*
instance. Names and versions are useless for this: two unrelated servers share
them trivially. Instead the companion posts `{"type":"get_config"}` to the
existing `api/webhook/{webhook_id}` and reads `hass_device_id`, Home Assistant's
own device-registry id for this registration.

That single call answers the whole question:

| Answer | Meaning |
| --- | --- |
| `hass_device_id` present and equal | Same instance, same registration. |
| HTTP 200 with an empty body | Home Assistant does not know this webhook - a different instance. Home Assistant answers this way deliberately so webhook ids cannot be enumerated. |
| HTTP 410 | The registration was deleted there. |

Nothing in this path registers anything, so testing an address never produces a
second Mobile App device, and a route switch never orphans entity history.

## Credential-safety ordering

`HttpRouteProbe` validates in a fixed order and stops at the first failure:

1. **Transport rules.** External must be HTTPS; rejected before any request.
2. **Redirect resolution and guards.** No HTTPS to HTTP downgrade, no change of
   host.
3. **Unauthenticated identity check.** `GET {base}manifest.json`, which the Home
   Assistant frontend serves without credentials.
4. **Refresh token exchange** at `/auth/token`.
5. **Authenticated API check** at `GET /api/`.
6. **Webhook identity check** via `get_config`.

Steps 1-3 use no credentials at all. A captive portal, a hijacked DNS answer or
a plain wrong address therefore fails *before* the refresh token or the webhook
id is sent anywhere. Certificate validation is never relaxed; there is no bypass
switch. An internal HTTPS address with a private certificate must be trusted by
Windows.

An endpoint that accepts a TCP connection without speaking HTTP fails step 3 as
`NotHomeAssistant` rather than escaping as a transport error, and `RouteValidator`
refuses to save it. This carries forward the protection the single-URL "change
server URL" flow gained before it was replaced, without exposing the underlying
socket error to the user.

## Route selection

`RouteSelector` is pure: it takes the configuration and a `NetworkContext` and
returns an ordered candidate list. `NetworkContext` is local-only and is never
logged or sent to Home Assistant.

Trust classification:

| Situation | Trust |
| --- | --- |
| SSID in the trusted list (and BSSID, if required) | Trusted |
| Wired, with "trust wired networks" on | Trusted |
| Identifiable network that is not trusted | Untrusted |
| Wi-Fi whose name Windows withholds (Location denied) | Unidentifiable |
| VPN active on an unrecognized network | Unidentifiable |
| No trusted networks configured at all | Unidentifiable |
| No network | Offline |

Automatic mode then chooses:

| Trust | Candidates |
| --- | --- |
| Trusted | Internal, then External |
| Untrusted | External only - the internal address is **never** probed |
| Unidentifiable | External; Internal only as a fallback if explicitly opted in |
| Offline | Nothing |

The four explicit modes ignore trust entirely and use the order the user asked
for.

## Failover and flap protection

- `ConnectionManager` raises `RouteUnhealthy` after two consecutive sync failures
  or from the second WebSocket reconnect attempt.
- Network changes are debounced for 5 seconds, because a transition produces a
  burst of events and a captive portal answers before it lets anything through.
- A freshly activated route is protected by a 2 minute cooldown against network
  changes and periodic checks. A real `ConnectionFailed` bypasses the cooldown,
  so a genuinely broken route still fails over immediately.
- Unattended evaluations are floored at 30 seconds apart.
- There is no background polling of either server. Evaluation is event-driven.

A switch only happens after a candidate has *proved* it is usable and is the same
instance. The REST and WebSocket clients are then rebuilt on the new address,
which re-opens the push notification channel. The refresh token, webhook id,
device and registered sensors are untouched.

## Lifecycle serialization

Failover runs on its own schedule, so it can collide with whatever the user is
doing in the window. `ConnectionLifecycle` makes every change to the connection —
sign-in, resume, disconnect, remove server, settings changes and background route
switches — take an exclusive lease, so two of them can never interleave.

Ordering alone is not enough, because a route switch that merely *waits* would
still rebuild a connection the user has since ended. So a lease also carries:

- **A generation counter**, bumped by every user action. A route switch re-checks
  it after being let in and stands down if it moved.
- **A "connection wanted" flag**, cleared by disconnect and remove server. Nothing
  can bring the connection back until the user asks for it. `Reconfigure` leaves
  it alone, so saving settings while disconnected does not reconnect.
- **Pre-emption**: a user action cancels the transition in progress before queuing
  for the lease, so the UI never waits on a rebuild's network calls.

Route switches never queue. If a transition is already running the switch is
dropped, because whatever prompted it will prompt it again, and the transition in
progress may well have settled it.

`BuildAndStartAsync` additionally tears down any live connection before building,
so no path can leave two `ConnectionManager`s — and two WebSocket sessions, sensor
sync loops and notification subscriptions — running invisibly alongside each other.

## Migration

An install saved by a single-URL version keeps its `BaseUrl`, remains connected,
and stays in the default single-URL mode. No classification prompt is shown.

Configurations from the first dual-URL release that contain both route addresses
are migrated with `UseSeparateUrls` enabled. A configuration containing only one
route-specific address is collapsed back to `BaseUrl`, because one address does
not need network classification or failover.

## Privacy

- SSIDs and BSSIDs used for routing are stored locally and are never sent to Home
  Assistant, and never written to the log. They are independent of the opt-in
  `connectivity_ssid` / `connectivity_bssid` sensors from
  [007-wifi-identifiers](../007-wifi-identifiers/spec.md), so routing works with
  those sensors disabled.
- BSSID matching is off by default: mesh networks present many BSSIDs under one
  SSID, and a BSSID is precise location data. An access point address is only
  recorded while access-point matching is switched on, and is discarded when it is
  switched off or the last trusted network is removed.
- The status view names the route ("Internal" / "External"), never the network.
- Home Assistant's `cloudhook_url` is never offered as a suggested address; it
  embeds the webhook capability secret.

## Deliberate limitations

- **Manifest check behind an authenticating proxy.** A reverse proxy that
  requires its own sign-in for `manifest.json` will fail step 3 and the address
  will read as "not Home Assistant". Refusing is the safe direction: the
  alternative is sending credentials to an unverified host.
- **No parallel connections.** Only one address is connected at a time. Failover
  costs a reconnect rather than being instant.
- **Wired networks cannot be told apart.** Windows exposes no SSID for Ethernet,
  so "trust wired networks" is all-or-nothing.
- **Wi-Fi trust needs the Location permission.** Without it Windows withholds the
  SSID, every Wi-Fi network is unidentifiable, and Automatic mode uses the
  external address. The panel links to the Windows Location settings.
- **A single configured address is always used**, regardless of network. The
  alternative - refusing to connect - would leave the app permanently offline
  rather than merely cautious.
- **`hass_device_id` on very old Home Assistant versions** has not been verified.
  An instance that omits it reads as "different instance" and is refused.
