# Test strategy

## Decision

The project uses three complementary test layers:

1. **Unit and component tests** run on every change. They cover Core decisions,
   state transitions, protocol parsing, storage, and failure handling.
2. **Golden payload tests** run with the unit suite. They compare complete outbound
   Home Assistant requests with payloads verified against the real integration.
3. **Manual pre-release smoke tests** cover Windows UI, OAuth, native notifications,
   and live Home Assistant behavior.

## Real Home Assistant contract tests

A containerized Home Assistant test is valuable but is not currently a per-PR gate.
Bootstrapping an authenticated `mobile_app` registration, maintaining compatibility
with Home Assistant releases, and exercising the push WebSocket channel add enough
cost and secret-management complexity that the suite would be fragile today.

Add it later as a scheduled and manually dispatched workflow when:

- GitHub-hosted CI is operational.
- A repeatable Home Assistant container fixture and token bootstrap exist.
- The job can track at least the latest stable Home Assistant version separately
  from the fast merge gate.

That suite should verify registration, `update_registration`, sensor registration,
sensor rejection bodies, disabled entity behavior, and the local push channel.

## Golden payload maintenance

Golden JSON files live under `tests/HaCompanion.Core.Tests/Golden/`. Change them only
when verified Home Assistant behavior changes. A code change that alters a payload
must explain the upstream contract evidence in its pull request and update the
relevant specification contract.

Golden tests deliberately compare full JSON structures, not selected fields. This
protects against adding metadata to `update_sensor_states`, omitting required
registration fields, or moving `disabled` to the wrong request type.

## Reliability and resource-usage tests

Sensor sources hold OS hooks and read privileged state, so their lifecycle is
covered by deterministic Core tests rather than wall-clock benchmarks. They assert
behavior that would otherwise regress silently: repeated start/stop/restart holds
exactly one subscription, a stopped source is never called back, a disabled sensor
performs zero enumeration and zero route probes, one grouped refresh takes one
snapshot, concurrent change notifications are serialised and coalesced, a burst of
identical changes publishes once, and probe handles are released on success,
failure and cancellation alike.

Keeping this testable is why platform sources delegate their lifecycle to a Core
coordinator and their OS hook to a small watcher interface. Stress tests use fakes
and counters and never sleep for a threshold, so they stay deterministic in CI.

Polled sources follow the same rule through `SensorPollLoop` and `ChangeGate<T>`:
start/stop/restart, single-flight collection, quiet cancellation and change
detection live in Core, so the Windows-only sources inherit tested behavior
instead of each re-implementing it untested. A scheduled collection that fails
leaves the poller alive rather than silently retiring the sensor until the next
app start, and that too is a test rather than a comment.


## UI automation

Automated WinUI sign-in and notification tests are not a merge gate. They require an
interactive desktop and a real Home Assistant instance, produce brittle timing
failures, and overlap with the manual release smoke test. Keep Windows shims thin and
move decision-making into Core instead.
