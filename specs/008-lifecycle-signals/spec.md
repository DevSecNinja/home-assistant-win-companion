# Feature Specification: Lifecycle Signals

**Status**: Shipped

Detect sleep, sign-out and shutdown without polling, report a best-effort final
state to Home Assistant, and report anything undelivered after the next successful
connection.

## Requirements

- Report one `system_state` sensor: `running`, `sleeping`, `signing_out` or
  `shutting_down`, with the Windows reason and a critical flag as attributes.
- Ship the sensor switched off, describe its limits in the catalog entry, mark it
  as best effort in the list, and confirm those limits in a dialog before the first
  time it is switched on; cancelling leaves it off and saves nothing.
- Observe `WM_POWERBROADCAST`, `WM_QUERYENDSESSION` and `WM_ENDSESSION` on a hidden
  top-level window, plus the equivalent `SystemEvents` notifications; treat the
  overlapping duplicates as idempotent.
- Keep the transition model, deduplication, journal and recovery in
  `HaCompanion.Core` with unit tests; keep the Windows hook thin.
- Record every transition locally before attempting to send it, in a file separate
  from `settings.json`, and never let that write break app exit.
- Attempt exactly one final sensor push, bounded by a two-second timeout, on a
  worker thread.
- Never veto, delay or block shutdown or suspend, and never show UI during one.
- Mark a transition delivered only when Home Assistant accepted a batch that
  contained it.
- Report an unacknowledged transition through attributes after the next successful
  connection, then stop reporting it.
- Cancel a pending final push when the machine resumes.
- Leave the Active and Screen Locked sensors unchanged; do not report lock, unlock
  or fast user switching as lifecycle transitions.
- Release the hook when the sensor is switched off.

## Out of scope

- Guaranteed delivery after power loss, kernel crash or forced termination.
- Distinguishing hibernate from sleep, or restart from shutdown: Windows does not
  expose either distinction before the transition.
- A Windows service, and remote shutdown or restart commands from Home Assistant.

See `docs/windows-lifecycle-signals.md` for the signal table and the reliability
limits.
