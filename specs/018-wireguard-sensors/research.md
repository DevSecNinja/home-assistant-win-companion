# Research: WireGuard Sensors

## Decision 1: Preserve a strict non-administrator boundary

**Decision**: Query local service and adapter state only. Never invoke `wg.exe`,
request elevation, change ACLs, or read WireGuard configuration.

**Rationale**: Testing on 2026-08-17 with WireGuard for Windows 1.1 under a confirmed
non-elevated token showed that `wg show interfaces` can list an interface, but
`wg show all dump` fails with `Permission denied` for the client's protected
`.conf.dpapi` configuration. Official enterprise documentation confirms runtime
configuration access requires Administrator/System for protected configurations.
Service status and adapter operational state remained readable without elevation.

**Alternatives considered**:

- `wg.exe`: rejected because useful runtime details cross the administrator boundary.
- Manager-service IPC: rejected because the client uses inherited private pipes and an
  internal Go `gob` protocol rather than a supported public endpoint.
- WireGuardNT/tunnel DLL embedding: rejected because it is intended for applications
  that own tunnel services and would add privileged lifecycle responsibilities.

**Evidence**:

- <https://github.com/WireGuard/wireguard-windows/blob/master/docs/enterprise.md>
- <https://github.com/WireGuard/wireguard-windows/blob/master/manager/service.go>
- <https://github.com/WireGuard/wireguard-windows/blob/master/manager/ipc_server.go>

## Decision 2: Identify official tunnels from exact Windows metadata

**Decision**: Enumerate Windows services and network interfaces. Treat services whose
names begin with `WireGuardTunnel$` as tunnel services and interfaces whose
description is exactly `WireGuard Tunnel` as official WireGuard adapters. Compare the
service suffix with the interface name internally to establish a corresponding pair.

**Rationale**: The official client documents the service naming contract. Local
inspection confirmed the exact adapter description, a running tunnel-service prefix,
and a readable operational state without revealing those names outside the probe.
Exact matching avoids classifying unrelated adapters merely containing "WireGuard".

**Alternatives considered**:

- Adapter-only detection: rejected because an adapter alone does not prove the
  documented tunnel service is running.
- Substring matching: rejected because it can misclassify third-party adapters.
- Publishing per-tunnel state: rejected because tunnel names can reveal sensitive
  network context and the requested first release is aggregate only.

## Decision 3: Use a three-state regular sensor

**Decision**: Publish a diagnostic `sensor` with lowercase `connected`,
`disconnected`, and `unavailable` states.

**Rationale**: A regular sensor can represent inspection failure explicitly. A binary
sensor naturally represents only on/off and risks turning an access or enumeration
failure into a false disconnected state.

**Alternatives considered**:

- Connectivity binary sensor: rejected because the third state is an acceptance
  requirement and should not be hidden in an attribute.
- Boolean state plus availability metadata: rejected because the current sensor
  contract has no separate availability field.

## Decision 4: Reuse event-driven monitoring and normal reads

**Decision**: Read status on the normal sensor cycle and subscribe to the existing
network-change watcher only while enabled. On an event, capture once, compare with the
last reported state, and request an immediate sync only for a genuine transition.
Collapse overlapping event bursts and discard callbacks after stop.

**Rationale**: Tunnel activation creates or removes a network adapter and therefore
produces network-change events. The existing network source already establishes the
required idempotent subscription and deduplication pattern. Local measurements found a
service query averaged about 3.2 ms; no continuous poller is warranted. A Release
measurement of 1,000 complete probe observations under the non-elevated application
token passed the projected 0.1% CPU budget at the normal one-minute sync interval.
The batch took about 72 seconds of wall time, confirming that enumeration should occur
only on normal reads and meaningful network changes rather than continuous polling.

**Alternatives considered**:

- Fixed-interval background polling: rejected because normal sensor sync already
  supplies eventual updates and disabled must mean zero work.
- Forward every Windows event: rejected because adapter changes arrive in bursts and
  would waste Home Assistant bandwidth.

The local Release probe also returned `connected` for the running test tunnel without
an elevation prompt, validating the service-to-adapter matching against the installed
official client.

## Decision 5: Use direct Service Control Manager interop

**Decision**: Use the Windows Service Control Manager API with query/enumeration access
and narrowly handle native access/enumeration failures as `unavailable`.

**Rationale**: Direct interop avoids adding `System.ServiceProcess.ServiceController`,
WMI, PowerShell, or another process. It is architecture-neutral for x64/ARM64 and
exposes only the minimum local metadata needed.

**Alternatives considered**:

- `ServiceController`: viable but adds a package for a single read-only query.
- WMI/CIM: rejected due to higher overhead and broader surface.
- PowerShell: rejected due to process startup cost and host availability concerns.
