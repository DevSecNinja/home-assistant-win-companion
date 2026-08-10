# Feature Specification: WinGet Update Status

**Feature Branch**: `feature/004-winget-updates`

**Created**: 2026-08-07

**Status**: Shipped

**Input**: User description: "Add an opt-in sensor for available WinGet package
updates using the official Microsoft.WinGet.Client PowerShell module."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See available application updates (Priority: P1)

As a Home Assistant user, I want my PC to report how many WinGet-managed
applications have updates available so I can include software maintenance in device
health automations.

**Why this priority**: The count is the useful Home Assistant signal and provides
value without exposing the installed software inventory.

**Independent Test**: Enable the sensor on a PC with known WinGet updates and verify
that Home Assistant receives the same count reported by the WinGet client module.

**Acceptance Scenarios**:

1. **Given** the required WinGet client module is installed, **When** a check
   completes, **Then** the sensor reports the number of packages with updates.
2. **Given** no updates are available, **When** a check completes, **Then** the
   sensor reports zero.
3. **Given** the check cannot run, **When** the sensor reports, **Then** its state is
   unavailable rather than zero.

---

### User Story 2 - Keep software inventory private (Priority: P2)

As a privacy-conscious user, I want package names and versions to remain on my PC
while still being able to inspect what contributes to the count.

**Why this priority**: Installed software is sensitive inventory data and must not
be transmitted merely to provide a maintenance count.

**Independent Test**: Inspect outgoing sensor payloads and confirm they contain only
the count, then inspect the Sensors page and confirm package details are shown
locally after a successful enabled check.

**Acceptance Scenarios**:

1. **Given** updates are available, **When** Home Assistant receives the sensor,
   **Then** no package names, identifiers, or versions are present.
2. **Given** the enabled sensor has completed a check, **When** the user opens the
   Sensors page, **Then** the local preview identifies the packages with updates.
3. **Given** the sensor is disabled, **When** the Sensors page is opened, **Then**
   it explains that enablement is required and performs no WinGet query.
4. **Given** the required module is missing, **When** the user enables the sensor,
   **Then** the app explains the dependency and provides a copyable current-user
   installation command.
5. **Given** the user closes or copies the setup instructions, **When** enablement
   finishes, **Then** the sensor remains disabled until the module is installed.
6. **Given** the displayed current-user command installs the module while the
   companion remains open, **When** the user selects Recheck or enables the sensor
   again, **Then** a fresh probe discovers it without an app restart.

---

### User Story 3 - Control expensive checks (Priority: P3)

As a user, I want update checks to run infrequently and on demand so they do not
slow the app or repeatedly refresh remote package sources.

**Why this priority**: WinGet checks are much heavier than normal sensor reads and
must not run on the one-minute sensor synchronization interval.

**Independent Test**: Observe an enabled sensor through multiple normal sync cycles,
confirm no repeated WinGet process is created, then use Update now and confirm one
fresh check occurs.

**Acceptance Scenarios**:

1. **Given** the sensor is enabled, **When** normal sensor syncs occur, **Then**
   cached results are used without starting a new check.
2. **Given** the sensor remains enabled, **When** six hours pass, **Then** one
   scheduled refresh occurs.
3. **Given** the user selects Update now, **When** the refresh completes, **Then**
   the new count is included in the manual push.
4. **Given** the sensor is disabled, **When** any amount of time passes, **Then** no
   WinGet checks are performed.

### Edge Cases

- The `Microsoft.WinGet.Client` PowerShell module is not installed or cannot load.
- The installed module is too old or is not signed by Microsoft.
- The companion inherited a stale or host-specific `PSModulePath` before the module
  was installed.
- Windows PowerShell 5.1 is unavailable for the companion's architecture.
- The module is discoverable but cannot be imported.
- WinGet itself is unavailable, disabled by policy, or its catalog connection fails.
- A check exceeds a reasonable timeout.
- The PowerShell process exits unsuccessfully or emits malformed structured output.
- Package names contain punctuation or non-ASCII characters.
- The sensor is disabled while a check is running.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The catalog MUST provide a `winget_updates` diagnostic sensor.
- **FR-002**: The sensor MUST be disabled by default.
- **FR-003**: Checks MUST use the official `Microsoft.WinGet.Client` PowerShell
  module and structured output; localized CLI tables MUST NOT be parsed.
- **FR-003a**: Enabling the sensor while the module is absent MUST explain the
  official dependency and provide a copyable PowerShell Gallery command using
  current-user scope.
- **FR-003b**: The app MUST NOT download, install, or update the PowerShell module.
- **FR-003c**: The sensor MUST remain disabled until a supported Microsoft-signed
  module is detected.
- **FR-003d**: Installation guidance and runtime probing MUST use Windows PowerShell
  5.1 and MUST resolve its CurrentUser module directory consistently.
- **FR-003e**: Every enablement or explicit recheck MUST perform a new capability
  probe. Missing, incompatible, untrusted, host-unavailable, import, probe, and
  command failures MUST remain distinguishable.
- **FR-004**: Home Assistant MUST receive only the available-update count or an
  unavailable state.
- **FR-005**: Package names, identifiers, installed versions, and available versions
  MUST remain local and MUST NOT be included in sensor attributes or logs.
- **FR-006**: The local Sensors page MUST show package details after a successful
  enabled check.
- **FR-007**: A disabled sensor MUST perform no WinGet or PowerShell queries,
  including during local preview.
- **FR-008**: An enabled sensor MUST refresh at startup, every six hours, and when
  the user invokes Update now.
- **FR-009**: Normal periodic sensor synchronization MUST use the cached result.
- **FR-010**: Missing module after setup, timeout, policy, source, and
  malformed-output failures MUST produce a distinguishable unavailable result
  without breaking other sensors.
- **FR-011**: Disabling the sensor MUST cancel an in-progress check where possible
  and stop future scheduled checks.
- **FR-012**: Installing or updating packages MUST remain out of scope.

### Key Entities

- **Update summary**: Check status, available-update count, local package details,
  check time, and local error description.
- **Package update**: Package name, identifier, installed version, and newest
  available version retained only in memory for local display.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The reported count matches the official WinGet client module for all
  packages found during manual verification.
- **SC-002**: No more than one automatic check occurs in any six-hour period.
- **SC-003**: Update now completes or reports a timeout within two minutes.
- **SC-004**: With the sensor disabled, zero PowerShell processes are created by
  this feature.
- **SC-005**: Inspection of Home Assistant payloads and logs reveals no package
  names, identifiers, or versions.
- **SC-006**: Missing-module and source-failure cases are distinguishable from zero
  available updates.
- **SC-007**: Viewing or copying setup instructions creates no sensor preference;
  after explicit installation the sensor can be enabled without restarting the app.
- **SC-008**: A long-lived companion process discovers a module newly installed into
  either standard CurrentUser PowerShell module directory without changing the
  machine or user environment.

## Assumptions

- Users install `Microsoft.WinGet.Client` explicitly from PowerShell Gallery for
  their current Windows account.
- Windows PowerShell 5.1 is available at its standard Windows path.
- Update details are held only in memory and disappear when the app exits.
- A two-minute timeout and six-hour refresh interval balance freshness and cost.
