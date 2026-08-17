# Implementation Plan: Home Assistant Examples

**Branch**: `devsecninja-offline-automation-examples` | **Date**: 2026-08-17 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/017-home-assistant-examples/spec.md`

## Summary

Add a user-facing Home Assistant example library organized by artifact type.
Ship an offline connectivity template first, based on the newest
server-maintained `last_reported` time among a named companion device's sensors.
Reserve a separate automation category so future importable automation YAML has
a stable destination.

## Technical Context

**Language/Version**: Markdown and Home Assistant YAML/Jinja templates

**Primary Dependencies**: Home Assistant Template integration and state object
`last_reported` support

**Storage**: Version-controlled example and documentation files

**Testing**: Static review plus Home Assistant template evaluation and an
end-to-end stale/resume scenario

**Target Platform**: Home Assistant 2024.4 or newer with Windows Companion sensor
reporting enabled

**Project Type**: Documentation and configuration examples for a Windows desktop
application

**Performance Goals**: Connectivity changes within one Home Assistant
minute-based template evaluation after the configured timeout

**Constraints**: No additional client timestamp sensor, heartbeat traffic,
undocumented Home Assistant API, secrets, or personal entity identifiers

**Scale/Scope**: One initial template example, one examples index, and reserved
structure for future automation examples

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

- **Native Windows Experience First**: Pass. The feature documents Home Assistant
  configuration and does not alter or embed the Home Assistant frontend.
- **Security & Privacy**: Pass. Examples use placeholders and require no secrets,
  server URLs, or additional transmitted data.
- **Evidence-Driven Development**: Pass. Home Assistant's documented
  `last_reported` behavior and existing project protocol research support the
  design; the feature has proportional specification artifacts.
- **Testable, Layered Architecture**: Pass. No application architecture changes;
  the example has reproducible stale and recovery scenarios.
- **Resilience & Observability**: Pass. The example explicitly handles abrupt
  shutdown and network loss without depending on graceful disconnect delivery.
- **Documented integration only**: Pass. The design uses documented template and
  state behavior, not private provisioning endpoints.

Post-design re-check: all gates remain passed. No complexity exceptions are
required.

## Project Structure

### Documentation (this feature)

```text
specs/017-home-assistant-examples/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── example-format.md
└── tasks.md
```

### Source Code (repository root)

```text
examples/
└── home-assistant/
    ├── README.md
    ├── templates/
    │   ├── README.md
    │   └── device-connectivity/
    │       ├── README.md
    │       └── template.yaml
    └── automations/
        └── README.md

README.md
```

**Structure Decision**: Keep user-installable Home Assistant material under
`examples/home-assistant/`, grouped by artifact type and then by example. Each
example owns its instructions and supporting artifacts in one stable directory.
A tracked automation index reserves the category without publishing a
placeholder automation. Link the library from the repository's primary
documentation table.

## Complexity Tracking

No constitution violations.
