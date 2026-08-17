# Research: Home Assistant Examples

## Decision: Use `last_reported` as the liveness signal

**Rationale**: Home Assistant updates `State.last_reported` every time an
integration reports an entity, even when neither state nor attributes change.
That makes it a reliable server-side observation of the companion's existing
periodic sensor reports. `last_updated` can remain unchanged for stable values and
would incorrectly classify a healthy PC as stale.

**Alternatives considered**:

- Add a client timestamp sensor: rejected because it creates a recorder update on
  every sync and duplicates server-maintained information.
- Use `last_updated` or `last_changed`: rejected because unchanged sensor values
  do not advance those timestamps.
- Send a graceful offline state during shutdown: rejected as the primary signal
  because shutdown and network-loss delivery is best effort.

## Decision: Use a state-based template binary sensor

**Rationale**: A template binary sensor can expose standard connectivity
semantics, resolve an exact device name, and compare `now()` with the newest
`last_reported` among that device's sensor entities. Home Assistant re-renders
templates using `now()` once per minute, allowing the status to expire without
messages from the client.

**Alternatives considered**:

- Provision a helper through client-side configuration flows: rejected because it
  requires elevated privileges and relies on configuration surfaces not intended
  as the mobile app protocol.
- Automation plus a manually created helper: rejected because it adds moving
  parts without improving the result.
- Blueprint: rejected because this is an entity template rather than an
  automation, and template blueprint installation is not a fully pre-populated
  one-click helper flow.

## Decision: Default to a three-minute timeout

**Rationale**: Windows Companion normally synchronizes once per minute. A
three-minute threshold tolerates ordinary scheduling jitter and one delayed
report while still surfacing an offline device promptly. The template's
minute-based evaluation can add up to one additional minute before the UI
changes.

**Alternatives considered**:

- One minute: rejected because it can flap around the normal reporting interval.
- Five minutes: safe but slower than needed for the default; users can choose it
  when their environment experiences greater delays.

## Decision: Organize examples by Home Assistant artifact type

**Rationale**: Entity templates and automations have different installation and
import behavior. Separate directories make those differences visible and provide
a stable location for future importable automations. Within each category, one
directory per example keeps its instructions and supporting artifacts together
without forcing later link changes.

**Alternatives considered**:

- One flat examples directory: rejected because installation instructions and
  artifact types become ambiguous as the library grows.
- Blueprints-only structure: rejected because not every reusable Home Assistant
  artifact is a blueprint.

## Evidence

- Home Assistant developer documentation, "New state timestamp
  `State.last_reported`" (2024-03-20).
- Home Assistant templating documentation: templates using `now()` re-run once
  per minute.
- Existing project research in `specs/002-sensor-catalog/research.md` confirms
  that `last_reported` tracks every report and avoids a client timestamp entity.
- `ConnectionManager` defaults the companion sensor synchronization interval to
  60 seconds.
