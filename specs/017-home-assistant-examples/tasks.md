# Tasks: Home Assistant Examples

**Input**: Design documents from `specs/017-home-assistant-examples/`
**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`,
`contracts/example-format.md`, `quickstart.md`

## Phase 1: Setup

No application setup or shared infrastructure changes are required. The feature
consists entirely of independently reviewable documentation and Home Assistant
configuration artifacts.

---

## Phase 2: Foundational

No foundational code is required. The example format and evidence are already
defined in `specs/017-home-assistant-examples/contracts/example-format.md` and
`specs/017-home-assistant-examples/research.md`.

---

## Phase 3: User Story 1 - Add Offline Device Status (Priority: P1) 🎯 MVP

**Goal**: Provide a ready-to-copy connectivity template based on the newest
`last_reported` among a named companion device's sensors.

**Independent Test**: Configure the example with an exact companion device name,
stop reports, and confirm disconnected and recovered transitions occur without
an entity ID or client timestamp.

- [X] T001 [P] [US1] Create the three-minute connectivity binary sensor configuration in `examples/home-assistant/templates/device-connectivity/template.yaml`
- [X] T002 [US1] Document prerequisites, installation, customization, timing, failure behavior, recovery, and removal in `examples/home-assistant/templates/device-connectivity/README.md`

---

## Phase 4: User Story 2 - Discover Compatible Examples (Priority: P2)

**Goal**: Let users navigate examples by artifact type and understand each
category's installation behavior.

**Independent Test**: Starting at the examples index, locate both categories and
identify how the connectivity template is installed and how future automations
will be presented.

- [X] T003 [P] [US2] Create the category-based examples index in `examples/home-assistant/README.md`
- [X] T004 [P] [US2] Reserve the automation category and describe supported future import metadata in `examples/home-assistant/automations/README.md`

---

## Phase 5: User Story 3 - Contribute Future Automations (Priority: P3)

**Goal**: Give contributors a stable contract for adding future automations
without reorganizing existing examples.

**Independent Test**: Classify a hypothetical automation using the documented
rules and confirm its path and required installation/import details are
unambiguous.

- [X] T005 [US3] Add the contributor example checklist and naming guidance to `examples/home-assistant/README.md`

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T006 [P] Link the Home Assistant examples library from the documentation table in `README.md`
- [X] T007 Validate the documentation, placeholders, YAML shape, and stale/recovery procedure against `specs/017-home-assistant-examples/quickstart.md`

---

## Dependencies

- User Story 1, User Story 2, and User Story 3 have no implementation dependency
  on application code.
- T002 depends on T001 so its instructions match the delivered configuration.
- T005 depends on T003 because both update the examples index.
- T006 can run in parallel with all user-story work.
- T007 depends on T001 through T006.

## Parallel Execution Examples

### User Story 1

T001 is the configuration artifact. Once complete, T002 documents its exact
placeholders and behavior.

### User Story 2

T003 and T004 modify different files and can be completed in parallel.

### User Story 3

T005 is independently reviewable after the examples index from T003 exists.

## Implementation Strategy

1. Complete T001-T002 as the MVP, producing a usable offline status example.
2. Complete T003-T004 to expose the category structure.
3. Complete T005 so future automation contributions follow a stable convention.
4. Complete T006-T007 for discoverability and end-to-end validation.

All tasks follow the required checklist format and include explicit file paths.

---

## Phase 7: Per-Example Directories

- [X] T008 Move the connectivity template and its guidance into `examples/home-assistant/templates/device-connectivity/`
- [X] T009 Add the template category index and update structural references in `examples/home-assistant/templates/README.md`, `examples/home-assistant/README.md`, and `specs/017-home-assistant-examples/`

- [X] T010 Resolve the heartbeat by exact device name and newest sensor report in `examples/home-assistant/templates/device-connectivity/template.yaml`
- [X] T011 Trim control-block whitespace from the rendered state in `examples/home-assistant/templates/device-connectivity/template.yaml`
- [X] T012 Accept apostrophes in device names and document name uniqueness in `examples/home-assistant/templates/device-connectivity/`
- [X] T013 Document the Home Assistant restart false-positive window in `examples/home-assistant/templates/device-connectivity/README.md`
