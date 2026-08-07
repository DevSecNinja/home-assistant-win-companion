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

## UI automation

Automated WinUI sign-in and notification tests are not a merge gate. They require an
interactive desktop and a real Home Assistant instance, produce brittle timing
failures, and overlap with the manual release smoke test. Keep Windows shims thin and
move decision-making into Core instead.
