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
- Select a route from user-configured local network rules, never by guessing from
  the Home Assistant hostname.
- Fail over to the other address when the active one stops working.
- Show which address is in use and why, without revealing network identifiers.
- Migrate a single-URL install without interrupting the connection.
- Keep the refresh token, webhook id, device and registered sensors across a
  route switch.

## Configuration model

`settings.json` (`%LOCALAPPDATA%\WindowsCompanion\settings.json`):

| Field | Purpose |
| --- | --- |
| `BaseUrl` | The address in use right now. Kept in step with the active route. |
| `UseSeparateUrls` | Explicit opt-in to internal/external route selection; false by default. |
| `InternalUrl` | Address reachable on the local networks the user configures. HTTPS is recommended. |
| `ExternalUrl` | Address reachable everywhere else. HTTPS only. |
| `ConnectionMode` | `Automatic`, `PreferInternal`, `PreferExternal`, `InternalOnly`, `ExternalOnly`. |
| `TrustedNetworks` | Canonical IPv4/IPv6 CIDRs, SSIDs, optional BSSIDs, and the wired/unknown-network switches. |
| `LastSuccessfulRoute` / `LastSuccessfulRouteAt` | The route that last carried a validated connection. |
| `InstanceDeviceId` | Home Assistant's device-registry id for this registration. |
| `RouteAssignmentPending` | Legacy compatibility field; cleared during migration. |

`BaseUrl` stays authoritative so anything that only needs "where is Home
Assistant right now" keeps working. Modes and routes serialize as strings, so the
file stays readable and stable across versions.

`TrustedNetworks.Cidrs` is an empty list when absent, so existing installs retain
their SSID/wired behavior. Saving advanced routing validates and canonicalizes the
list without changing the webhook id, device id, refresh token, registration, or
registered sensors.

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

## Address meaning and transport

The labels describe reachability, not a security boundary derived from a server
request's source IP:

- The **internal address** is reachable on one or more networks the user
  deliberately configures below.
- The **external address** is the address to try everywhere else.

HTTPS is recommended for both. Plain HTTP is accepted only for the internal
address, with an explicit warning: the user is deliberately accepting that
another party on the matched local network could read or alter Home Assistant
traffic. The default and placeholder examples use HTTPS and do not steer users
toward a raw private IP over HTTP.

## Trusted CIDRs

Users can enter multiple IPv4 and IPv6 CIDR blocks, one per line. A block is
valid only when:

- it contains exactly one IPv4 or IPv6 address and prefix;
- its prefix is `0..32` for IPv4 or `0..128` for IPv6;
- the address is the network address (all host bits are zero);
- it has no IPv6 zone id; and
- it neither duplicates nor overlaps another configured block of the same
  address family.

Accepted values are persisted in canonical form. Errors identify the entry and
show a corrected network address when host bits are set. Invalid persisted values
fail closed and never match.

For routing, Windows enumerates every active non-loopback, non-tunnel, non-virtual
Ethernet or Wi-Fi interface. If any IPv4 or IPv6 address on any such interface is
inside a configured block, the network is trusted. Addresses and configured
blocks remain local-only and are never logged or sent to Home Assistant.

## Route selection

`RouteSelector` is pure: it takes the configuration and a `NetworkContext` and
returns an ordered candidate list. `NetworkContext` is local-only and is never
logged or sent to Home Assistant.

Trust classification:

| Situation | Trust |
| --- | --- |
| Any active Ethernet/Wi-Fi address inside a configured IPv4/IPv6 CIDR | Trusted |
| SSID in the trusted list (and BSSID, if required) | Trusted |
| Wired, with "trust wired networks" on | Trusted |
| Identifiable network that is not trusted | Untrusted |
| Wi-Fi whose name Windows withholds, with no valid CIDRs configured or no addresses available for comparison | Unidentifiable |
| Valid CIDRs configured and addresses available, but no CIDR/SSID/wired rule matches | Untrusted |
| VPN active on an unrecognized network | Unidentifiable |
| No trusted networks configured at all | Unidentifiable |
| No network | Offline |

Automatic mode then chooses:

| Trust | Candidates |
| --- | --- |
| Trusted | Internal, then External if the internal address cannot connect |
| Untrusted | External only - the internal address is **never** probed |
| Unidentifiable | External; Internal only if the external address cannot connect and unknown-network fallback is explicitly enabled |
| Offline | Nothing |

The four explicit modes ignore trust entirely and use the order the user asked
for. "Fallback" always means the next configured address is tried only after the
first one cannot establish a validated connection; it does not mean both
addresses are connected simultaneously.

## Failover and flap protection

- `ConnectionManager` raises `RouteUnhealthy` after two consecutive sync failures
  or from the second WebSocket reconnect attempt. Each source raises once per
  outage rather than on every later retry, bounding route probes when neither
  address is reachable.
- Network changes are debounced for 5 seconds, because a transition produces a
  burst of events and a captive portal answers before it lets anything through.
  Snapshots with the same routing profile coalesce even when Windows supplies new
  adapter-list instances.
- A freshly activated route is protected by a 2 minute cooldown against network
  changes and periodic checks. A real `ConnectionFailed` bypasses the cooldown,
  so a genuinely broken route still fails over immediately.
- Unattended evaluations are floored at 30 seconds apart.
- There is no background polling of either server. Evaluation is event-driven.
- A meaningful online network change or explicit refresh can bypass one pending
  connection delay after route evaluation. Duplicate events cannot accumulate
  bypasses or create another lifecycle transition.

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
Each manager itself owns exactly one WebSocket attempt loop and one serialized
sensor loop. Early socket closes continue the bounded backoff until a connection
has remained authenticated for 30 seconds; teardown cancels the attempt, delay and
coalesced signals before the lifecycle lease is released.

## Migration

An install saved by a single-URL version keeps its `BaseUrl`, remains connected,
and stays in the default single-URL mode. No classification prompt is shown.

Configurations from the first dual-URL release that contain both route addresses
are migrated with `UseSeparateUrls` enabled. A configuration containing only one
route-specific address is collapsed back to `BaseUrl`, because one address does
not need network classification or failover.

Configurations written before CIDR support deserialize with an empty CIDR list.
Existing SSID, BSSID, wired-network and unknown-network choices are preserved, as
are all registration and secret fields, so upgrading never causes a second device
registration.

## Privacy

- CIDRs, connected interface addresses, SSIDs and BSSIDs used for routing stay
  local and are never sent to Home Assistant or written to the log. They are
  independent of the opt-in
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
- **Broad wired trust is still available.** "Trust wired networks" remains an
  all-or-nothing compatibility option; CIDRs are the precise alternative.
- **Wi-Fi-name trust needs the Location permission.** Without it Windows withholds
  the SSID. CIDR matching still works from active interface addresses; if none are
  available, Automatic mode uses the external address. The panel links to the
  Windows Location settings.
- **A single configured address is always used**, regardless of network. The
  alternative - refusing to connect - would leave the app permanently offline
  rather than merely cautious.
- **`hass_device_id` on very old Home Assistant versions** has not been verified.
  An instance that omits it reads as "different instance" and is refused.
